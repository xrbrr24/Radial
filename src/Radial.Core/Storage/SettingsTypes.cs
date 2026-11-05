using Radial.Core.Model;

namespace Radial.Core.Storage;

/// <summary>De dónde salió la configuración que se está usando.</summary>
public enum SettingsSource
{
    /// <summary>Se leyó <c>config.json</c> y era válido.</summary>
    Loaded,

    /// <summary>No había configuración (primer inicio): se generó la predeterminada y se guardó.</summary>
    CreatedDefaults,

    /// <summary>El archivo era inutilizable, se puso en cuarentena y se restauró la copia de seguridad válida más reciente.</summary>
    RecoveredFromBackup,

    /// <summary>El archivo era inutilizable, se puso en cuarentena y no había copia válida: se generó la predeterminada y se guardó.</summary>
    DefaultsAfterProblem,

    /// <summary>
    /// Se usan los valores predeterminados SOLO en memoria: el archivo del disco no se tocó (versión futura, o no se pudo leer o
    /// escribir). <see cref="SettingsLoadResult.CanSave"/> es falso.
    /// </summary>
    DefaultsNotPersisted,
}

public enum SettingsNoticeSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Un aviso sobre la carga de la configuración, para el diagnóstico. No incluye contenido del archivo.</summary>
public sealed record SettingsNotice(SettingsNoticeSeverity Severity, string Message);

/// <param name="CanSave">Falso si guardar sobrescribiría datos que no se deben perder (p. ej. una configuración de una versión futura).</param>
public sealed record SettingsLoadResult(RadialConfiguration Configuration, SettingsSource Source, IReadOnlyList<SettingsNotice> Notices, bool CanSave);

public sealed record SettingsSaveResult(bool Success, bool Changed, string Message)
{
    public static SettingsSaveResult Saved { get; } = new(true, true, "Configuración guardada.");

    public static SettingsSaveResult Unchanged { get; } = new(true, false, "La configuración no ha cambiado.");

    public static SettingsSaveResult Failed(string message) => new(false, false, message);
}

/// <summary>
/// Almacén de la configuración. La implementación real, en disco, está en Radial.Windows (<c>FileSettingsStore</c>); el editor
/// de las fases siguientes usará esta misma interfaz. Ninguna operación lanza por problemas del contenido o del disco: los
/// devuelve como resultado.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Devuelve siempre una configuración utilizable. Primer inicio: crea la predeterminada. Archivo corrupto: lo pone en
    /// cuarentena y recupera una copia válida o, si no hay, la predeterminada. Versión futura: no lo toca (ver
    /// <see cref="SettingsLoadResult.CanSave"/>).
    /// </summary>
    SettingsLoadResult LoadOrCreate();

    /// <summary>Valida y guarda de forma atómica, con copia de seguridad de la anterior.</summary>
    SettingsSaveResult Save(RadialConfiguration configuration);
}
