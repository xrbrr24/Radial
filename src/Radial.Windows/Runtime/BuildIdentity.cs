using System.Globalization;
using System.Reflection;

namespace Radial.Windows.Runtime;

/// <summary>
/// Qué compilación de Radial se está ejecutando: la fecha de compilación (la de escritura del ensamblado principal) y su carpeta. Existe
/// porque una instancia de Radial puede seguir en ejecución (bandeja) con una compilación antigua mientras se prueba otra: la instancia
/// única hace que abrir la nueva solo traiga al frente la vieja, y los síntomas parecen un fallo del código nuevo. Se muestra en el título
/// de la ventana, en el texto de la bandeja y en el registro. Formateo puro: lo único que toca el disco es <see cref="ForAssembly"/>.
/// </summary>
public static class BuildIdentity
{
    /// <summary>Límite de <c>NotifyIcon.Text</c>: 63 caracteres.</summary>
    public const int MaxTrayText = 63;

    public const string Unknown = "desconocida";

    /// <summary>Fecha y hora local con minutos (suficiente para distinguir dos compilaciones del mismo día).</summary>
    public static string FormatStamp(DateTime localTime) => localTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>La fecha de compilación de un ensamblado, o <see cref="Unknown"/> si no se puede saber (p. ej. sin ruta en disco).</summary>
    public static string StampOf(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        try
        {
            var location = assembly.Location;
            return string.IsNullOrEmpty(location) || !File.Exists(location) ? Unknown : FormatStamp(File.GetLastWriteTime(location));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Unknown;
        }
    }

    /// <summary>Título de la ventana principal.</summary>
    public static string WindowTitle(string stamp) => $"Radial — compilación {stamp}";

    /// <summary>Texto de la bandeja, recortado al límite de Windows.</summary>
    public static string TrayText(string stamp)
    {
        var text = $"Radial · compilación {stamp}";
        return text.Length <= MaxTrayText ? text : text[..MaxTrayText];
    }

    /// <summary>Línea para el registro y el diagnóstico: qué compilación, y desde qué carpeta.</summary>
    public static string Describe(string stamp, string? directory) =>
        string.IsNullOrWhiteSpace(directory) ? $"Compilación {stamp}." : $"Compilación {stamp}, ejecutándose desde {directory}.";

    /// <summary>Carpeta del ensamblado, o <c>null</c> si no se sabe.</summary>
    public static string? DirectoryOf(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        try
        {
            var location = assembly.Location;
            return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException or PathTooLongException)
        {
            return null;
        }
    }
}
