using Radial.BlurSpike.Native;

namespace Radial.BlurSpike.Overlay;

/// <summary>
/// VARIANTE C — variante B + SetWindowRgn con una elipse. Se recalcula tras cada colocación porque la región
/// está en píxeles físicos de ventana. Resultado observado en run1: la ventana siguió viéndose rectangular.
/// El resultado de SetWindowRgn no se registró en run1, así que la causa NO se determinó.
/// </summary>
internal sealed class WindowC : WindowB
{
    protected override void OnPlaced(Win32.RECT bounds) => ApplyEllipseRegion();
}
