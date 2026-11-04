using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

public class OverlayPlacementTests
{
    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(120, 1.25)]
    [InlineData(144, 1.5)]
    [InlineData(192, 2.0)]
    public void Scale_IsTheDpiOver96(int dpi, double expected) => Assert.Equal(expected, OverlayPlacement.Scale(dpi));

    [Theory]
    [InlineData(400, 96, 400)]
    [InlineData(400, 120, 500)]
    [InlineData(400, 144, 600)]
    [InlineData(400, 192, 800)]
    [InlineData(400, 168, 700)] // 175 %
    public void PhysicalSize_ScalesTheDipSizeWithTheMonitorDpi(double sizeDip, int dpi, int expected) =>
        Assert.Equal(expected, OverlayPlacement.PhysicalSize(sizeDip, dpi));

    [Theory]
    [InlineData(401, 96)]
    [InlineData(333, 120)]
    [InlineData(333, 144)]
    [InlineData(417, 168)]
    public void PhysicalSize_IsAlwaysEven_SoTheCenterIsExactlyOnAPixel(double sizeDip, int dpi) =>
        Assert.Equal(0, OverlayPlacement.PhysicalSize(sizeDip, dpi) % 2);

    [Fact]
    public void PhysicalSize_IsNeverSmallerThanTwoPixels() => Assert.Equal(2, OverlayPlacement.PhysicalSize(0.1, 96));

    [Theory]
    [InlineData(1000, 500, 96)]
    [InlineData(1000, 500, 120)]
    [InlineData(1000, 500, 192)]
    [InlineData(0, 0, 96)]
    public void Centered_PutsTheCursorExactlyInTheMiddle(int x, int y, int dpi)
    {
        var bounds = OverlayPlacement.Centered(new ScreenPoint(x, y), 400, dpi);

        Assert.Equal(x, bounds.X + bounds.Size / 2);
        Assert.Equal(y, bounds.Y + bounds.Size / 2);
        Assert.Equal(bounds.Size, bounds.Right - bounds.X);
        Assert.Equal(bounds.Size, bounds.Bottom - bounds.Y);
    }

    [Fact]
    public void Centered_WorksWithNegativeCoordinates_OfAMonitorToTheLeftOrAbove()
    {
        // Monitor secundario a la izquierda del principal: el escritorio virtual tiene X negativas.
        var bounds = OverlayPlacement.Centered(new ScreenPoint(-1500, -200), 400, 96);

        Assert.Equal(-1700, bounds.X);
        Assert.Equal(-400, bounds.Y);
        Assert.Equal(400, bounds.Size);
    }

    [Fact]
    public void Centered_DoesNotClampToTheScreen_SoItCanOverflowAtAnEdge()
    {
        var bounds = OverlayPlacement.Centered(new ScreenPoint(5, 5), 400, 96);

        Assert.True(bounds.X < 0);
        Assert.True(bounds.Y < 0);
    }

    [Fact]
    public void InvalidInput_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayPlacement.Scale(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayPlacement.Scale(-96));
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayPlacement.PhysicalSize(0, 96));
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayPlacement.PhysicalSize(double.NaN, 96));
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayPlacement.PhysicalSize(400, 0));
    }

    [Fact]
    public void TheOverlay_AnswersMouseActivationWithNoActivate_AndIgnoresOtherMessages()
    {
        Assert.True(OverlayWindowInterop.TryHandleMessage(0x0021, out var result)); // WM_MOUSEACTIVATE
        Assert.Equal(3, (int)result); // MA_NOACTIVATE

        Assert.False(OverlayWindowInterop.TryHandleMessage(0x0200, out var other)); // WM_MOUSEMOVE
        Assert.Equal(IntPtr.Zero, other);
    }
}
