using Radial.Core.Model;

namespace Radial.App.Overlay;

/// <summary>
/// Traduce el nombre de un icono de tipo <see cref="IconSource.Glyph"/> a su carácter en Segoe Fluent Icons / Segoe MDL2 Assets.
/// Catálogo mínimo: solo lo que usa el menú actual. Los iconos de archivo (<see cref="IconSource.File"/>) y el catálogo completo
/// del editor llegan en fases posteriores; mientras tanto se muestra un glifo neutro, nunca un hueco.
/// </summary>
internal static class GlyphCatalog
{
    public const string Fallback = ""; // «más»: tres puntos

    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Globe"] = "",
        ["Link"] = "",
        ["Folder"] = "",
        ["Copy"] = "",
        ["Paste"] = "",
        ["Undo"] = "",
        ["SelectAll"] = "",
        ["Save"] = "",
    };

    public static string Resolve(IconReference? icon) =>
        icon is { Source: IconSource.Glyph } && Glyphs.TryGetValue(icon.Value, out var glyph) ? glyph : Fallback;
}
