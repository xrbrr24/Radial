using Radial.Core.Engine;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

/// <summary>
/// Con el radial abierto, el cursor cruza a otro monitor y el radial lo sigue. Aquí se prueba la lógica del coordinador con un presentador
/// y unos monitores falsos que reproducen lo que hace Windows (qué monitor es, su escala, el ajuste al área de trabajo); lo que Windows
/// hace de verdad con la ventana lo comprueba <c>--edge-check</c> (ruta real) y la prueba manual. La transición tiene dos casos: si el
/// radial cabe donde se pide, el cursor conserva su posición relativa y la selección no cambia; si hay que desplazarlo (lo normal junto
/// al borde compartido), no hay selección hasta que el cursor se mueve (nunca un salto de sector, nunca una activación involuntaria).
/// </summary>
public class RadialOverlayCoordinatorMonitorFollowTests
{
    private const double ViewDip = 420;

    private static readonly OpenFolderAction East = new(@"C:\Temp\East");
    private static readonly OpenFolderAction West = new(@"C:\Temp\West");

    private static RadialConfiguration Config() => new()
    {
        RootMenuId = new MenuId("main"),
        Menus =
        [
            new RadialMenu(
                new MenuId("main"),
                "Principal",
                [
                    new RadialItem(new ItemId("main.east"), "Este", RadialDirection.East, East),
                    new RadialItem(new ItemId("main.west"), "Oeste", RadialDirection.West, West),
                    new RadialItem(new ItemId("main.south"), "Sur", RadialDirection.South, new OpenFolderAction(@"C:\Temp\South")),
                ]),
        ],
        Geometry = new GeometrySettings { DeadZoneRadius = 40, HysteresisDegrees = 0 },
    };

    /// <summary>Monitores inventados: el más cercano al punto (como <c>MONITOR_DEFAULTTONEAREST</c>).</summary>
    private sealed class FakeMonitors(params MonitorAreas[] monitors) : IMonitorLocator
    {
        public MonitorAreas? Find(ScreenPoint point) =>
            monitors.Length == 0 ? null : monitors.OrderBy(m => MonitorFollow.DistanceOutside(m.Bounds, point)).First();
    }

    private sealed class NoMonitors : IMonitorLocator
    {
        public MonitorAreas? Find(ScreenPoint point) => null;
    }

    /// <summary>Una disposición de monitores con su escala, y un presentador que hace lo que haría Windows con ella.</summary>
    private sealed class Rig
    {
        private readonly (MonitorAreas Area, double Scale)[] _layout;

        public Rig((MonitorAreas Area, double Scale)[] layout, ScreenPoint activation, bool realisticFollow = true)
        {
            _layout = layout;
            Locator = new FakeMonitors([.. layout.Select(l => l.Area)]);
            var presenter = new RecordingPresenter
            {
                Scale = ScaleAt(activation),
                FitCenter = requested => Fit(requested, requested),
            };
            if (realisticFollow)
            {
                presenter.FollowBehavior = (cursor, offset) =>
                {
                    var scale = ScaleAt(cursor);
                    var ideal = OverlayPlacement.IdealCenter(cursor, offset.X, offset.Y, scale);
                    var placed = new PresentedPlacement(scale, Fit(ideal, cursor));
                    LastFollow = placed;
                    return placed;
                };
            }

            Harness = new CoordinatorHarness(Config(), presenter, monitors: Locator);
        }

        public IMonitorLocator Locator { get; }

        public CoordinatorHarness Harness { get; }

        public RecordingPresenter Presenter => Harness.Presenter;

        /// <summary>Lo último que «devolvió Windows» al seguir al cursor.</summary>
        public PresentedPlacement LastFollow { get; private set; }

        public double ScaleAt(ScreenPoint point)
        {
            var bounds = Locator.Find(point)!.Value.Bounds;
            return _layout.First(l => l.Area.Bounds == bounds).Scale;
        }

        public RadialDirection? Current => Presenter.Selections.Count == 0 ? null : Presenter.Selections[^1];

        /// <summary>El ajuste de Windows: la ventana entera en el área de trabajo del monitor de <paramref name="areaPoint"/>.</summary>
        private ScreenPoint Fit(ScreenPoint center, ScreenPoint areaPoint)
        {
            var scale = ScaleAt(areaPoint);
            var size = OverlayPlacement.PhysicalSize(ViewDip, (int)Math.Round(96 * scale));
            return OverlayPlacement.Fit(center, size, Locator.Find(areaPoint)!.Value.Work);
        }

        public void Activate(ScreenPoint at)
        {
            Harness.Move(at);
            Harness.Activate(at);
        }
    }

    // Disposiciones

    // A | B en horizontal, B más alto que A; la barra de tareas de A (40 px abajo) no está en el área de trabajo.
    private static (MonitorAreas Area, double Scale)[] SideBySide(double scaleA = 1.0, double scaleB = 1.0) =>
    [
        (new MonitorAreas(new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1040)), scaleA),
        (new MonitorAreas(new ScreenRect(1920, 0, 3840, 1200), new ScreenRect(1920, 0, 3840, 1200)), scaleB),
    ];

    // L | R con coordenadas negativas: «L» a la izquierda del principal, «R» (principal) con otra escala.
    private static (MonitorAreas Area, double Scale)[] NegativeLeft(double scaleL = 1.0, double scaleR = 2.0) =>
    [
        (new MonitorAreas(new ScreenRect(-1920, 0, 0, 1080), new ScreenRect(-1920, 0, 0, 1080)), scaleL),
        (new MonitorAreas(new ScreenRect(0, 0, 2560, 1440), new ScreenRect(0, 0, 2560, 1400)), scaleR),
    ];

    // Uno encima de otro.
    private static (MonitorAreas Area, double Scale)[] Stacked() =>
    [
        (new MonitorAreas(new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1080)), 1.0),
        (new MonitorAreas(new ScreenRect(0, 1080, 1920, 2160), new ScreenRect(0, 1080, 1920, 2120)), 1.0),
    ];

    /// <summary>Activa junto al borde derecho de A, despierta la selección moviendo y deja el cursor sobre el sector del este.</summary>
    private static Rig ArmedEastOnA(double scaleA = 1.0, double scaleB = 1.0, bool realisticFollow = true)
    {
        var rig = new Rig(SideBySide(scaleA, scaleB), new ScreenPoint(1800, 500), realisticFollow);
        rig.Activate(new ScreenPoint(1800, 500));
        rig.Harness.Move(new ScreenPoint(1830, 500));
        Assert.Equal(RadialDirection.East, rig.Current); // el radial se desplazó hacia dentro de A: el cursor está al este de su centro
        return rig;
    }

    // Cuándo se cruza

    [Fact]
    public void TheRadialStaysOnItsMonitor_WhileTheCursorIsWithinTheMargin()
    {
        var rig = ArmedEastOnA();

        rig.Harness.Move(new ScreenPoint(1925, 500)); // 6 px fuera de A: temblor, no cruce

        Assert.Empty(rig.Presenter.Follows);
    }

    [Fact]
    public void TheRadialFollowsTheCursor_OnceItMovedTheMarginOutOfItsMonitor()
    {
        var rig = ArmedEastOnA();

        rig.Harness.Move(new ScreenPoint(1932, 500)); // 13 px fuera de A

        var follow = Assert.Single(rig.Presenter.Follows);
        Assert.Equal(new ScreenPoint(1932, 500), follow.Cursor);
    }

    [Fact]
    public void WithoutAMonitorLocator_ThereIsNoFollowing_AndTheRestKeepsWorking()
    {
        var presenter = new RecordingPresenter();
        var h = new CoordinatorHarness(Config(), presenter);
        h.Move(CoordinatorHarness.Start);
        h.Activate();

        h.Move(new ScreenPoint(CoordinatorHarness.Start.X + 5_000, CoordinatorHarness.Start.Y));

        Assert.Empty(presenter.Follows);
        Assert.Equal(RadialDirection.East, presenter.Selections[^1]);
    }

    [Fact]
    public void IfWindowsDoesNotKnowTheMonitors_ItNeverFollows_AndNothingBreaks()
    {
        var presenter = new RecordingPresenter();
        var h = new CoordinatorHarness(Config(), presenter, monitors: new NoMonitors());
        h.Move(CoordinatorHarness.Start);
        h.Activate();

        h.Move(new ScreenPoint(CoordinatorHarness.Start.X + 5_000, CoordinatorHarness.Start.Y));

        Assert.Empty(presenter.Follows);
        Assert.True(h.Coordinator.IsActive);
    }

    [Fact]
    public void TheMarginIsAHysteresis_AFollowDoesNotComeBackForATremor_ButDoesForARealReturn()
    {
        var rig = ArmedEastOnA();
        rig.Harness.Move(new ScreenPoint(1932, 500));
        Assert.Single(rig.Presenter.Follows);

        // De vuelta junto al borde compartido: sigue en B (el margen se mide ahora desde B).
        rig.Harness.Move(new ScreenPoint(1912, 500)); // 8 px fuera de B
        rig.Harness.Move(new ScreenPoint(1919, 500)); // último píxel de A, pegado al borde
        Assert.Single(rig.Presenter.Follows);

        rig.Harness.Move(new ScreenPoint(1905, 500)); // 15 px fuera de B: vuelve
        Assert.Equal(2, rig.Presenter.Follows.Count);
        Assert.Equal(new ScreenPoint(1905, 500), rig.Presenter.Follows[1].Cursor);
    }

    // Continuidad de la selección

    [Fact]
    public void WhenTheRadialFitsWhereItIsAsked_TheCursorKeepsItsPlaceAndTheSelectionDoesNotChange()
    {
        // Sin ajuste (el presentador deja el radial exactamente donde se le pide): transición continua.
        var rig = ArmedEastOnA(realisticFollow: false);
        var before = rig.Presenter.Selections.Count;

        rig.Harness.Move(new ScreenPoint(1932, 500));

        Assert.Single(rig.Presenter.Follows);
        Assert.Equal(before, rig.Presenter.Selections.Count); // ni se quitó ni se cambió el resalte
        Assert.Equal(RadialDirection.East, rig.Current);

        // El desfase viaja en DIP: el cursor estaba 222 px a la derecha del centro dibujado (1932 − 1710).
        var offset = rig.Presenter.Follows[0].OffsetDip;
        Assert.True(offset.X > 40, "el cursor estaba fuera de la zona central, al este");
        Assert.Equal(0, offset.Y, 3);
    }

    [Fact]
    public void WhenTheRadialMustBeShifted_NoSectorIsSelectedUntilTheCursorMoves_AndNeverAJumpToAnotherSector()
    {
        var rig = ArmedEastOnA();
        RadialDirection? lastBefore = rig.Current;
        Assert.Equal(RadialDirection.East, lastBefore);

        // Camino del cursor hacia B, de 3 en 3 px: en el instante del cruce la selección pasa de «East» a ninguna, nunca a otro sector.
        var followedAt = -1;
        for (var x = 1830; x <= 1990; x += 3)
        {
            var followsBefore = rig.Presenter.Follows.Count;
            var selectedBefore = rig.Current;
            rig.Harness.Move(new ScreenPoint(x, 500));
            if (rig.Presenter.Follows.Count > followsBefore)
            {
                followedAt = x;
                Assert.True(rig.Current == selectedBefore || rig.Current is null, $"salto de {selectedBefore} a {rig.Current} al cruzar");
                Assert.Null(rig.Current); // el radial se desplazó: se espera a que el cursor se mueva
                break;
            }
        }

        Assert.True(followedAt >= 1932, $"cruzó en x = {followedAt}");
    }

    [Fact]
    public void AfterAShiftedFollow_ReleasingWithoutMovingExecutesNothing_EvenThoughTheCursorIsOverASector()
    {
        var rig = ArmedEastOnA();
        rig.Harness.Move(new ScreenPoint(1932, 500)); // cruce desplazado: el cursor queda en el borde oeste del radial nuevo

        rig.Harness.Release(new ScreenPoint(1932, 500));

        var result = Assert.Single(rig.Harness.Completed);
        Assert.Equal(CancelReason.NoSelection, result.Reason);
        Assert.Empty(rig.Harness.Executor.Executed);
    }

    [Fact]
    public async Task AfterAShiftedFollow_MovingTheCursorSelectsFromTheNewDrawnCenter()
    {
        var rig = ArmedEastOnA();
        rig.Harness.Move(new ScreenPoint(1932, 500));
        var center = rig.LastFollow.Center;
        Assert.True(center.X > 1932, "el radial nuevo está a la derecha del cursor, entero dentro de B");

        // 12 px más (≥ 10 DIP): la selección sale del centro nuevo. El cursor está a su oeste.
        rig.Harness.Move(new ScreenPoint(1944, 500));
        Assert.Equal(RadialDirection.West, rig.Current);

        rig.Harness.Release(new ScreenPoint(1944, 500));
        var (item, _) = await rig.Harness.NextFinishedAsync();
        Assert.Equal(West, item.Action);
    }

    [Fact]
    public void TheNewRadialIsEntirelyInsideTheWorkAreaOfTheTargetMonitor()
    {
        var rig = ArmedEastOnA();
        rig.Harness.Move(new ScreenPoint(1932, 500));
        var center = rig.LastFollow.Center;

        var work = rig.Locator.Find(new ScreenPoint(1932, 500))!.Value.Work;
        var window = OverlayPlacement.Centered(center, ViewDip, 96);
        Assert.True(window.X >= work.Left && window.Right <= work.Right && window.Y >= work.Top && window.Bottom <= work.Bottom);
    }

    [Fact]
    public async Task Confirming_NeverFollows_WhatIsExecutedIsWhatWasShown()
    {
        var rig = ArmedEastOnA();

        // El botón se suelta ya en B, sin que ningún sondeo anterior haya visto el cruce: se ejecuta lo que se veía (el este).
        rig.Harness.Release(new ScreenPoint(1960, 500));

        Assert.Empty(rig.Presenter.Follows);
        var (item, _) = await rig.Harness.NextFinishedAsync();
        Assert.Equal(East, item.Action);
    }

    // Escalas distintas

    [Fact]
    public void TheOffsetTravelsInDips_SoADifferentScaleOnTheOtherMonitorDoesNotChangeTheSector()
    {
        var rig = ArmedEastOnA(scaleA: 1.25, scaleB: 2.0, realisticFollow: false);
        var first = rig.Presenter.Selections.Count;

        // Windows deja el radial donde se le pide (sin ajuste), pero con la escala del monitor nuevo: 200 %.
        rig.Presenter.FollowBehavior = (cursor, offset) =>
            new PresentedPlacement(2.0, OverlayPlacement.IdealCenter(cursor, offset.X, offset.Y, 2.0));
        var cursor = new ScreenPoint(1960, 500);

        // Al 125 % el margen son 15 px: hay que alejarse 15 px de A.
        rig.Harness.Move(new ScreenPoint(1919 + 14, 500));
        Assert.Empty(rig.Presenter.Follows);
        rig.Harness.Move(cursor);

        var follow = Assert.Single(rig.Presenter.Follows);
        // El desfase en DIP es el de píxeles ÷ la escala del monitor de ORIGEN (1,25), no la del nuevo.
        var windowSize = OverlayPlacement.PhysicalSize(ViewDip, 120);
        var centerA = 1920 - (windowSize / 2);
        Assert.Equal((1960 - centerA) / 1.25, follow.OffsetDip.X, 1);
        Assert.Equal(first, rig.Presenter.Selections.Count);
    }

    [Fact]
    public void AfterTheFollow_EverythingIsMeasuredWithTheScaleOfTheNewMonitor()
    {
        // A al 100 %, B al 200 %: la zona central (40 DIP) son 80 px en B.
        var rig = ArmedEastOnA(scaleA: 1.0, scaleB: 2.0);
        rig.Harness.Move(new ScreenPoint(1932, 500));
        var center = rig.LastFollow.Center;
        Assert.Equal(2.0, rig.LastFollow.Scale);

        // Despertar la selección: 10 DIP son 20 px en B.
        rig.Harness.Move(new ScreenPoint(1932 + 19, 500));
        Assert.Null(rig.Current);
        rig.Harness.Move(new ScreenPoint(1932 + 20, 500));
        Assert.NotNull(rig.Current);

        // Desde el centro nuevo: 60 px = 30 DIP caen en la zona central; 120 px = 60 DIP, ya en el sector del este.
        rig.Harness.Move(new ScreenPoint(center.X + 60, center.Y));
        Assert.Null(rig.Current);
        rig.Harness.Move(new ScreenPoint(center.X + 120, center.Y));
        Assert.Equal(RadialDirection.East, rig.Current);
    }

    // Disposiciones

    [Fact]
    public void WithNegativeCoordinates_TheRadialFollowsInBothDirections_WithTheMarginOfTheMonitorItIsOn()
    {
        // Activación en «L» (escala 1, a la izquierda del principal), junto a su borde derecho.
        var layout = NegativeLeft(scaleL: 1.0, scaleR: 2.0);
        var rig = new Rig(layout, new ScreenPoint(-5, 500));
        rig.Activate(new ScreenPoint(-5, 500));
        rig.Harness.Move(new ScreenPoint(-40, 500));

        // Al principal: margen de 12 px (la escala de L).
        rig.Harness.Move(new ScreenPoint(10, 500));
        Assert.Empty(rig.Presenter.Follows);
        rig.Harness.Move(new ScreenPoint(12, 500));
        Assert.Single(rig.Presenter.Follows);
        Assert.Equal(2.0, rig.LastFollow.Scale);
        Assert.True(rig.LastFollow.Center.X >= 420, "el radial (840 px) cabe entero dentro del principal, que empieza en 0");

        // De vuelta a «L»: ahora el margen son 24 px (la escala del principal, donde está el radial).
        rig.Harness.Move(new ScreenPoint(-22, 500));
        Assert.Single(rig.Presenter.Follows);
        rig.Harness.Move(new ScreenPoint(-25, 500));
        Assert.Equal(2, rig.Presenter.Follows.Count);
        Assert.Equal(1.0, rig.LastFollow.Scale);
        Assert.True(rig.LastFollow.Center.X <= -210, "y entero dentro de «L»: su borde derecho es 0");
    }

    [Fact]
    public void AMonitorBelow_IsFollowedDownwards()
    {
        var rig = new Rig(Stacked(), new ScreenPoint(900, 1070));
        rig.Activate(new ScreenPoint(900, 1070));
        rig.Harness.Move(new ScreenPoint(900, 1075));

        rig.Harness.Move(new ScreenPoint(900, 1080 + 12));

        var follow = Assert.Single(rig.Presenter.Follows);
        Assert.Equal(new ScreenPoint(900, 1092), follow.Cursor);
        var window = OverlayPlacement.Centered(rig.LastFollow.Center, ViewDip, 96);
        Assert.True(window.Y >= 1080 && window.Bottom <= 2120, $"el radial nuevo ocupa de {window.Y} a {window.Bottom}");
    }

    [Fact]
    public void ATaskbarOnTheTargetMonitor_IsRespected_BecauseTheFitUsesTheWorkArea()
    {
        // B tiene una barra de tareas (80 px) abajo: el radial seguido no puede llegar a ella.
        var layout = new (MonitorAreas Area, double Scale)[]
        {
            (new MonitorAreas(new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1080)), 1.0),
            (new MonitorAreas(new ScreenRect(1920, 0, 3840, 1080), new ScreenRect(1920, 0, 3840, 1000)), 1.0),
        };
        var rig = new Rig(layout, new ScreenPoint(1800, 1000));
        rig.Activate(new ScreenPoint(1800, 1000));
        rig.Harness.Move(new ScreenPoint(1830, 1000));

        rig.Harness.Move(new ScreenPoint(1932, 1070)); // cruza por abajo, sobre la barra de B

        var window = OverlayPlacement.Centered(rig.LastFollow.Center, ViewDip, 96);
        Assert.True(window.Bottom <= 1000, $"la ventana llega a {window.Bottom} y la barra de B empieza en 1000");
    }

    [Fact]
    public void ARadialThatCannotFitOnTheTargetMonitor_IsCenteredOnThatAxis_AndStaysStable()
    {
        // B es más bajo (300 px) que el radial (420): no cabe en vertical. Se centra en B en ese eje; nada se rompe y se puede seguir.
        var layout = new (MonitorAreas Area, double Scale)[]
        {
            (new MonitorAreas(new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1080)), 1.0),
            (new MonitorAreas(new ScreenRect(1920, 0, 3840, 300), new ScreenRect(1920, 0, 3840, 300)), 1.0),
        };
        var rig = new Rig(layout, new ScreenPoint(1800, 200));
        rig.Activate(new ScreenPoint(1800, 200));
        rig.Harness.Move(new ScreenPoint(1830, 200));

        rig.Harness.Move(new ScreenPoint(1932, 200));

        Assert.Equal(150, rig.LastFollow.Center.Y); // centrado en los 300 px de B
        Assert.True(rig.Harness.Coordinator.IsActive);
        rig.Harness.Move(new ScreenPoint(1990, 200)); // y sigue respondiendo
        Assert.NotNull(rig.Current);
    }

    // Seguridad

    [Fact]
    public void IfFollowingFails_TheRadialIsClosedAndNothingIsExecuted()
    {
        var rig = ArmedEastOnA();
        rig.Presenter.FailOnFollow = new InvalidOperationException("la ventana no se pudo mover");

        Assert.Throws<InvalidOperationException>(() => rig.Harness.Move(new ScreenPoint(1932, 500)));

        Assert.False(rig.Presenter.Visible);
        Assert.False(rig.Harness.Coordinator.IsActive);
        Assert.Empty(rig.Harness.Executor.Executed);
    }

    [Fact]
    public void ADpiNoticeRightAfterAFollow_ChangesNothing_NeitherTheSelectionNorTheWait()
    {
        var rig = ArmedEastOnA();
        rig.Harness.Move(new ScreenPoint(1932, 500)); // cruce desplazado: esperando a que se mueva el cursor
        var placed = rig.LastFollow;

        // Windows avisa del cambio de DPI de la ventana que ya se acaba de colocar (mismo centro, misma escala).
        rig.Presenter.RaisePlacementChanged(placed);
        rig.Harness.Move(new ScreenPoint(1932, 500));

        Assert.Null(rig.Current); // sigue la espera: el aviso no la apagó ni la volvió a encender
        rig.Harness.Release(new ScreenPoint(1932, 500));
        Assert.Equal(CancelReason.NoSelection, Assert.Single(rig.Harness.Completed).Reason);
    }

    [Fact]
    public void ACrossingWhileWaiting_RestartsTheWaitFromTheCrossingPoint()
    {
        // Activado y sin mover (esperando), el cursor cruza a B más de un margen: el radial lo sigue y la espera vuelve a empezar allí.
        var rig = new Rig(SideBySide(), new ScreenPoint(1800, 500));
        rig.Activate(new ScreenPoint(1800, 500));

        rig.Harness.Move(new ScreenPoint(1800 + 9, 500)); // menos de 10 DIP: sigue esperando
        Assert.Null(rig.Current);
        rig.Harness.Move(new ScreenPoint(1932, 500));      // cruza (y ya se había movido más de 10 DIP desde la activación)

        Assert.Single(rig.Presenter.Follows);
        Assert.Null(rig.Current); // pero la espera se reinició en el punto del cruce: no hay selección todavía
        rig.Harness.Move(new ScreenPoint(1932 + 9, 500));
        Assert.Null(rig.Current);
        rig.Harness.Move(new ScreenPoint(1932 + 10, 500));
        Assert.NotNull(rig.Current);
    }

    // Diagnóstico del seguimiento («[monitores]» en Diagnóstico): qué recibió Radial, qué monitor detectó y si pidió trasladar la ventana

    private static (Rig Rig, List<string> Trace) Traced(ScreenPoint activation)
    {
        var rig = new Rig(SideBySide(), activation);
        var trace = new List<string>();
        rig.Harness.Coordinator.FollowTrace += trace.Add;
        rig.Activate(activation);
        return (rig, trace);
    }

    [Fact]
    public void TheActivationTrace_SaysWhichMonitorTheRadialIsOn_AndThatFollowingIsActive()
    {
        var (_, trace) = Traced(new ScreenPoint(1800, 500));

        var line = Assert.Single(trace);
        Assert.Contains("(1800, 500)", line);
        Assert.Contains("monitor del radial (0, 0)–(1920, 1080)", line);
        Assert.Contains("área de trabajo (0, 0)–(1920, 1040)", line);
        Assert.Contains("seguimiento entre monitores: activo", line);
    }

    [Fact]
    public void ACrossing_LeavesTheTraceOfWhatWasReceivedWhichMonitorWasDetectedAndWhatWasAsked()
    {
        var (rig, trace) = Traced(new ScreenPoint(1800, 500));
        rig.Harness.Move(new ScreenPoint(1830, 500));

        rig.Harness.Move(new ScreenPoint(1925, 500)); // en B, pero a 6 px de A
        rig.Harness.Move(new ScreenPoint(1932, 500)); // 13 px: sigue

        Assert.Equal(3, trace.Count); // activación, «todavía no», «sigue»
        Assert.Contains("el cursor (1925, 500) está en el monitor (1920, 0)–(3840, 1200)", trace[1]);
        Assert.Contains("a 6 px del monitor del radial (0, 0)–(1920, 1080)", trace[1]);
        Assert.Contains("todavía no lo sigue (margen 12 px)", trace[1]);
        Assert.Contains("el radial sigue al cursor (1932, 500) al monitor (1920, 0)–(3840, 1200)", trace[2]);
        Assert.Contains("centro (", trace[2]);
        Assert.Contains("desplazado por el borde", trace[2]);
    }

    [Fact]
    public void StayingInTheSameState_DoesNotRepeatTheLine()
    {
        var (rig, trace) = Traced(new ScreenPoint(1800, 500));
        rig.Harness.Move(new ScreenPoint(1830, 500));

        for (var i = 0; i < 6; i++)
        {
            rig.Harness.Move(new ScreenPoint(1925, 500));
        }

        Assert.Equal(2, trace.Count); // activación y UNA línea de «todavía no»
    }

    [Fact]
    public void ACursorTremblingOnTheSharedEdge_CannotFloodTheDiagnostics()
    {
        var (rig, trace) = Traced(new ScreenPoint(1800, 500));
        rig.Harness.Move(new ScreenPoint(1830, 500));

        for (var i = 0; i < 60; i++)
        {
            rig.Harness.Move(new ScreenPoint(1921, 500)); // otro monitor, a 2 px
            rig.Harness.Move(new ScreenPoint(1900, 500)); // de vuelta
        }

        Assert.Equal(RadialOverlayCoordinator.MaxWaitTraces, trace.Count(l => l.Contains("todavía no")));
        Assert.Empty(rig.Presenter.Follows);
    }

    [Fact]
    public void WithoutALocator_TheActivationTraceSaysSoPlainly_AndThereIsNeverAFollow()
    {
        var presenter = new RecordingPresenter();
        var h = new CoordinatorHarness(Config(), presenter);
        var trace = new List<string>();
        h.Coordinator.FollowTrace += trace.Add;
        h.Move(CoordinatorHarness.Start);
        h.Activate();

        h.Move(new ScreenPoint(CoordinatorHarness.Start.X + 5_000, CoordinatorHarness.Start.Y));

        Assert.False(h.Coordinator.FollowsMonitors);
        var line = Assert.Single(trace);
        Assert.Contains("NO hay localizador de monitores", line);
    }

    [Fact]
    public void IfWindowsDoesNotGiveTheMonitorOfTheRadial_TheTraceSaysSo()
    {
        var presenter = new RecordingPresenter();
        var h = new CoordinatorHarness(Config(), presenter, monitors: new NoMonitors());
        var trace = new List<string>();
        h.Coordinator.FollowTrace += trace.Add;
        h.Move(CoordinatorHarness.Start);

        h.Activate();

        Assert.Contains("Windows no dio el monitor", Assert.Single(trace));
    }

    [Fact]
    public void IfTheCursorMonitorIsLostMidGesture_ItIsReportedOnceAndNothingBreaks()
    {
        var locator = new SwitchableMonitors(SideBySide().Select(l => l.Area).ToArray());
        var presenter = new RecordingPresenter();
        var h = new CoordinatorHarness(Config(), presenter, monitors: locator);
        var trace = new List<string>();
        h.Coordinator.FollowTrace += trace.Add;
        h.Move(new ScreenPoint(1000, 500));
        h.Activate();

        locator.Working = false;
        h.Move(new ScreenPoint(1500, 500));
        h.Move(new ScreenPoint(1600, 500));

        Assert.Single(trace, l => l.Contains("Windows no dio el monitor del cursor"));
        Assert.Empty(presenter.Follows);
        Assert.True(h.Coordinator.IsActive);
    }

    private sealed class SwitchableMonitors(MonitorAreas[] monitors) : IMonitorLocator
    {
        public bool Working { get; set; } = true;

        public MonitorAreas? Find(ScreenPoint point) =>
            Working ? monitors.OrderBy(m => MonitorFollow.DistanceOutside(m.Bounds, point)).First() : null;
    }

    [Fact]
    public void ASubscriberOfTheTrace_ThatThrows_CannotBreakTheGestureNorTheFollow()
    {
        var rig = new Rig(SideBySide(), new ScreenPoint(1800, 500));
        rig.Harness.Coordinator.FollowTrace += _ => throw new InvalidOperationException("el diagnóstico falló");
        rig.Activate(new ScreenPoint(1800, 500));
        rig.Harness.Move(new ScreenPoint(1830, 500));

        rig.Harness.Move(new ScreenPoint(1932, 500));

        Assert.Single(rig.Presenter.Follows);
        Assert.True(rig.Harness.Coordinator.IsActive);
    }

    [Fact]
    public void IfTheWindowCannotBeMoved_TheTraceSaysItFailed_AndTheRadialIsClosed()
    {
        var (rig, trace) = Traced(new ScreenPoint(1800, 500));
        rig.Harness.Move(new ScreenPoint(1830, 500));
        rig.Presenter.FailOnFollow = new InvalidOperationException("no se pudo mover");

        Assert.Throws<InvalidOperationException>(() => rig.Harness.Move(new ScreenPoint(1932, 500)));

        Assert.Contains(trace, l => l.Contains("FALLÓ al trasladar el radial"));
        Assert.False(rig.Presenter.Visible);
    }

    [Fact]
    public void ANewGesture_StartsItsTraceAgain_WithItsOwnLimit()
    {
        var (rig, trace) = Traced(new ScreenPoint(1800, 500));
        rig.Harness.Move(new ScreenPoint(1830, 500));
        for (var i = 0; i < 30; i++)
        {
            rig.Harness.Move(new ScreenPoint(1921, 500));
            rig.Harness.Move(new ScreenPoint(1900, 500));
        }

        rig.Harness.Cancel(CancelCause.Escape);
        rig.Harness.Move(new ScreenPoint(1800, 500));
        rig.Harness.Activate(new ScreenPoint(1800, 500));
        rig.Harness.Move(new ScreenPoint(1830, 500));
        rig.Harness.Move(new ScreenPoint(1921, 500));

        Assert.Equal(2, trace.Count(l => l.Contains("seguimiento entre monitores: activo")));
        Assert.True(trace.Count(l => l.Contains("todavía no")) > RadialOverlayCoordinator.MaxWaitTraces); // la del gesto nuevo sí sale
    }
}
