using System.Diagnostics;
using System.Threading.Channels;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

/// <summary>
/// La composición de PRODUCCIÓN del coordinador y el camino de los eventos hasta él. El seguimiento entre monitores existía y estaba
/// probado, pero la aplicación construía el coordinador sin pasarle el localizador de monitores y no se activaba nunca: las pruebas
/// del coordinador lo construían a mano con él y <c>--edge-check</c> también. Ahora todo se construye por la fábrica. Estas pruebas
/// no generan entrada real (las reglas del proyecto lo prohíben): el hook y el sondeo de 15 ms de la interfaz se sustituyen por un
/// canal de eventos y una llamada a <c>Poll</c>; que el hook entrega movimiento real y que el temporizador sondea lo comprueba la
/// prueba manual (las líneas «[monitores]» de Diagnóstico).
/// </summary>
public class RadialOverlayCoordinatorFactoryTests
{
    private static readonly MonitorAreas A = new(new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1080));
    private static readonly MonitorAreas B = new(new ScreenRect(1920, 0, 3840, 1080), new ScreenRect(1920, 0, 3840, 1080));

    private sealed class TwoMonitors : IMonitorLocator
    {
        public MonitorAreas? Find(ScreenPoint point) => point.X < 1920 ? A : B;
    }

    private static RadialConfiguration Config() => new()
    {
        RootMenuId = new MenuId("main"),
        Menus =
        [
            new RadialMenu(
                new MenuId("main"),
                "Principal",
                [new RadialItem(new ItemId("main.east"), "Este", RadialDirection.East, new OpenFolderAction(@"C:\Temp\East"))]),
        ],
        Geometry = new GeometrySettings { DeadZoneRadius = 40, HysteresisDegrees = 0 },
    };

    [Fact]
    public void TheProductionComposition_AlwaysIncludesTheMonitorLocator()
    {
        // Sin pasar nada (como hace la aplicación): el seguimiento tiene que estar activo.
        var coordinator = RadialOverlayCoordinatorFactory.Create(Config(), new RecordingPresenter(), () => new ScreenPoint(0, 0));

        Assert.True(coordinator.FollowsMonitors);
    }

    [Fact]
    public void ACoordinatorBuiltByHandWithoutALocator_DoesNotFollow_WhichIsWhyProductionMustUseTheFactory()
    {
        var byHand = new RadialOverlayCoordinator(Config(), new TestClock(), new RecordingPresenter(), () => new ScreenPoint(0, 0));

        Assert.False(byHand.FollowsMonitors);
    }

    [Fact]
    public async Task TheEventsOfTheInputChannel_ReachTheCoordinator_AndTheNextPollFollowsTheCursorToTheOtherMonitor()
    {
        var presenter = new RecordingPresenter();
        var pointer = new ScreenPoint(1000, 500);
        var coordinator = RadialOverlayCoordinatorFactory.Create(Config(), presenter, () => pointer, monitors: new TwoMonitors());
        var trace = new List<string>();
        coordinator.FollowTrace += trace.Add;

        // El camino de producción: el hook publica en el canal; el lector único lo vacía hacia el coordinador (CaptureHost.ProcessGesture).
        var channel = Channel.CreateUnbounded<GestureEvent>();
        var reader = new GestureEventReader(channel.Reader, coordinator.Handle);
        channel.Writer.TryWrite(new GestureEvent(1, Stopwatch.GetTimestamp(), GestureEventKind.Activated, pointer, CancelCause.None));
        channel.Writer.Complete();
        var end = await reader.RunAsync(CancellationToken.None);

        Assert.Equal(GestureReaderEndReason.ChannelCompleted, end.Reason);
        Assert.True(coordinator.IsActive);
        Assert.True(presenter.Visible);

        // El temporizador de la interfaz sondea el cursor (LatestPointer del hook): ahora está en el otro monitor.
        pointer = new ScreenPoint(1940, 500);
        coordinator.Poll();

        var follow = Assert.Single(presenter.Follows);
        Assert.Equal(new ScreenPoint(1940, 500), follow.Cursor);
        Assert.Contains(trace, line => line.Contains("sigue al cursor (1940, 500)"));
    }

    [Fact]
    public async Task ACursorWithNegativeCoordinates_ReachesTheCoordinatorIntact()
    {
        // El monitor de la izquierda del principal: x negativas. Lo que el hook publica es lo que el coordinador mide.
        var presenter = new RecordingPresenter();
        var pointer = new ScreenPoint(-300, 500);
        var coordinator = RadialOverlayCoordinatorFactory.Create(
            Config(),
            presenter,
            () => PointerPacking.Unpack(PointerPacking.Pack(pointer)), // exactamente lo que hace LatestPointer
            monitors: new NegativeLeftAndMain());
        var channel = Channel.CreateUnbounded<GestureEvent>();
        var reader = new GestureEventReader(channel.Reader, coordinator.Handle);
        channel.Writer.TryWrite(new GestureEvent(1, Stopwatch.GetTimestamp(), GestureEventKind.Activated, pointer, CancelCause.None));
        channel.Writer.Complete();
        await reader.RunAsync(CancellationToken.None);

        pointer = new ScreenPoint(30, 500); // pasa al principal (x ≥ 0)
        coordinator.Poll();

        Assert.Single(presenter.Follows);
        Assert.Equal(new ScreenPoint(30, 500), presenter.Follows[0].Cursor);
    }

    private sealed class NegativeLeftAndMain : IMonitorLocator
    {
        private static readonly MonitorAreas Left = new(new ScreenRect(-1920, 0, 0, 1080), new ScreenRect(-1920, 0, 0, 1080));
        private static readonly MonitorAreas Main = new(new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1080));

        public MonitorAreas? Find(ScreenPoint point) => point.X < 0 ? Left : Main;
    }
}
