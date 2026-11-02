using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Radial.BlurSpike.Native;

namespace Radial.BlurSpike.Overlay;

/// <summary>
/// VARIANTE D — SetWindowCompositionAttribute (API NO documentada; ver ExperimentalComposition).
/// EXPERIMENTAL. Solo investiga qué hace en este equipo. Nunca debe usarse en producción.
/// </summary>
internal sealed class WindowD : OverlayWindowBase
{
    private readonly Rectangle _tint = new();
    private bool _circular;

    public WindowD()
        : base(allowsTransparency: false)
    {
        Root.Children.Add(_tint);
        AddDiagnosticContent();
    }

    protected override void OnHandleReady() =>
        HwndSource.FromHwnd(Hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;

    protected override void ApplyRequest(PresentRequest request)
    {
        _circular = request.CircularRegion;
        _tint.Fill = Tint(request.Opacity);

        var state = request.Accent == AccentKind.AcrylicBlurBehind
            ? ExperimentalComposition.AccentState.EnableAcrylicBlurBehind
            : ExperimentalComposition.AccentState.EnableBlurBehind;

        // GradientColor es 0xAABBGGRR. Con alfa casi 0 el tinte lo aporta _tint; se deja un alfa mínimo
        // porque con 0 algunas versiones de Windows no pintan el acrílico.
        var result = ExperimentalComposition.SetAccent(Hwnd, state, 0x01000000u | 0x001A1614u);
        Notes = $"SetWindowCompositionAttribute(WCA_ACCENT_POLICY, {state}) -> {result} (0 = fallo). API NO documentada.";
    }

    protected override void OnPlaced(Win32.RECT bounds)
    {
        if (_circular)
        {
            ApplyEllipseRegion();
        }
        else
        {
            ClearRegion();
        }
    }
}
