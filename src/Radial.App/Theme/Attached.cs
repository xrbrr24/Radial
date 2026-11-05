using System.Windows;

namespace Radial.App.Theme;

/// <summary>Propiedades adjuntas que los estilos usan para variar una plantilla sin duplicarla.</summary>
internal static class Attached
{
    /// <summary>Texto de ayuda que se ve mientras un cuadro de texto está vacío.</summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached(
        "Hint", typeof(string), typeof(Attached), new PropertyMetadata(string.Empty));

    public static string GetHint(DependencyObject element) => (string)element.GetValue(HintProperty);

    public static void SetHint(DependencyObject element, string value) => element.SetValue(HintProperty, value);

    /// <summary>Glifo (nombre del catálogo) que un cuadro de texto o un botón dibuja a un lado.</summary>
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(Attached), new PropertyMetadata(string.Empty));

    public static string GetGlyph(DependencyObject element) => (string)element.GetValue(GlyphProperty);

    public static void SetGlyph(DependencyObject element, string value) => element.SetValue(GlyphProperty, value);

    /// <summary>Radio de las esquinas de un control (cuando el estilo base no basta).</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Attached), new PropertyMetadata(new CornerRadius(8)));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);
}
