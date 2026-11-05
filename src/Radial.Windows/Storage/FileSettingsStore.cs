using System.Text;
using Radial.Core.Catalog;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Core.Validation;

namespace Radial.Windows.Storage;

/// <summary>
/// Almacén de la configuración en disco: <c>%LOCALAPPDATA%\Radial\</c> con <c>config.json</c>, <c>backups\</c>, <c>icons\</c> y
/// <c>logs\</c> (sección 7 del documento técnico). El JSON, las migraciones y la validación son de Radial.Core
/// (<see cref="ConfigurationFile"/>); aquí está lo que toca el disco:
/// - Escritura atómica: se escribe un temporal en la misma carpeta. Primera escritura (el destino no existe): <c>File.Move</c> sin
///   sobrescribir. Siguientes: <c>File.Replace</c>, que además deja la versión anterior en <c>backups\</c>.
/// - Copias de seguridad: <c>backups\config-aaaaMMdd-HHmmss-fff.json</c>, las <see cref="MaxBackups"/> más recientes.
/// - Un archivo corrupto no tumba la aplicación: se renombra a <c>config.corrupt-…json</c> (cuarentena, las
///   <see cref="MaxQuarantined"/> más recientes), se restaura la copia VÁLIDA más reciente (solo si es válida) o, si no hay ninguna,
///   se genera la predeterminada. Nunca se sobrescribe un archivo sin ponerlo antes a salvo.
/// - Una configuración de una versión FUTURA no se toca ni se pone en cuarentena: se usan los valores predeterminados solo en
///   memoria y se rechaza guardar mientras dure el proceso, para no destruir datos que una versión más nueva sí entiende.
/// - Un problema de lectura o de escritura del disco (permisos, bloqueo, disco lleno) no es corrupción: se informa y se usan los
///   valores predeterminados en memoria sin tocar el archivo.
/// Ninguna operación lanza por problemas del contenido o del disco. Seguro entre hilos.
/// </summary>
public sealed class FileSettingsStore : ISettingsStore
{
    public const int MaxBackups = 10;
    public const int MaxQuarantined = 10;

    private const string ConfigFileName = "config.json";
    private const string BackupPrefix = "config-";
    private const string QuarantinePrefix = "config.corrupt-";
    private const int MaxReportedIssues = 3;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<RadialConfiguration> _defaults;
    private bool _protected;

    /// <param name="rootDirectory">Carpeta de datos de Radial; se crea si no existe.</param>
    /// <param name="utcNow">Reloj de las marcas de tiempo de copias y cuarentena (las pruebas lo fijan).</param>
    /// <param name="defaults">Origen de la configuración inicial; por defecto <see cref="DefaultConfiguration.Create()"/>.</param>
    public FileSettingsStore(string rootDirectory, Func<DateTimeOffset>? utcNow = null, Func<RadialConfiguration>? defaults = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _defaults = defaults ?? DefaultConfiguration.Create;
    }

    /// <summary><c>%LOCALAPPDATA%\Radial</c>.</summary>
    public static string DefaultRootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Radial");

    public string RootDirectory { get; }

    public string ConfigPath => Path.Combine(RootDirectory, ConfigFileName);

    public string BackupsDirectory => Path.Combine(RootDirectory, "backups");

    public string IconsDirectory => Path.Combine(RootDirectory, "icons");

    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public SettingsLoadResult LoadOrCreate()
    {
        lock (_gate)
        {
            var notices = new List<SettingsNotice>();
            if (!EnsureDirectories(notices))
            {
                return InMemoryDefaults(notices);
            }

            DeleteStaleTemporaries();

            if (!File.Exists(ConfigPath))
            {
                notices.Add(new(SettingsNoticeSeverity.Info, "No había configuración (primer inicio): se genera la predeterminada."));
                return CreateDefaults(notices, SettingsSource.CreatedDefaults);
            }

            var file = ReadAndParse(ConfigPath);
            if (file.IoError is not null)
            {
                notices.Add(new(SettingsNoticeSeverity.Error,
                    $"No se pudo leer la configuración ({file.IoError}). No se modifica el archivo; se usan los valores predeterminados en memoria."));
                return InMemoryDefaults(notices);
            }

            var parsed = file.Result!;
            switch (parsed.Status)
            {
                case ConfigurationParseStatus.Ok:
                    foreach (var warning in parsed.Issues.Where(i => i.Severity == ValidationSeverity.Warning))
                    {
                        notices.Add(new(SettingsNoticeSeverity.Warning, $"{warning.Path}: {warning.Message}"));
                    }

                    return new SettingsLoadResult(parsed.Configuration!, SettingsSource.Loaded, notices, CanSave: true);

                case ConfigurationParseStatus.FutureVersion:
                    _protected = true;
                    notices.Add(new(SettingsNoticeSeverity.Error,
                        $"{Describe(parsed.Issues)} El archivo NO se modifica y no se guardará nada mientras tanto; se usan los valores predeterminados en memoria. Actualiza Radial para usar esa configuración."));
                    return InMemoryDefaults(notices);

                default:
                    return Recover(parsed, notices);
            }
        }
    }

    public SettingsSaveResult Save(RadialConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        lock (_gate)
        {
            if (_protected)
            {
                return SettingsSaveResult.Failed("La configuración del disco es de una versión posterior de Radial y no se sobrescribe.");
            }

            // Toda acción conocida queda enlazada a la biblioteca antes de validar y de escribir.
            configuration = LibraryResolver.Normalize(configuration);

            var validation = ConfigurationValidator.Validate(configuration);
            if (!validation.IsValid)
            {
                return SettingsSaveResult.Failed("La configuración no es válida y no se guarda: " + Describe(validation.Errors));
            }

            byte[] bytes;
            try
            {
                bytes = Utf8NoBom.GetBytes(ConfigurationFile.Serialize(configuration));
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
            {
                return SettingsSaveResult.Failed("No se pudo serializar la configuración: " + exception.GetType().Name + ".");
            }

            if (bytes.Length > ConfigurationFile.MaxBytes)
            {
                return SettingsSaveResult.Failed("La configuración supera el tamaño máximo permitido y no se guarda.");
            }

            var directoryProblems = new List<SettingsNotice>();
            if (!EnsureDirectories(directoryProblems))
            {
                return SettingsSaveResult.Failed(directoryProblems[0].Message);
            }

            var temporary = TemporaryPath();
            try
            {
                WriteTemporary(temporary, bytes);

                if (File.Exists(ConfigPath))
                {
                    if (HasSameContent(ConfigPath, bytes))
                    {
                        TryDelete(temporary);
                        return SettingsSaveResult.Unchanged;
                    }

                    // File.Replace deja la versión anterior en el destino de la copia de seguridad.
                    File.Replace(temporary, ConfigPath, NextBackupPath());
                    PruneBackups();
                }
                else
                {
                    File.Move(temporary, ConfigPath);
                }

                return SettingsSaveResult.Saved;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                TryDelete(temporary);
                return SettingsSaveResult.Failed("No se pudo guardar la configuración: " + exception.GetType().Name + ".");
            }
        }
    }

    // Recuperación

    private SettingsLoadResult Recover(ConfigurationParseResult problem, List<SettingsNotice> notices)
    {
        notices.Add(new(SettingsNoticeSeverity.Warning,
            $"La configuración está dañada o no es válida y se pone en cuarentena. {Describe(problem.Issues)}"));

        var quarantined = Quarantine();
        if (quarantined is null)
        {
            notices.Add(new(SettingsNoticeSeverity.Error,
                "No se pudo poner el archivo en cuarentena; no se modifica y se usan los valores predeterminados en memoria."));
            return InMemoryDefaults(notices);
        }

        notices.Add(new(SettingsNoticeSeverity.Info, $"Archivo original conservado como «{Path.GetFileName(quarantined)}»."));

        foreach (var backup in ListNewestFirst(BackupsDirectory, BackupPrefix))
        {
            var candidate = ReadAndParse(backup);
            if (candidate.Result is { Status: ConfigurationParseStatus.Ok } valid && candidate.Bytes is not null)
            {
                if (TryWriteNew(candidate.Bytes))
                {
                    notices.Add(new(SettingsNoticeSeverity.Warning, $"Configuración restaurada desde la copia «{Path.GetFileName(backup)}»."));
                    return new SettingsLoadResult(valid.Configuration!, SettingsSource.RecoveredFromBackup, notices, CanSave: true);
                }

                notices.Add(new(SettingsNoticeSeverity.Error, "Se encontró una copia válida pero no se pudo restaurar; se usan los valores predeterminados en memoria."));
                return InMemoryDefaults(notices);
            }

            notices.Add(new(SettingsNoticeSeverity.Info, $"La copia «{Path.GetFileName(backup)}» no es utilizable y se omite."));
        }

        notices.Add(new(SettingsNoticeSeverity.Warning, "No había ninguna copia válida: se genera la configuración predeterminada."));
        return CreateDefaults(notices, SettingsSource.DefaultsAfterProblem);
    }

    /// <returns>La ruta de la cuarentena, o <c>null</c> si no se pudo mover.</returns>
    private string? Quarantine()
    {
        try
        {
            var stamp = _utcNow().UtcDateTime;
            for (var attempt = 0; attempt < 1000; attempt++)
            {
                var target = Path.Combine(RootDirectory, $"{QuarantinePrefix}{stamp.AddMilliseconds(attempt):yyyyMMdd-HHmmss-fff}.json");
                if (File.Exists(target))
                {
                    continue;
                }

                File.Move(ConfigPath, target);
                PruneOld(RootDirectory, QuarantinePrefix, MaxQuarantined);
                return target;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    private SettingsLoadResult CreateDefaults(List<SettingsNotice> notices, SettingsSource source)
    {
        var configuration = _defaults();
        var validation = ConfigurationValidator.Validate(configuration);
        if (!validation.IsValid)
        {
            // Un defecto del programa, no del usuario: nunca se escribe una configuración inválida.
            notices.Add(new(SettingsNoticeSeverity.Error, "La configuración predeterminada no es válida: " + Describe(validation.Errors)));
            return InMemoryDefaults(notices);
        }

        if (!TryWriteNew(Utf8NoBom.GetBytes(ConfigurationFile.Serialize(configuration))))
        {
            notices.Add(new(SettingsNoticeSeverity.Error, "No se pudo guardar la configuración predeterminada; se usa en memoria."));
            return InMemoryDefaults(notices, configuration);
        }

        notices.Add(new(SettingsNoticeSeverity.Info, "Configuración predeterminada guardada en config.json."));
        return new SettingsLoadResult(configuration, source, notices, CanSave: true);
    }

    private SettingsLoadResult InMemoryDefaults(List<SettingsNotice> notices, RadialConfiguration? configuration = null) =>
        new(configuration ?? _defaults(), SettingsSource.DefaultsNotPersisted, notices, CanSave: false);

    // Disco

    /// <summary>Primera escritura: el destino no existe. Temporal en la misma carpeta y <c>File.Move</c> sin sobrescribir.</summary>
    private bool TryWriteNew(byte[] bytes)
    {
        var temporary = TemporaryPath();
        try
        {
            WriteTemporary(temporary, bytes);
            File.Move(temporary, ConfigPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return false;
        }
    }

    private static void WriteTemporary(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private bool EnsureDirectories(List<SettingsNotice> notices)
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(BackupsDirectory);
            Directory.CreateDirectory(IconsDirectory);
            Directory.CreateDirectory(LogsDirectory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            notices.Add(new(SettingsNoticeSeverity.Error,
                $"No se pudo crear la carpeta de datos de Radial ({exception.GetType().Name}); se usan los valores predeterminados en memoria."));
            return false;
        }
    }

    private (ConfigurationParseResult? Result, byte[]? Bytes, string? IoError) ReadAndParse(string path)
    {
        try
        {
            if (new FileInfo(path).Length > ConfigurationFile.MaxBytes)
            {
                return (ConfigurationFile.Parse(new byte[ConfigurationFile.MaxBytes + 1]), null, null);
            }

            var bytes = File.ReadAllBytes(path);
            return (ConfigurationFile.Parse(bytes), bytes, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, null, exception.GetType().Name);
        }
    }

    private static bool HasSameContent(string path, byte[] bytes)
    {
        try
        {
            return new FileInfo(path).Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string TemporaryPath() => Path.Combine(RootDirectory, $"{ConfigFileName}.{Guid.NewGuid():N}.tmp");

    private string NextBackupPath()
    {
        var stamp = _utcNow().UtcDateTime;
        for (var attempt = 0; ; attempt++)
        {
            var path = Path.Combine(BackupsDirectory, $"{BackupPrefix}{stamp.AddMilliseconds(attempt):yyyyMMdd-HHmmss-fff}.json");
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    private void PruneBackups() => PruneOld(BackupsDirectory, BackupPrefix, MaxBackups);

    private static void PruneOld(string directory, string prefix, int keep)
    {
        try
        {
            foreach (var old in ListNewestFirst(directory, prefix).Skip(keep))
            {
                TryDelete(old);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Limpiar es opcional: si no se puede, se intentará en el próximo guardado.
        }
    }

    // Los nombres llevan la fecha en un formato ordenable, así que el orden alfabético es el cronológico.
    private static IEnumerable<string> ListNewestFirst(string directory, string prefix)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, prefix + "*.json")
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToList();
    }

    private void DeleteStaleTemporaries()
    {
        try
        {
            foreach (var stale in Directory.GetFiles(RootDirectory, ConfigFileName + ".*.tmp"))
            {
                TryDelete(stale);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Describe(IEnumerable<ValidationIssue> issues)
    {
        var list = issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
        var text = string.Join(" ", list.Take(MaxReportedIssues).Select(i => $"{i.Path}: {i.Message}"));
        return list.Count > MaxReportedIssues ? $"{text} (y {list.Count - MaxReportedIssues} problema(s) más)" : text;
    }
}
