using Radial.Core.Geometry;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

/// <summary>
/// El ajuste del radial a los bordes del monitor (<see cref="OverlayPlacement.Fit"/>): cálculo puro, sin ventanas ni Windows. Cubre
/// bordes, esquinas, monitores con origen negativo (a la izquierda o encima del principal), escalas distintas y configuraciones
/// extremas. Lo que no se puede probar aquí (qué monitor elige Windows, el DPI real) está en la validación manual.
/// </summary>
public class OverlayPlacementFitTests
{
    private const double ViewDip = 420;

    // Distintas disposiciones (derecha y abajo exclusivos): principal, secundario a la izquierda con coordenadas negativas,
    // secundario encima, vertical y un monitor pequeño.
    public static TheoryData<int, int, int, int> Monitors => new()
    {
        { 0, 0, 1920, 1080 },
        { -1920, 0, 0, 1080 },
        { 0, -1080, 1920, 0 },
        { 2560, 200, 2560 + 1080, 200 + 1920 },
        { -3840, -400, 0, 1760 },
        { 0, 0, 1366, 768 },
    };

    private static IEnumerable<int> Probes(int low, int high)
    {
        var size = high - low;
        foreach (var offset in new[] { -400, -1, 0, 1, 5, 50, 209, 210, 211, 300, size / 2, size - 300, size - 211, size - 210, size - 209, size - 50, size - 5, size - 2, size - 1, size, size + 1, size + 400 })
        {
            yield return low + offset;
        }
    }

    private static IEnumerable<int> Dpis() => [96, 120, 144, 168, 192];

    [Theory]
    [MemberData(nameof(Monitors))]
    public void TheWholeWindowFitsInsideTheMonitor_ForEveryCursorPosition_AndEveryScale(int left, int top, int right, int bottom)
    {
        var monitor = new ScreenRect(left, top, right, bottom);

        foreach (var dpi in Dpis())
        {
            var size = OverlayPlacement.PhysicalSize(ViewDip, dpi);
            var fits = monitor.Width >= size && monitor.Height >= size;
            foreach (var x in Probes(left, right))
            {
                foreach (var y in Probes(top, bottom))
                {
                    var center = OverlayPlacement.Fit(new ScreenPoint(x, y), size, monitor);
                    var window = OverlayPlacement.Centered(center, ViewDip, dpi);

                    if (fits)
                    {
                        Assert.True(
                            window.X >= left && window.Y >= top && window.Right <= right && window.Bottom <= bottom,
                            $"dpi {dpi}, cursor ({x}, {y}) → ventana ({window.X}, {window.Y}, {window.Size}) fuera de ({left}, {top}, {right}, {bottom})");
                    }

                    Assert.Equal(size, window.Size); // el ajuste nunca cambia el tamaño: mismas proporciones y distancias
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Monitors))]
    public void ACursorAwayFromTheEdges_KeepsTheCursorAsTheCenter(int left, int top, int right, int bottom)
    {
        var monitor = new ScreenRect(left, top, right, bottom);
        var size = OverlayPlacement.PhysicalSize(ViewDip, 96);
        var middle = new ScreenPoint((left + right) / 2, (top + bottom) / 2);

        if (monitor.Width > size + 2 && monitor.Height > size + 2)
        {
            Assert.Equal(middle, OverlayPlacement.Fit(middle, size, monitor));
        }
    }

    [Theory]
    [MemberData(nameof(Monitors))]
    public void EveryPointOfTheDrawnWheel_IsPhysicallyReachable_AndSelectsTheDrawnDirection(int left, int top, int right, int bottom)
    {
        var monitor = new ScreenRect(left, top, right, bottom);

        foreach (var dpi in Dpis())
        {
            var size = OverlayPlacement.PhysicalSize(ViewDip, dpi);
            if (monitor.Width < size || monitor.Height < size)
            {
                continue; // un monitor más pequeño que el radial se prueba aparte
            }

            // El cursor pegado a cada borde y a cada esquina.
            foreach (var x in new[] { left, right - 1 })
            {
                foreach (var y in new[] { top, bottom - 1 })
                {
                    var center = OverlayPlacement.Fit(new ScreenPoint(x, y), size, monitor);
                    var origin = new PointD(center.X, center.Y);
                    var reach = (size / 2) - 2; // casi el borde exterior del dibujo
                    foreach (var direction in Enum.GetValues<RadialDirection>())
                    {
                        var target = RadialLayout.PointAt(origin, reach, RadialGeometry.CenterAngle(direction));
                        var px = (int)Math.Round(target.X);
                        var py = (int)Math.Round(target.Y);

                        Assert.True(
                            px >= left && px < right && py >= top && py < bottom,
                            $"dpi {dpi}, cursor ({x}, {y}): el sector {direction} cae fuera del monitor en ({px}, {py})");

                        var selected = new SectorSelector(0).Update(origin, new PointD(px, py), deadZoneRadius: 40 * OverlayPlacement.Scale(dpi));
                        Assert.Equal<RadialDirection?>(direction, selected);
                    }
                }
            }
        }
    }

    [Fact]
    public void ATopLeftCursor_ThatWouldLeaveTheWheelHalfOffScreen_IsMovedInwardsExactlyHalfAWindow()
    {
        var monitor = new ScreenRect(0, 0, 1920, 1080);
        var size = OverlayPlacement.PhysicalSize(ViewDip, 96); // 420

        var center = OverlayPlacement.Fit(new ScreenPoint(0, 0), size, monitor);

        Assert.Equal(new ScreenPoint(210, 210), center);
    }

    [Fact]
    public void TheBottomRightPixel_UsesTheExclusiveLimit()
    {
        var monitor = new ScreenRect(0, 0, 1920, 1080);
        var size = OverlayPlacement.PhysicalSize(ViewDip, 96);

        var center = OverlayPlacement.Fit(new ScreenPoint(1919, 1079), size, monitor);
        var window = OverlayPlacement.Centered(center, ViewDip, 96);

        Assert.Equal(1920, window.Right);
        Assert.Equal(1080, window.Bottom);
    }

    [Fact]
    public void ASecondaryMonitorToTheLeft_WithNegativeCoordinates_IsHandledLikeAnyOther()
    {
        var monitor = new ScreenRect(-1920, 0, 0, 1080);
        var size = OverlayPlacement.PhysicalSize(ViewDip, 96);

        var center = OverlayPlacement.Fit(new ScreenPoint(-1, 540), size, monitor);

        Assert.Equal(new ScreenPoint(-210, 540), center);
    }

    [Fact]
    public void ADifferentScaleOnTheMonitor_ChangesTheShiftBecauseTheWindowIsBiggerInPixels()
    {
        var monitor = new ScreenRect(0, 0, 3840, 2160);

        var at100 = OverlayPlacement.Fit(new ScreenPoint(0, 1000), OverlayPlacement.PhysicalSize(ViewDip, 96), monitor);
        var at200 = OverlayPlacement.Fit(new ScreenPoint(0, 1000), OverlayPlacement.PhysicalSize(ViewDip, 192), monitor);

        Assert.Equal(210, at100.X);
        Assert.Equal(420, at200.X); // 420 DIP al 200 % son 840 px: la mitad, 420
        Assert.Equal(1000, at100.Y);
        Assert.Equal(1000, at200.Y);
    }

    [Fact]
    public void AMonitorSmallerThanTheWindow_CentersTheWheelOnThatAxis_WithoutThrowing()
    {
        var monitor = new ScreenRect(0, 0, 1280, 360); // más bajo que los 420 px de la ventana

        var center = OverlayPlacement.Fit(new ScreenPoint(10, 10), 420, monitor);

        Assert.Equal(210, center.X);   // el eje que sí cabe se ajusta
        Assert.Equal(180, center.Y);   // el que no cabe queda centrado en el monitor
    }

    [Fact]
    public void ACursorOutsideTheMonitor_IsBroughtInside()
    {
        var monitor = new ScreenRect(0, 0, 1920, 1080);

        var center = OverlayPlacement.Fit(new ScreenPoint(5000, -300), 420, monitor);

        Assert.Equal(new ScreenPoint(1710, 210), center);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(10, 10, 10, 50)]
    [InlineData(100, 100, 50, 50)]
    public void AnUnusableMonitorRectangle_LeavesTheCenterUntouched(int left, int top, int right, int bottom)
    {
        var desired = new ScreenPoint(3, 4);

        Assert.Equal(desired, OverlayPlacement.Fit(desired, 420, new ScreenRect(left, top, right, bottom)));
    }

    [Fact]
    public void ExtremeCoordinates_DoNotOverflow()
    {
        var monitor = new ScreenRect(int.MinValue + 10, int.MinValue + 10, int.MaxValue - 10, int.MaxValue - 10);

        var center = OverlayPlacement.Fit(new ScreenPoint(int.MaxValue, int.MinValue), 420, monitor);

        Assert.Equal(int.MaxValue - 10 - 210, center.X);
        Assert.Equal(int.MinValue + 10 + 210, center.Y);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void ANonPositiveSize_IsRejected(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayPlacement.Fit(default, size, new ScreenRect(0, 0, 100, 100)));

    [Fact]
    public void TheCenterOfAFittedWindow_IsExactlyThePointThatWasReturned()
    {
        var monitor = new ScreenRect(0, 0, 1920, 1080);
        foreach (var dpi in Dpis())
        {
            var size = OverlayPlacement.PhysicalSize(ViewDip, dpi);
            var center = OverlayPlacement.Fit(new ScreenPoint(0, 1079), size, monitor);

            Assert.Equal(center, OverlayPlacement.Centered(center, ViewDip, dpi).Center);
        }
    }

    [Theory]
    [InlineData(0, 1079)]    // cursor sobre la barra de tareas (fuera del área de trabajo pero dentro del monitor)
    [InlineData(960, 1100)]
    [InlineData(1919, 1199)]
    public void WithADockedTaskbar_TheWindowFitsInsideTheWorkArea_SoTheTaskbarNeverCoversTheWheel(int x, int y)
    {
        // Monitor 1920x1200 con una barra de tareas de 48 px abajo: el área de trabajo termina en 1152.
        var work = new ScreenRect(0, 0, 1920, 1152);
        var size = OverlayPlacement.PhysicalSize(ViewDip, 120); // 526

        var center = OverlayPlacement.Fit(new ScreenPoint(x, y), size, work);
        var window = OverlayPlacement.Centered(center, ViewDip, 120);

        Assert.True(window.Bottom <= 1152, $"la ventana llega a {window.Bottom} y la barra empieza en 1152");
        Assert.True(window.X >= 0 && window.Right <= 1920 && window.Y >= 0);
    }

    [Fact]
    public void ACursorOnTheTaskbar_StillReachesEverySector_BecauseTheCursorMayGoAnywhereInTheMonitor()
    {
        var bounds = new ScreenRect(0, 0, 1920, 1200);
        var work = new ScreenRect(0, 0, 1920, 1152);
        var size = OverlayPlacement.PhysicalSize(ViewDip, 120);
        var center = OverlayPlacement.Fit(new ScreenPoint(960, 1199), size, work);
        var origin = new PointD(center.X, center.Y);

        foreach (var direction in Enum.GetValues<RadialDirection>())
        {
            var target = RadialLayout.PointAt(origin, (size / 2) - 2, RadialGeometry.CenterAngle(direction));
            var px = (int)Math.Round(target.X);
            var py = (int)Math.Round(target.Y);

            Assert.True(px >= bounds.Left && px < bounds.Right && py >= bounds.Top && py < bounds.Bottom, $"{direction} cae fuera del monitor");
            Assert.Equal<RadialDirection?>(direction, new SectorSelector(0).Update(origin, new PointD(px, py), deadZoneRadius: 50));
        }
    }
}
