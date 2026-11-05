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
        _ => "No soportada",
    };

    public static string Direction(RadialDirection direction) => DirectionNames.Spanish(direction) switch
    {
        { Length: > 0 } name => char.ToUpperInvariant(name[0]) + name[1..],
        _ => direction.ToString(),
    };

    /// <summary>Una línea que resume lo que hace una acción (la combinación, el programa, la ruta o la dirección).</summary>
    public static string Summary(RadialAction action, Func<string, string?>? keyLabel = null) => action switch
    {
        SendKeysAction keys => KeyChordText.Format(keys.Chord, keyLabel),
        LaunchApplicationAction launch => string.IsNullOrWhiteSpace(launch.Arguments) ? launch.Path : $"{launch.Path} {launch.Arguments}",
        OpenFileAction file => file.Path,
        OpenFolderAction folder => folder.Path,
        OpenUrlAction url => url.Url,
        OpenMenuAction => "Abre un submenú",
        UnknownAction unknown => $"Acción de tipo «{unknown.Type}» (no soportada)",
        _ => string.Empty,
    };
}
