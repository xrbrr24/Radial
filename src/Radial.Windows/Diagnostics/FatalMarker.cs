using System.Globalization;
using Radial.Core.Diagnostics;

namespace Radial.Windows.Diagnostics;

/// <summary>
/// Deja constancia de que Radial terminó por un error grave, para poder decírselo al usuario en el siguiente arranque (en el momento
/// del fallo puede no haber ventana, ni bandeja, ni tiempo). Solo guarda el cuándo y un resumen sin datos del usuario (origen, tipo de
/// excepción y operación), nunca el mensaje de la excepción. <see cref="Record"/> no lanza.
/// </summary>
public interface IFatalMarker
{
    void Record(DateTimeOffset when, string summary);
}

/// <summary>Un marcador que no hace nada.</summary>
public sealed class NullFatalMarker : IFatalMarker
{
    public static NullFatalMarker Instance { get; } = new();

    public void Record(DateTimeOffset when, string summary)
    {
    }
}

/// <summary>
/// El marcador en disco: el archivo <c>last-fatal.txt</c> de la carpeta de registros (no coincide con el nombre de los registros, así
/// que la retención no lo toca). Una línea con la fecha y otra con el resumen. Al arrancar se lee y se borra con <see cref="TakePending"/>,
/// de modo que el aviso se da una sola vez.
/// </summary>
public sealed class FileFatalMarker : IFatalMarker
{
    public const string FileName = "last-fatal.txt";

    private readonly string _path;
    private readonly IRadialLog _log;

    public FileFatalMarker(string directory, IRadialLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _path = Path.Combine(Path.GetFullPath(directory), FileName);
        _log = log ?? NullRadialLog.Instance;
    }

    public string MarkerPath => _path;

    public void Record(DateTimeOffset when, string summary)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllLines(_path, [when.ToString("o", CultureInfo.InvariantCulture), OneLine(summary)]);
        }
        catch (Exception exception)
        {
            SafeLog("No se pudo guardar el marcador de cierre por error grave.", exception);
        }
    }

    /// <summary>
    /// El resumen del cierre por error grave de la ejecución anterior (con su fecha), o <c>null</c> si no hubo. Borra el marcador:
    /// cada cierre se anuncia una sola vez. Si el archivo no se puede leer o borrar, devuelve <c>null</c> y lo anota.
    /// </summary>
    public string? TakePending()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var lines = File.ReadAllLines(_path);
            File.Delete(_path);
            if (lines.Length == 0)
            {
                return "(sin detalles)";
            }

            var when = DateTimeOffset.TryParse(lines[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "fecha desconocida";
            return lines.Length > 1 ? $"{when}: {lines[1]}" : when;
        }
        catch (Exception exception)
        {
            SafeLog("No se pudo leer el marcador de cierre por error grave.", exception);
            return null;
        }
    }

    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

    private void SafeLog(string message, Exception exception)
    {
        try
        {
            _log.Write(LogLevel.Warning, nameof(FileFatalMarker), message, exception);
        }
        catch
        {
            // Sin más recursos: el marcador es una ayuda, no puede provocar otro fallo.
        }
    }
}
