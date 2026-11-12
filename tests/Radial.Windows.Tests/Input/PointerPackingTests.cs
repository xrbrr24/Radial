using Radial.Windows.Input;

namespace Radial.Windows.Tests.Input;

/// <summary>
/// La posición del cursor que publica el hilo del hook (en un solo <c>long</c>) tiene que conservar el signo de las dos coordenadas: un
/// monitor a la izquierda o encima del principal tiene coordenadas negativas, y el radial solo puede seguir al cursor a ese monitor si
/// el coordinador recibe la posición intacta.
/// </summary>
public class PointerPackingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1919, 1199)]
    [InlineData(-1, 540)]            // último píxel de un monitor a la izquierda del principal
    [InlineData(-1920, 0)]
    [InlineData(-3840, -400)]        // monitor a la izquierda Y más arriba
    [InlineData(500, -1)]            // monitor encima
    [InlineData(-1, -1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(int.MinValue, int.MinValue)]
    [InlineData(int.MinValue, int.MaxValue)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void APositionSurvivesBeingPackedAndUnpacked(int x, int y)
    {
        var point = new ScreenPoint(x, y);

        Assert.Equal(point, PointerPacking.Unpack(PointerPacking.Pack(point)));
    }

    [Fact]
    public void TheZeroPackedValue_IsTheOrigin() => Assert.Equal(new ScreenPoint(0, 0), PointerPacking.Unpack(0));

    [Fact]
    public void ANegativeYDoesNotCorruptX_AndViceVersa()
    {
        Assert.Equal(new ScreenPoint(7, -3), PointerPacking.Unpack(PointerPacking.Pack(new ScreenPoint(7, -3))));
        Assert.Equal(new ScreenPoint(-7, 3), PointerPacking.Unpack(PointerPacking.Pack(new ScreenPoint(-7, 3))));
    }
}
