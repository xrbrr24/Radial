using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Input;

namespace Radial.Windows.Overlay;

/// <summary>
/// La composición de producción del coordinador del radial: el reloj monotónico y el localizador de monitores de Windows. Existe porque
/// <see cref="RadialOverlayCoordinator"/> admite un localizador nulo (las pruebas lo usan así) y, cuando la aplicación construía el
/// coordinador a mano, se olvidó pasárselo: el seguimiento entre monitores existía, estaba probado y NUNCA se activaba. Producción
/// (<c>CaptureHost</c>) y las herramientas de comprobación (<c>--edge-check</c>) construyen el coordinador SOLO por aquí, así que lo que se
/// comprueba es lo que se ejecuta.
/// </summary>
public static class RadialOverlayCoordinatorFactory
{
    /// <param name="monitors">Solo para pruebas; por defecto, el localizador real de Windows (<see cref="SystemMonitorLocator"/>).</param>
    /// <param name="clock">Solo para pruebas; por defecto, el reloj monotónico.</param>
    public static RadialOverlayCoordinator Create(
        RadialConfiguration configuration,
        IRadialPresenter presenter,
        Func<ScreenPoint> pointerSource,
        IActionExecutor? executor = null,
        IMonitorLocator? monitors = null,
        IClock? clock = null) =>
        new(
            configuration,
            clock ?? new MonotonicClock(),
            presenter,
            pointerSource,
            executor: executor,
            monitors: monitors ?? new SystemMonitorLocator());
}
