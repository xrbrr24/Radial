using Radial.Core.Catalog;
using Radial.Core.Editing;
using Radial.Core.Geometry;
using Radial.Core.Model;

namespace Radial.Windows.Presentation;

/// <summary>Los textos que ve el usuario para tipos, categorías y direcciones. Un solo sitio, para traducirlos o cambiarlos después.</summary>
public static class UiText
{
    public static string CategoryName(ActionCategory category) => category.Id switch
    {
        "editing" => "Edición",
        "browser" => "Navegador",
        "applications" => "Aplicaciones",
        "files" => "Archivos",
        "system" => "Sistema",
        "multimedia" => "Multimedia",
        "custom" => "Personalizadas",
        null or "" => "Sin categoría",
        _ => char.ToUpperInvariant(category.Id[0]) + category.Id[1..].Replace('-', ' '),
    };

    public static string KindName(ActionKind kind) => kind switch
    {
        ActionKind.Shortcut => "Atajo de teclado",
        ActionKind.Application => "Aplicación",
        ActionKind.File => "Archivo",
        ActionKind.Folder => "Carpeta",
        ActionKind.Url => "Dirección web",
        ActionKind.Text => "Insertar texto",
        ActionKind.Volume => "Volumen",
        ActionKind.Window => "Ventana activa",
        _ => "No soportada",
    };

    public static string VolumeOperationName(VolumeOperation operation) => operation switch
    {
        VolumeOperation.Increase => "Subir el volumen",
        VolumeOperation.Decrease => "Bajar el volumen",
        VolumeOperation.SetLevel => "Fijar el nivel de volumen",
        VolumeOperation.ToggleMute => "Alternar el silencio",
        _ => operation.ToString(),
    };

    public static string WindowOperationName(WindowOperation operation) => operation switch
    {
        WindowOperation.Minimize => "Minimizar",
        WindowOperation.Maximize => "Maximizar",
        WindowOperation.Restore => "Restaurar",
        WindowOperation.Close => "Cerrar",
        _ => operation.ToString(),
    };

    private static string VolumeSummary(VolumeAction volume) => volume.Operation switch
    {
        VolumeOperation.Increase => $"Sube el volumen {volume.Percent} puntos",
        VolumeOperation.Decrease => $"Baja el volumen {volume.Percent} puntos",
        VolumeOperation.SetLevel => $"Fija el volumen al {volume.Percent} %",
        VolumeOperation.ToggleMute => "Activa o quita el silencio",
        _ => string.Empty,
    };

    // Un argumento con espacios se muestra entre comillas SOLO para leerlo: el programa recibe el argumento tal cual.
    private static string DisplayArgument(string argument) =>
        argument.Length == 0 || argument.Contains(' ') ? $"\"{argument}\"" : argument;

    // El texto de una acción en una sola línea y recortado, para listas y resúmenes.
    private static string Shorten(string text)
    {
        var singleLine = text.Replace("\r\n", " ↵ ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        return singleLine.Length <= 40 ? singleLine : singleLine[..40].TrimEnd() + "…";
    }

    public static string Direction(RadialDirection direction) => DirectionNames.Spanish(direction) switch
    {
        { Length: > 0 } name => char.ToUpperInvariant(name[0]) + name[1..],
        _ => direction.ToString(),
    };

    /// <summary>Una línea que resume lo que hace una acción (la combinación, el programa, la ruta o la dirección).</summary>
    public static string Summary(RadialAction action, Func<string, string?>? keyLabel = null) => action switch
    {
        SendKeysAction keys => KeyChordText.Format(keys.Chord, keyLabel),
        LaunchApplicationAction launch => launch.Arguments is { Count: > 0 } arguments
            ? $"{launch.Path} {string.Join(' ', arguments.Select(DisplayArgument))}"
            : launch.Path,
        TypeTextAction typeText => $"Escribe «{Shorten(typeText.Text)}»",
        VolumeAction volume => VolumeSummary(volume),
        WindowControlAction window => $"{WindowOperationName(window.Operation)} la ventana activa",
        OpenFileAction file => file.Path,
        OpenFolderAction folder => folder.Path,
        OpenUrlAction url => url.Url,
        OpenMenuAction => "Abre un submenú",
        UnknownAction unknown => $"Acción de tipo «{unknown.Type}» (no soportada)",
        _ => string.Empty,
    };
}
