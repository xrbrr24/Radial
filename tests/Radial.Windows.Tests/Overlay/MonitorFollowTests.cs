using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

/// <summary>
/// La lógica pura del seguimiento del cursor entre monitores: cuándo se cruza (con histéresis), la distancia a un monitor con
/// coordenadas negativas o en cualquier disposición, y el centro «ideal» que conserva el desfase del cursor en DIP.
/// </summary>
public class MonitorFollowTests
{
    private static readonly MonitorAreas Left = new(new ScreenRect(-1920, 0, 0, 1080), new ScreenRect(-1920, 0, 0, 1080));
    private static readonly MonitorAreas Main = new(new ScreenRect(0, 0, 2560, 1440), new ScreenRect(0, 0, 2560, 1400));
    private static readonly MonitorAreas Above = new(new ScreenRect(0, -1080, 1920, 0), new ScreenRect(0, -1080, 1920, 0));

    // Distancia hasta un rectángulo

    [Theory]
    [InlineData(0, 0, 0)]        // esquina superior izquierda: dentro
    [InlineData(2559, 1439, 0)]  // último píxel: dentro (derecha y abajo son exclusivos)
    [InlineData(2560, 700, 1)]   // primer píxel fuera por la derecha
    [InlineData(2575, 700, 16)]
    [InlineData(-1, 700, 1)]
    [InlineData(-12, 700, 12)]
    [InlineData(700, -1, 1)]
    [InlineData(700, 1440, 1)]
    [InlineData(-5, -30, 30)]    // por una esquina, manda el eje más lejano
    [InlineData(2600, 1500, 61)]
    public void TheDistanceOutsideARectangle_IsTheFarthestAxis_AndZeroInside(int x, int y, int expected) =>
        Assert.Equal(expected, MonitorFollow.DistanceOutside(Main.Bounds, new ScreenPoint(x, y)));

    [Fact]
    public void TheDistanceOutsideAMonitorWithNegativeCoordinates_IsMeasuredTheSameWay()
    {
        Assert.Equal(0, MonitorFollow.DistanceOutside(Left.Bounds, new ScreenPoint(-1, 500)));    // su último píxel
        Assert.Equal(1, MonitorFollow.DistanceOutside(Left.Bounds, new ScreenPoint(0, 500)));     // ya en el monitor de al lado
        Assert.Equal(13, MonitorFollow.DistanceOutside(Left.Bounds, new ScreenPoint(12, 500)));
        Assert.Equal(0, MonitorFollow.DistanceOutside(Left.Bounds, new ScreenPoint(-1920, 0)));   // su primer píxel
        Assert.Equal(1, MonitorFollow.DistanceOutside(Left.Bounds, new ScreenPoint(-1921, 0)));
    }

    [Fact]
    public void ExtremeCoordinates_DoNotOverflow()
    {
        var huge = new ScreenRect(int.MinValue + 5, int.MinValue + 5, int.MaxValue - 5, int.MaxValue - 5);

        Assert.Equal(0, MonitorFollow.DistanceOutside(huge, new ScreenPoint(0, 0)));
        Assert.True(MonitorFollow.DistanceOutside(huge, new ScreenPoint(int.MaxValue, 0)) > 0);
        Assert.True(MonitorFollow.DistanceOutside(huge, new ScreenPoint(int.MinValue, int.MaxValue)) > 0);
    }

    // Cuándo seguir

    [Fact]
    public void WithinTheSameMonitor_ThereIsNeverAFollow() =>
        Assert.False(MonitorFollow.ShouldFollow(Main, Main, new ScreenPoint(2559, 1439), marginPixels: 12));

    [Theory]
    [InlineData(0, false)]   // acaba de pasar al monitor de al lado: dista 1 px del anterior (distancia = x + 1)
    [InlineData(5, false)]
    [InlineData(10, false)]  // distancia 11: aún no
    [InlineData(11, true)]   // distancia 12: el margen
    [InlineData(80, true)]
    public void TheRadialOnlyFollows_OnceTheCursorMovedTheMarginAwayFromItsMonitor(int xOnTheNextMonitor, bool follows)
    {
        // El radial está en «Left» (a la izquierda, coordenadas negativas) y el cursor entra en «Main» (a la derecha).
        var cursor = new ScreenPoint(xOnTheNextMonitor, 500);

        Assert.Equal(follows, MonitorFollow.ShouldFollow(Left, Main, cursor, marginPixels: 12));
    }

    [Fact]
    public void TheMarginIsSymmetric_AndNeverLetsTheRadialPingPongOnTheSharedEdge()
    {
        // El radial ya está en «Main». Volver a «Left» exige alejarse el margen del rectángulo de «Main», no del de «Left».
        Assert.False(MonitorFollow.ShouldFollow(Main, Left, new ScreenPoint(-1, 500), marginPixels: 12));
        Assert.False(MonitorFollow.ShouldFollow(Main, Left, new ScreenPoint(-11, 500), marginPixels: 12));
        Assert.True(MonitorFollow.ShouldFollow(Main, Left, new ScreenPoint(-12, 500), marginPixels: 12));

        // Un temblor de ±3 px alrededor del borde compartido no cruza en ninguno de los dos sentidos.
        for (var x = -3; x <= 3; x++)
        {
            var cursor = new ScreenPoint(x, 500);
            var onMain = x >= 0;
            Assert.False(MonitorFollow.ShouldFollow(onMain ? Left : Main, onMain ? Main : Left, cursor, 12));
        }
    }

    [Fact]
    public void AMonitorAbove_IsHandledLikeAMonitorToTheSide()
    {
        Assert.False(MonitorFollow.ShouldFollow(Main, Above, new ScreenPoint(500, -11), marginPixels: 12));
        Assert.True(MonitorFollow.ShouldFollow(Main, Above, new ScreenPoint(500, -12), marginPixels: 12));
    }

    [Fact]
    public void ADiagonalNeighbour_UsesTheFarthestAxis()
    {
        // Entrando en «Above» por una esquina de «Main»: 12 px por arriba y 3 por la izquierda (manda el eje más lejano).
        Assert.True(MonitorFollow.ShouldFollow(Main, Above, new ScreenPoint(-3, -12), marginPixels: 12));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AMarginBelowOne_StillNeedsTheCursorToBeOutsideTheMonitor(int margin)
    {
        // Un margen absurdo no hace que se «siga» a un cursor que sigue sobre el monitor del radial (distancia 0 < 1).
        Assert.False(MonitorFollow.ShouldFollow(Main, Left, new ScreenPoint(5, 5), margin));
        Assert.True(MonitorFollow.ShouldFollow(Main, Left, new ScreenPoint(-1, 5), margin));
    }

    // Centro ideal

    [Fact]
    public void WithoutOffset_TheIdealCenterIsTheCursor() =>
        Assert.Equal(new ScreenPoint(900, 400), OverlayPlacement.IdealCenter(new ScreenPoint(900, 400), 0, 0, 1.5));

    [Theory]
    [InlineData(1.0, 100, 50, 800, 350)]
    [InlineData(1.25, 100, 50, 775, 337)]   // 125 px y 62,5 px (se admite 1 px de diferencia por el redondeo)
    [InlineData(2.0, 100, 50, 700, 300)]
    [InlineData(1.0, -100, -50, 1000, 450)]
    public void TheIdealCenter_KeepsTheOffsetInDips_ConvertedWithTheNewScale(double scale, double offsetX, double offsetY, int expectedX, int expectedY)
    {
        var center = OverlayPlacement.IdealCenter(new ScreenPoint(900, 400), offsetX, offsetY, scale);

        // El redondeo es al píxel más cercano (mitades alejándose de cero): como mucho 1 px de diferencia con el valor exacto.
        Assert.InRange(Math.Abs(center.X - expectedX), 0, 1);
        Assert.InRange(Math.Abs(center.Y - expectedY), 0, 1);
    }

    [Fact]
    public void TheSameOffsetInDips_IsDoubleAsManyPixelsAtDoubleTheScale()
    {
        var cursor = new ScreenPoint(2000, 700);

        var at100 = OverlayPlacement.IdealCenter(cursor, 150, -40, 1.0);
        var at200 = OverlayPlacement.IdealCenter(cursor, 150, -40, 2.0);

        Assert.Equal(150, cursor.X - at100.X);
        Assert.Equal(300, cursor.X - at200.X);
        Assert.Equal(-40, cursor.Y - at100.Y);
        Assert.Equal(-80, cursor.Y - at200.Y);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnInvalidScale_IsRejected(double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayPlacement.IdealCenter(new ScreenPoint(1, 1), 1, 1, scale));

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void ANonFiniteOffset_IsRejected(double x, double y) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayPlacement.IdealCenter(new ScreenPoint(1, 1), x, y, 1.0));
}
