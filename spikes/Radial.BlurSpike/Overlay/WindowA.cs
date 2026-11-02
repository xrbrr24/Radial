using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Radial.BlurSpike.Overlay;

/// <summary>
/// VARIANTE A — ventana layered de WPF (AllowsTransparency=true).
/// Un círculo con relleno translúcido oscuro. NO hay desenfoque: el escritorio se ve nítido a través del círculo,
/// solo oscurecido. Sirve de referencia básica.
/// </summary>
internal sealed class WindowA : OverlayWindowBase
{
    private readonly Ellipse _disc;

    public WindowA()
        : base(allowsTransparency: true)
    {
        _disc = new Ellipse
        {
            Margin = new Thickness(Inset),
            Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.5, Color = Colors.Black },
        };
        Root.Children.Add(_disc);
        AddDiagnosticContent();
        Notes = "WPF AllowsTransparency=true (WS_EX_LAYERED lo añade WPF). Sin blur real.";
    }

    protected override void ApplyRequest(PresentRequest request) => _disc.Fill = Tint(request.Opacity);
}
