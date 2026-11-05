using Radial.Core.Model;

namespace Radial.App.Overlay;

/// <summary>
/// Traduce el nombre de un icono de tipo <see cref="IconSource.Glyph"/> a su carácter en Segoe Fluent Icons / Segoe MDL2 Assets.
/// Los puntos de código se escriben como números (son caracteres de uso privado: en el código fuente serían invisibles). Los iconos
/// de archivo (<see cref="IconSource.File"/>) no se resuelven aquí; si un nombre no existe se muestra un glifo neutro, nunca un hueco.
/// </summary>
internal static class GlyphCatalog
{
    /// <summary>«Más»: tres puntos.</summary>
    public static readonly string Fallback = Char(0xE712);

    private static readonly Dictionary<string, int> CodePoints = new(StringComparer.OrdinalIgnoreCase)
    {
        // Los que ya usaba el menú inicial.
        ["Globe"] = 0xE774,
        ["Link"] = 0xE71B,
        ["Folder"] = 0xE8B7,
        ["Copy"] = 0xE8C8,
        ["Paste"] = 0xE77F,
        ["Undo"] = 0xE7A7,
        ["SelectAll"] = 0xE8B3,
        ["Save"] = 0xE74E,
        ["Apps"] = 0xE71D,

        // Edición.
        ["Cut"] = 0xE8C6,
        ["Redo"] = 0xE7A6,
        ["Find"] = 0xE721,
        ["New"] = 0xE710,
        ["Open"] = 0xE8E5,
        ["Print"] = 0xE749,
        ["Delete"] = 0xE74D,
        ["Edit"] = 0xE70F,
        ["Document"] = 0xE8A5,

        // Navegador y navegación.
        ["Close"] = 0xE711,
        ["Refresh"] = 0xE72C,
        ["Back"] = 0xE72B,
        ["Forward"] = 0xE72A,
        ["Download"] = 0xE896,
        ["Home"] = 0xE80F,
        ["ChevronRight"] = 0xE76C,
        ["ArrowLeft"] = 0xE72B,
        ["ArrowRight"] = 0xE72A,
        ["More"] = 0xE712,

        // Sistema.
        ["Desktop"] = 0xE7F4,
        ["Pictures"] = 0xE91B,
        ["Keyboard"] = 0xE765,
        ["Camera"] = 0xE722,
        ["Settings"] = 0xE713,
        ["Calculator"] = 0xE8EF,
        ["Color"] = 0xE790,
        ["Emoji"] = 0xE76E,
        ["Lock"] = 0xE72E,
        ["TaskView"] = 0xE7C4,
        ["Application"] = 0xE71D,

        // Multimedia.
        ["Play"] = 0xE768,
        ["Next"] = 0xE893,
        ["Previous"] = 0xE892,
        ["Stop"] = 0xE71A,
        ["VolumeUp"] = 0xE995,
        ["VolumeDown"] = 0xE993,
        ["Mute"] = 0xE74F,
        ["VolumeMute"] = 0xE74F,

        // Otros de uso general en la interfaz.
        ["Search"] = 0xE721,
        ["Add"] = 0xE710,
        ["Check"] = 0xE73E,
        ["Warning"] = 0xE7BA,
        ["Info"] = 0xE946,
        ["Tag"] = 0xE8EC,
        ["Star"] = 0xE734,
        ["Code"] = 0xE943,
        ["Music"] = 0xE8D6,
        ["Mail"] = 0xE715,
        ["Calendar"] = 0xE787,
        ["Clock"] = 0xE823,
        ["Heart"] = 0xEB51,
        ["Flag"] = 0xE7C1,
        ["Pin"] = 0xE718,
        ["Shield"] = 0xE83D,
        ["Power"] = 0xE7E8,
        ["Wifi"] = 0xE701,
        ["Video"] = 0xE714,
        ["Game"] = 0xE7FC,
        ["Terminal"] = 0xE756,
        ["Chat"] = 0xE8BD,
        ["Cloud"] = 0xE753,
        ["Shopping"] = 0xE7BF,
    };

    /// <summary>Los nombres que el editor ofrece al elegir un icono, en el orden en que se muestran.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "Apps", "Application", "Globe", "Link", "Folder", "Document", "Copy", "Paste", "Cut", "Undo", "Redo", "SelectAll", "Save", "Find", "New", "Open",
        "Print", "Delete", "Edit", "Close", "Refresh", "Back", "Forward", "Home", "Download", "Desktop", "Pictures", "Keyboard", "Camera", "Settings",
        "Calculator", "Color", "Emoji", "Lock", "TaskView", "Play", "Previous", "Next", "Stop", "VolumeUp", "VolumeDown", "Mute", "Music", "Video",
        "Mail", "Calendar", "Clock", "Chat", "Cloud", "Code", "Terminal", "Game", "Star", "Heart", "Flag", "Pin", "Tag", "Shield", "Power", "Wifi", "Shopping",
    ];

    public static string Resolve(IconReference? icon) =>
        icon is { Source: IconSource.Glyph } && Resolve(icon.Value) is { } glyph ? glyph : Fallback;

    /// <summary>El glifo de un nombre del catálogo, o <c>null</c> si no existe.</summary>
    public static string? Resolve(string? name) =>
        name is not null && CodePoints.TryGetValue(name, out var code) ? Char(code) : null;

    public static bool Contains(string? name) => name is not null && CodePoints.ContainsKey(name);

    private static string Char(int codePoint) => char.ConvertFromUtf32(codePoint);
}
