using System.Windows.Media;

namespace Radial.App.Overlay;

/// <summary>
/// Paleta y medidas del radial: grafito translúcido con un único acento frío. Sin degradados ni brillos (CLAUDE.md, «Estética»).
/// Los pinceles están congelados: se comparten entre sectores y no generan notificaciones de cambio.
/// Las medidas son DIP; el motor mide en los mismos DIP, así que el radio de la zona central de la configuración es el del botón central.
/// </summary>
internal static class RadialTheme
{
    /// <summary>Lado de la ventana del overlay y de la vista. El radial cabe con margen para el sector resaltado.</summary>
    public const double ViewSize = 420;

    public const double BackdropRadius = 206;

    /// <summary>Separación entre la zona central y el arranque de los sectores.</summary>
    public const double HubClearance = 14;

    public const double OuterRadius = 188;

    /// <summary>El sector resaltado crece hacia fuera esta cantidad.</summary>
    public const double SelectedGrowth = 8;

    public const double SectorGapDegrees = 3;

    public const double LabelRadius = 124;

    /// <summary>Grosor del arco de progreso de las permanencias (entrar en un grupo y volver atrás).</summary>
    public const double ProgressThickness = 4;

    /// <summary>Banda de acento de un grupo: separación del borde exterior, grosor y hueco angular (la hace más corta que el sector).</summary>
    public const double GroupBandOffset = 4;
    public const double GroupBandThickness = 4;
    public const double GroupBandGapDegrees = 18;

    /// <summary>Glifo (Segoe «Warning») de una acción de tipo desconocido, que se conserva pero no se puede usar.</summary>
    public const string UnsupportedGlyph = "";

    public static readonly Brush Backdrop = Freeze(Color.FromArgb(0xD9, 0x12, 0x14, 0x18));
    public static readonly Brush BackdropEdge = Freeze(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));

    public static readonly Brush SectorFill = Freeze(Color.FromArgb(0xE6, 0x1F, 0x23, 0x2A));
    public static readonly Brush SectorEdge = Freeze(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    public static readonly Brush SectorFillEmpty = Freeze(Color.FromArgb(0x66, 0x17, 0x1A, 0x1F));

    public static readonly Brush SelectedFill = Freeze(Color.FromArgb(0xF0, 0x31, 0x50, 0x65));
    public static readonly Brush SelectedEdge = Freeze(Color.FromArgb(0xE6, 0x5B, 0xB8, 0xF0));

    public static readonly Brush Label = Freeze(Color.FromRgb(0xC7, 0xCC, 0xD4));
    public static readonly Brush LabelSelected = Freeze(Color.FromRgb(0xFF, 0xFF, 0xFF));
    public static readonly Brush Icon = Freeze(Color.FromRgb(0xAE, 0xB6, 0xC2));
    public static readonly Brush IconSelected = Freeze(Color.FromRgb(0x9A, 0xD8, 0xFF));

    public static readonly Brush Hub = Freeze(Color.FromArgb(0xE6, 0x0F, 0x11, 0x15));
    public static readonly Brush HubEdge = Freeze(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Pointer = Freeze(Color.FromRgb(0x5B, 0xB8, 0xF0));

    public static readonly Brush IconDim = Freeze(Color.FromRgb(0x6B, 0x73, 0x80));
    public static readonly Brush HubEdgeArmed = Freeze(Color.FromArgb(0xE6, 0x5B, 0xB8, 0xF0));
    public static readonly Brush GroupBand = Freeze(Color.FromArgb(0x8C, 0x5B, 0xB8, 0xF0));
    public static readonly Brush GroupBandSelected = Freeze(Color.FromArgb(0xFF, 0x9A, 0xD8, 0xFF));
    public static readonly Brush Progress = Freeze(Color.FromRgb(0x9A, 0xD8, 0xFF));

    // Segoe Fluent Icons (Windows 11) con Segoe MDL2 Assets de respaldo; texto con la fuente del sistema.
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
