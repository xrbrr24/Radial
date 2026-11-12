using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Radial.Core.Diagnostics;

namespace Radial.Windows.Diagnostics;

/// <summary>
/// El registro persistente de Radial: archivos de texto en una carpeta (en producción <c>%LOCALAPPDATA%\Radial\logs\</c>), uno por día
/// (<c>radial-aaaaMMdd-NNN.log</c>) que pasa al siguiente número al llegar a <c>maxFileBytes</c>, con retención por número de archivos y
/// por tamaño total (<see cref="LogRetention"/>). Sin dependencias externas.
///
/// Garantías:
/// - <see cref="Write"/> no lanza, no bloquea (formatea y encola) y se puede llamar desde cualquier hilo, también desde un hilo de
///   trabajo. Un hilo propio escribe en disco. La cola está acotada: si se llena (disco parado) se descartan entradas y se cuenta
///   (<see cref="DroppedEntries"/>), nunca se bloquea a quien registra ni crece la memoria.
/// - Un fallo del propio registro (carpeta imposible de crear, disco lleno, archivo bloqueado) se queda aquí: se cuenta
///   (<see cref="WriteFailures"/>, <see cref="LastFailure"/>), la entrada se pierde y se sigue; nunca se propaga ni provoca otro fallo.
/// - Las entradas <see cref="LogLevel.Critical"/> esperan (hasta 2 s) a estar escritas antes de volver, porque normalmente el proceso
///   está a punto de terminar. <see cref="Dispose"/> y <see cref="Flush"/> vacían la cola, así que no se pierden los últimos
///   registros del cierre.
/// - El texto pasa por <see cref="LogSanitizer"/> y los archivos se abren con uso compartido (se pueden leer mientras Radial corre).
/// </summary>
public sealed class FileRadialLog : IRadialLog, IDisposable
{
    private const int MaxBatch = 200;
    private static readonly TimeSpan CriticalFlushTimeout = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly LogSanitizer _sanitizer;
    private readonly Func<DateTimeOffset> _now;
    private readonly int _maxFiles;
    private readonly long _maxTotalBytes;
    private readonly long _maxFileBytes;
    private readonly BlockingCollection<Item> _queue;
    private readonly Thread _thread;

    // Estado del hilo de escritura (solo lo toca ese hilo).
    private DateOnly _currentDate;
    private int _sequence;
    private long _currentSize;
    private bool _positioned;
    private bool _retentionDue;
    private string? _currentName;
    private long _reportedDropped;

    private volatile bool _disposed;
    private long _dropped;
    private int _failures;
    private string? _lastFailure;

    /// <param name="directory">Carpeta de los registros; se crea si no existe.</param>
    /// <param name="sanitizer">Limpieza de datos sensibles; por defecto solo recorta direcciones y longitud.</param>
    /// <param name="now">Reloj de las marcas de tiempo (las pruebas lo fijan).</param>
    public FileRadialLog(
        string directory,
        LogSanitizer? sanitizer = null,
        Func<DateTimeOffset>? now = null,
        int maxFiles = LogRetention.DefaultMaxFiles,
        long maxTotalBytes = LogRetention.DefaultMaxTotalBytes,
        long maxFileBytes = LogRetention.DefaultMaxFileBytes,
        int queueCapacity = 2000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTotalBytes, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 64L);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueCapacity, 1);

        _directory = Path.GetFullPath(directory);
        _sanitizer = sanitizer ?? LogSanitizer.Default;
        _now = now ?? (() => DateTimeOffset.Now);
        _maxFiles = maxFiles;
        _maxTotalBytes = maxTotalBytes;
        _maxFileBytes = maxFileBytes;
        _queue = new BlockingCollection<Item>(queueCapacity);
        _thread = new Thread(Run) { IsBackground = true, Name = "Radial.Log" };
        _thread.Start();
    }

    /// <summary>La carpeta donde se escriben los registros.</summary>
    public string LogDirectory => _directory;

    /// <summary>Entradas descartadas porque la cola estaba llena.</summary>
    public long DroppedEntries => Interlocked.Read(ref _dropped);

    /// <summary>Veces que el propio registro no pudo escribir (carpeta, disco, bloqueo...).</summary>
    public int WriteFailures => Volatile.Read(ref _failures);

    /// <summary>Qué falló la última vez (tipo de excepción), o <c>null</c>.</summary>
    public string? LastFailure => Volatile.Read(ref _lastFailure);

    public void Write(LogLevel level, string source, string message, Exception? exception = null, string? operation = null, string? context = null)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var entry = new LogEntry(_now(), level, source, message, operation, exception, context, Environment.CurrentManagedThreadId);
            if (!_queue.TryAdd(new Item(LogFormatter.Format(entry, _sanitizer), null)))
            {
                Interlocked.Increment(ref _dropped);
            }

            if (level >= LogLevel.Critical)
            {
                Flush(CriticalFlushTimeout);
            }
        }
        catch (Exception exceptionWhileLogging)
        {
            RecordFailure(exceptionWhileLogging);
        }
    }

    /// <summary>Espera a que todo lo anterior esté escrito. Devuelve <c>false</c> si no se consiguió a tiempo (o el registro está cerrado).</summary>
    public bool Flush(TimeSpan timeout)
    {
        if (_disposed)
        {
            return false;
        }

        try
        {
            using var done = new ManualResetEventSlim(false);
            var marker = new Item(null, done);
            if (!_queue.TryAdd(marker, 100))
            {
                return false;
            }

            return done.Wait(timeout);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            return false;
        }
    }

    /// <summary>Vacía la cola y cierra. Espera hasta 3 s a que se termine de escribir; lo que quede se pierde, pero no se bloquea el cierre.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _queue.CompleteAdding();
            if (_thread.Join(TimeSpan.FromSeconds(3)))
            {
                _queue.Dispose();
            }
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
    }

    // ───────────────────────── Hilo de escritura ─────────────────────────

    private void Run()
    {
        try
        {
            SafeRetention();
            foreach (var first in _queue.GetConsumingEnumerable())
            {
                var lines = new List<string>(16);
                var flushes = new List<ManualResetEventSlim>(2);
                Collect(first, lines, flushes);
                while (lines.Count + flushes.Count < MaxBatch && _queue.TryTake(out var next))
                {
                    Collect(next, lines, flushes);
                }

                try
                {
                    WriteBatch(lines);
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                }

                foreach (var flush in flushes)
                {
                    try
                    {
                        flush.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Quien esperaba ya se rindió (tiempo agotado) y liberó su señal: no hay nadie a quien avisar.
                    }
                }
            }
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
    }

    private static void Collect(Item item, List<string> lines, List<ManualResetEventSlim> flushes)
    {
        if (item.Text is not null)
        {
            lines.Add(item.Text);
        }

        if (item.Flush is not null)
        {
            flushes.Add(item.Flush);
        }
    }

    private void WriteBatch(List<string> lines)
    {
        var dropped = Interlocked.Read(ref _dropped);
        if (dropped > _reportedDropped)
        {
            var lost = dropped - _reportedDropped;
            _reportedDropped = dropped;
            lines.Insert(0, LogFormatter.Format(
                new LogEntry(_now(), LogLevel.Warning, nameof(FileRadialLog), $"Se descartaron {lost} entradas del registro porque la cola estaba llena."), _sanitizer));
        }

        if (lines.Count == 0)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(_directory);
        var date = DateOnly.FromDateTime(_now().DateTime);
        if (!_positioned || date != _currentDate)
        {
            Position(date);
        }

        FileStream? stream = null;
        try
        {
            foreach (var line in lines)
            {
                var bytes = Utf8NoBom.GetBytes(line + Environment.NewLine);
                if (_currentSize > 0 && _currentSize + bytes.Length > _maxFileBytes)
                {
                    stream?.Dispose();
                    stream = null;
                    _sequence++;
                    _currentSize = 0;
                    _currentName = LogRetention.FileName(_currentDate, _sequence);
                    _retentionDue = true;
                }

                stream ??= new FileStream(Path.Combine(_directory, _currentName!), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write(bytes);
                _currentSize += bytes.Length;
            }
        }
        finally
        {
            stream?.Dispose();
            if (_retentionDue)
            {
                // Con el archivo del día ya creado: así cuenta en el límite y la retención es exacta.
                _retentionDue = false;
                SafeRetention();
            }
        }
    }

    // Elige el archivo del día: el de mayor número que ya exista (se sigue escribiendo en él) o el 001.
    private void Position(DateOnly date)
    {
        _currentDate = date;
        _sequence = 1;
        _currentSize = 0;
        foreach (var path in System.IO.Directory.GetFiles(_directory, $"radial-{date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-*.log"))
        {
            var name = Path.GetFileName(path);
            if (LogRetention.IsLogFile(name)
                && int.TryParse(name.AsSpan(name.Length - 7, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number > _sequence)
            {
                _sequence = number;
            }
        }

        _currentName = LogRetention.FileName(date, _sequence);
        var current = new FileInfo(Path.Combine(_directory, _currentName));
        _currentSize = current.Exists ? current.Length : 0;
        _positioned = true;
        _retentionDue = true;
    }

    private void SafeRetention()
    {
        try
        {
            if (!System.IO.Directory.Exists(_directory))
            {
                return;
            }

            var files = new DirectoryInfo(_directory).GetFiles("radial-*.log")
                .Select(file => new LogFileInfo(file.Name, file.Length))
                .ToList();
            foreach (var name in LogRetention.SelectToDelete(files, _maxFiles, _maxTotalBytes))
            {
                if (string.Equals(name, _currentName, StringComparison.Ordinal))
                {
                    continue; // nunca se borra el archivo en el que se está escribiendo
                }

                try
                {
                    File.Delete(Path.Combine(_directory, name));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Un archivo abierto por otro programa: se intentará en la próxima retención.
                }
            }
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
    }

    private void RecordFailure(Exception exception)
    {
        Interlocked.Increment(ref _failures);
        Volatile.Write(ref _lastFailure, exception.GetType().Name);
    }

    private sealed record Item(string? Text, ManualResetEventSlim? Flush);
}
