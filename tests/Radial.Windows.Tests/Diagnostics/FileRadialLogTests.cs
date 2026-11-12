using Radial.Core.Diagnostics;
using Radial.Windows.Diagnostics;

namespace Radial.Windows.Tests.Diagnostics;

/// <summary>
/// El registro persistente en disco real (una carpeta temporal propia de cada prueba): escritura y formato, creación de carpetas,
/// rotación, retención, vaciado al cerrar y, sobre todo, que sus propios fallos no se propaguen ni provoquen otros.
/// </summary>
public sealed class FileRadialLogTests : IDisposable
{
    private static readonly DateTimeOffset Day = new(2026, 11, 10, 14, 3, 22, 123, TimeSpan.FromHours(1));
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "radial-log-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _open = [];

    public void Dispose()
    {
        foreach (var log in _open)
        {
            log.Dispose();
        }

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // La limpieza de la carpeta temporal no puede hacer fallar una prueba.
        }
    }

    private FileRadialLog NewLog(
        string? directory = null,
        LogSanitizer? sanitizer = null,
        Func<DateTimeOffset>? now = null,
        int maxFiles = LogRetention.DefaultMaxFiles,
        long maxTotalBytes = LogRetention.DefaultMaxTotalBytes,
        long maxFileBytes = LogRetention.DefaultMaxFileBytes)
    {
        var log = new FileRadialLog(directory ?? Path.Combine(_root, "logs"), sanitizer, now ?? (() => Day), maxFiles, maxTotalBytes, maxFileBytes);
        _open.Add(log);
        return log;
    }

    private string TodayFile(int sequence = 1) => Path.Combine(_root, "logs", LogRetention.FileName(new DateOnly(2026, 11, 10), sequence));

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void AnEntry_IsWrittenInTheDailyFile_WithTheAgreedFormat()
    {
        var log = NewLog();

        log.Write(LogLevel.Warning, "ElOrigen", "hola mundo", operation: "una operación");
        Assert.True(log.Flush(Patience));

        var text = ReadShared(TodayFile());
        Assert.Contains("2026-11-10T14:03:22.123+01:00 WRN [ElOrigen] t", text);
        Assert.Contains("op=una operación | hola mundo", text);
    }

    [Fact]
    public void TheFolder_IsCreatedWhenItDoesNotExist()
    {
        var nested = Path.Combine(_root, "a", "b", "c");
        var log = NewLog(nested);

        log.Write(LogLevel.Information, "X", "m");
        Assert.True(log.Flush(Patience));

        Assert.True(Directory.Exists(nested));
        Assert.Single(Directory.GetFiles(nested, "radial-*.log"));
    }

    [Fact]
    public void AnException_AndTheContext_AreWrittenInFull()
    {
        var log = NewLog();
        Exception thrown;
        try
        {
            throw new InvalidOperationException("detalle del fallo");
        }
        catch (Exception caught)
        {
            thrown = caught;
        }

        log.Write(LogLevel.Error, "X", "falló", thrown, "op", "09:00:00.000 antes");
        Assert.True(log.Flush(Patience));

        var text = ReadShared(TodayFile());
        Assert.Contains("! System.InvalidOperationException: detalle del fallo", text);
        Assert.Contains("ctx: 09:00:00.000 antes", text);
    }

    [Fact]
    public void TheUserProfileFolder_NeverReachesTheDisk()
    {
        var log = NewLog(sanitizer: new LogSanitizer(@"C:\Users\nombreunico"));

        log.Write(LogLevel.Error, "X", @"No se pudo abrir C:\Users\nombreunico\secreto.txt (https://x.example/p?token=abc)");
        Assert.True(log.Flush(Patience));

        var text = ReadShared(TodayFile());
        Assert.DoesNotContain("nombreunico", text);
        Assert.DoesNotContain("token=abc", text);
        Assert.Contains("%USERPROFILE%", text);
    }

    [Fact]
    public void Disposing_WritesWhatWasStillQueued()
    {
        var log = NewLog();
        for (var index = 0; index < 150; index++)
        {
            log.Write(LogLevel.Information, "X", "entrada-" + index);
        }

        log.Dispose(); // sin Flush antes: el cierre no pierde los últimos registros

        var text = ReadShared(TodayFile());
        Assert.Contains("entrada-0", text);
        Assert.Contains("entrada-149", text);
        Assert.Equal(150, text.Split('\n').Count(line => line.Contains("| entrada-", StringComparison.Ordinal)));
    }

    [Fact]
    public void ACriticalEntry_IsOnDiskWhenWriteReturns()
    {
        var log = NewLog();

        log.Write(LogLevel.Critical, "X", "error grave");

        Assert.Contains("CRT [X]", ReadShared(TodayFile())); // sin Flush: el proceso podría terminar justo después
    }

    [Fact]
    public void ManyThreadsWritingAtOnce_LoseNothing()
    {
        var log = NewLog();

        Parallel.For(0, 300, index => log.Write(LogLevel.Information, "X", "paralelo-" + index));
        Assert.True(log.Flush(Patience));

        var text = ReadShared(TodayFile());
        Assert.Equal(300, text.Split('\n').Count(line => line.Contains("| paralelo-", StringComparison.Ordinal)));
        Assert.Equal(0, log.DroppedEntries);
        Assert.Equal(0, log.WriteFailures);
    }

    [Fact]
    public void WhenAFileReachesItsSize_TheLogContinuesInTheNextOne()
    {
        var log = NewLog(maxFileBytes: 300);

        for (var index = 0; index < 12; index++)
        {
            log.Write(LogLevel.Information, "Origen", "relleno-" + index + " " + new string('z', 60));
        }

        Assert.True(log.Flush(Patience));

        var files = Directory.GetFiles(Path.Combine(_root, "logs"), "radial-*.log").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.True(files.Length >= 3, "Se esperaban varios archivos y hay " + files.Length);
        Assert.Equal(LogRetention.FileName(new DateOnly(2026, 11, 10), 1), files[0]);
        Assert.Equal(LogRetention.FileName(new DateOnly(2026, 11, 10), 2), files[1]);
        var allText = string.Concat(files.Select(name => ReadShared(Path.Combine(_root, "logs", name!))));
        Assert.Equal(12, allText.Split('\n').Count(line => line.Contains("| relleno-", StringComparison.Ordinal)));
    }

    [Fact]
    public void AfterARestart_TheLogKeepsWritingInTheLatestFileOfTheDay()
    {
        var logs = Path.Combine(_root, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, LogRetention.FileName(new DateOnly(2026, 11, 10), 1)), "antiguo\n");
        File.WriteAllText(Path.Combine(logs, LogRetention.FileName(new DateOnly(2026, 11, 10), 3)), "ultimo\n");
        var log = NewLog();

        log.Write(LogLevel.Information, "X", "después del reinicio");
        Assert.True(log.Flush(Patience));

        Assert.Contains("después del reinicio", ReadShared(Path.Combine(logs, LogRetention.FileName(new DateOnly(2026, 11, 10), 3))));
        Assert.False(File.Exists(Path.Combine(logs, LogRetention.FileName(new DateOnly(2026, 11, 10), 4))));
    }

    [Fact]
    public void ANewDay_StartsANewFile()
    {
        var current = Day;
        var log = NewLog(now: () => current);
        log.Write(LogLevel.Information, "X", "primer día");
        Assert.True(log.Flush(Patience));

        current = Day.AddDays(1);
        log.Write(LogLevel.Information, "X", "segundo día");
        Assert.True(log.Flush(Patience));

        Assert.Contains("primer día", ReadShared(TodayFile()));
        Assert.Contains("segundo día", ReadShared(Path.Combine(_root, "logs", LogRetention.FileName(new DateOnly(2026, 11, 11), 1))));
    }

    [Fact]
    public void TheRetention_DeletesTheOldestLogs_AndLeavesEveryOtherFileAlone()
    {
        var logs = Path.Combine(_root, "logs");
        Directory.CreateDirectory(logs);
        for (var day = 1; day <= 20; day++)
        {
            File.WriteAllText(Path.Combine(logs, LogRetention.FileName(new DateOnly(2026, 10, day), 1)), "viejo\n");
        }

        File.WriteAllText(Path.Combine(logs, "last-fatal.txt"), "marcador");
        File.WriteAllText(Path.Combine(logs, "notas.txt"), "del usuario");
        var log = NewLog(maxFiles: 5);

        log.Write(LogLevel.Information, "X", "hoy");
        Assert.True(log.Flush(Patience));

        var remaining = Directory.GetFiles(logs, "radial-*.log").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(5, remaining.Length);
        Assert.Contains(LogRetention.FileName(new DateOnly(2026, 11, 10), 1), remaining); // el de hoy nunca se borra
        Assert.DoesNotContain(LogRetention.FileName(new DateOnly(2026, 10, 1), 1), remaining);
        Assert.True(File.Exists(Path.Combine(logs, "last-fatal.txt")));
        Assert.True(File.Exists(Path.Combine(logs, "notas.txt")));
    }

    [Fact]
    public void AFolderThatCannotBeCreated_NeverThrows_AndIsCounted()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "es-un-archivo");
        File.WriteAllText(blocker, "x");
        var log = NewLog(Path.Combine(blocker, "logs")); // una carpeta «dentro» de un archivo: imposible

        var thrown = Record.Exception(() =>
        {
            log.Write(LogLevel.Error, "X", "uno");
            log.Write(LogLevel.Critical, "X", "dos");
            log.Flush(Patience);
            log.Write(LogLevel.Information, "X", "tres");
        });

        Assert.Null(thrown);
        Assert.True(log.WriteFailures >= 1);
        Assert.NotNull(log.LastFailure);
    }

    [Fact]
    public void ALockedFile_LosesThatBatch_ButTheNextEntriesAreWrittenWhenItIsFree()
    {
        Directory.CreateDirectory(Path.Combine(_root, "logs"));
        var log = NewLog();
        var locked = new FileStream(TodayFile(), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        try
        {
            log.Write(LogLevel.Error, "X", "mientras-bloqueado");
            Assert.True(log.Flush(Patience));
            Assert.True(log.WriteFailures >= 1);
        }
        finally
        {
            locked.Dispose();
        }

        log.Write(LogLevel.Error, "X", "ya-libre");
        Assert.True(log.Flush(Patience));

        var text = ReadShared(TodayFile());
        Assert.Contains("ya-libre", text);
        Assert.DoesNotContain("mientras-bloqueado", text);
    }

    [Fact]
    public void WritingAfterDisposing_IsIgnored_WithoutError()
    {
        var log = NewLog();
        log.Dispose();

        var thrown = Record.Exception(() =>
        {
            log.Write(LogLevel.Error, "X", "tarde");
            Assert.False(log.Flush(TimeSpan.FromMilliseconds(100)));
            log.Dispose();
        });

        Assert.Null(thrown);
        Assert.Equal(0, log.WriteFailures);
    }

    [Fact]
    public void InvalidArguments_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new FileRadialLog(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileRadialLog(_root, maxFiles: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileRadialLog(_root, maxTotalBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileRadialLog(_root, maxFileBytes: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileRadialLog(_root, queueCapacity: 0));
    }
}
