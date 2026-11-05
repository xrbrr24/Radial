using System.Windows;
using System.Windows.Controls;
using Radial.App.Overlay;

namespace Radial.App.Controls;

/// <summary>
/// Un icono del catálogo de glifos (<see cref="GlyphCatalog"/>) dibujado con la fuente de iconos del sistema. Se usa por el nombre
/// (<c>&lt;c:GlyphIcon Glyph="Copy"/&gt;</c>) o enlazado a un nombre del modelo. Un nombre desconocido muestra el glifo neutro.
/// </summary>
internal sealed class GlyphIcon : TextBlock
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(GlyphIcon), new PropertyMetadata(string.Empty, (d, _) => ((GlyphIcon)d).Refresh()));

    public GlyphIcon()
    {
        SetResourceReference(FontFamilyProperty, "Font.Icon");
        FontSize = 16;
        TextAlignment = TextAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        Refresh();
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    private void Refresh() => Text = string.IsNullOrEmpty(Glyph) ? string.Empty : GlyphCatalog.Resolve(Glyph) ?? GlyphCatalog.Fallback;
}
