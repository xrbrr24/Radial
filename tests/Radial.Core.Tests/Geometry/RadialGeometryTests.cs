using Radial.Core.Geometry;
using Radial.Core.Tests.Support;

namespace Radial.Core.Tests.Geometry;

public class RadialGeometryTests
{
    private static readonly PointD Origin = new(500, 500);

    [Fact]
    public void Directions_AreNumberedClockwiseFromNorth()
    {
        var expected = new[]
        {
            RadialDirection.North, RadialDirection.NorthEast, RadialDirection.East, RadialDirection.SouthEast,
            RadialDirection.South, RadialDirection.SouthWest, RadialDirection.West, RadialDirection.NorthWest,
        };

        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(index, (int)expected[index]);
        }
    }

    // Vectores exactos sobre los ejes y las diagonales: ejercitan los signos de atan2 (incluidos -0.0).
    [Theory]
    [InlineData(0, -1, RadialDirection.North)]
    [InlineData(1, -1, RadialDirection.NorthEast)]
    [InlineData(1, 0, RadialDirection.East)]
    [InlineData(1, 1, RadialDirection.SouthEast)]
    [InlineData(0, 1, RadialDirection.South)]
    [InlineData(-1, 1, RadialDirection.SouthWest)]
    [InlineData(-1, 0, RadialDirection.West)]
    [InlineData(-1, -1, RadialDirection.NorthWest)]
    public void Select_ResolvesAllEightDirections(double dx, double dy, RadialDirection expected)
    {
        var pointer = new PointD(Origin.X + dx * 100, Origin.Y + dy * 100);

        Assert.Equal(expected, RadialGeometry.Select(Origin, pointer, deadZoneRadius: 30));
    }

    [Theory]
    [InlineData(RadialDirection.North, 0.0)]
    [InlineData(RadialDirection.NorthEast, 45.0)]
    [InlineData(RadialDirection.East, 90.0)]
    [InlineData(RadialDirection.SouthEast, 135.0)]
    [InlineData(RadialDirection.South, 180.0)]
    [InlineData(RadialDirection.SouthWest, 225.0)]
    [InlineData(RadialDirection.West, 270.0)]
    [InlineData(RadialDirection.NorthWest, 315.0)]
    public void CenterAngle_IsClockwiseFromNorth(RadialDirection direction, double expected)
    {
        Assert.Equal(expected, RadialGeometry.CenterAngle(direction));
        Assert.Equal(direction, RadialGeometry.DirectionFromAngle(expected));
    }

    // Límite k = 22.5° + 45°·k. Justo antes pertenece al sector k; justo después, al siguiente.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Select_SplitsSectorsAtEachBoundary(int k)
    {
        var boundary = 22.5 + 45.0 * k;

        var before = RadialGeometry.Select(Origin, Pointers.AtAngle(Origin, boundary - 0.01, 100), 30);
        var after = RadialGeometry.Select(Origin, Pointers.AtAngle(Origin, boundary + 0.01, 100), 30);

        Assert.Equal((RadialDirection)k, before);
        Assert.Equal((RadialDirection)((k + 1) % 8), after);
    }

    // Convención de los límites: [inicio, fin). Las sumas con 22.5 son exactas en coma flotante.
    [Theory]
    [InlineData(22.5, RadialDirection.NorthEast)]
    [InlineData(67.5, RadialDirection.East)]
    [InlineData(112.5, RadialDirection.SouthEast)]
    [InlineData(157.5, RadialDirection.South)]
    [InlineData(202.5, RadialDirection.SouthWest)]
    [InlineData(247.5, RadialDirection.West)]
    [InlineData(292.5, RadialDirection.NorthWest)]
    [InlineData(337.5, RadialDirection.North)]
    public void DirectionFromAngle_ExactBoundaryBelongsToNextClockwiseSector(double boundary, RadialDirection expected)
    {
        Assert.Equal(expected, RadialGeometry.DirectionFromAngle(boundary));
    }

    [Theory]
    [InlineData(-90.0, RadialDirection.West)]
    [InlineData(-45.0, RadialDirection.NorthWest)]
    [InlineData(-135.0, RadialDirection.SouthWest)]
    [InlineData(-180.0, RadialDirection.South)]
    [InlineData(-315.0, RadialDirection.NorthEast)]
    [InlineData(-0.001, RadialDirection.North)]
    [InlineData(-22.6, RadialDirection.NorthWest)]
    [InlineData(-22.4, RadialDirection.North)]
    public void DirectionFromAngle_HandlesNegativeAngles(double degrees, RadialDirection expected)
    {
        Assert.Equal(expected, RadialGeometry.DirectionFromAngle(degrees));
    }

    [Theory]
    [InlineData(360.0, RadialDirection.North)]
    [InlineData(720.0, RadialDirection.North)]
    [InlineData(-360.0, RadialDirection.North)]
    [InlineData(-720.0, RadialDirection.North)]
    [InlineData(405.0, RadialDirection.NorthEast)]
    [InlineData(450.0, RadialDirection.East)]
    [InlineData(3600.0 + 180.0, RadialDirection.South)]
    [InlineData(-3600.0 - 90.0, RadialDirection.West)]
    public void DirectionFromAngle_TreatsMultiplesOf360AsEquivalent(double degrees, RadialDirection expected)
    {
        Assert.Equal(expected, RadialGeometry.DirectionFromAngle(degrees));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(90.0, 90.0)]
    [InlineData(359.9, 359.9)]
    [InlineData(360.0, 0.0)]
    [InlineData(720.0, 0.0)]
    [InlineData(450.0, 90.0)]
    [InlineData(-1.0, 359.0)]
    [InlineData(-90.0, 270.0)]
    [InlineData(-360.0, 0.0)]
    [InlineData(-450.0, 270.0)]
    public void NormalizeDegrees_ReturnsValueInZeroToThreeSixty(double input, double expected)
    {
        Assert.Equal(expected, RadialGeometry.NormalizeDegrees(input), precision: 9);
    }

    [Fact]
    public void NormalizeDegrees_NegativeZeroIsNorth()
    {
        Assert.Equal(0.0, RadialGeometry.NormalizeDegrees(-0.0));
        Assert.Equal(RadialDirection.North, RadialGeometry.DirectionFromAngle(-0.0));
    }

    [Theory]
    [InlineData(-1e-20)]
    [InlineData(-1e-300)]
    [InlineData(-double.Epsilon)]
    public void NormalizeDegrees_TinyNegativeNeverReturnsThreeSixty(double tiny)
    {
        // -1e-20 + 360 redondea a 360.0; el resultado debe seguir dentro de [0, 360).
        var normalized = RadialGeometry.NormalizeDegrees(tiny);

        Assert.InRange(normalized, 0.0, 359.9999999);
        Assert.Equal(RadialDirection.North, RadialGeometry.DirectionFromAngle(tiny));
    }

    [Theory]
    [InlineData(1e15)]
    [InlineData(-1e15)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void NormalizeDegrees_StaysInRangeForHugeAngles(double huge)
    {
        var normalized = RadialGeometry.NormalizeDegrees(huge);

        Assert.True(normalized >= 0.0 && normalized < 360.0);
    }

    [Theory]
    [InlineData(10.0, 350.0, 20.0)]
    [InlineData(350.0, 10.0, 20.0)]
    [InlineData(0.0, 180.0, 180.0)]
    [InlineData(-10.0, 10.0, 20.0)]
    [InlineData(90.0, 90.0, 0.0)]
    [InlineData(720.0, 0.0, 0.0)]
    public void AngleDifference_IsShortestDistanceAroundTheCircle(double first, double second, double expected)
    {
        Assert.Equal(expected, RadialGeometry.AngleDifference(first, second), precision: 9);
    }

    [Fact]
    public void Select_ReturnsNull_ForNullVector_EvenWithZeroRadius()
    {
        Assert.Null(RadialGeometry.Select(Origin, Origin, deadZoneRadius: 30));
        Assert.Null(RadialGeometry.Select(Origin, Origin, deadZoneRadius: 0));
    }

    [Fact]
    public void Select_DeadZoneBoundary_StrictlyInsideIsNoSelection_AtRadiusIsSelected()
    {
        var justInside = new PointD(Origin.X, Origin.Y - 29.999);
        var exactlyAtRadius = new PointD(Origin.X, Origin.Y - 30.0);
        var justOutside = new PointD(Origin.X, Origin.Y - 30.001);

        Assert.Null(RadialGeometry.Select(Origin, justInside, 30));
        Assert.Equal(RadialDirection.North, RadialGeometry.Select(Origin, exactlyAtRadius, 30));
        Assert.Equal(RadialDirection.North, RadialGeometry.Select(Origin, justOutside, 30));
    }

    [Fact]
    public void Select_ZeroRadius_SelectsAnyNonZeroVector()
    {
        Assert.Equal(RadialDirection.East, RadialGeometry.Select(Origin, new PointD(Origin.X + 0.001, Origin.Y), 0));
    }

    [Fact]
    public void Select_DeadZoneAppliesInEveryDirection()
    {
        foreach (var direction in Enum.GetValues<RadialDirection>())
        {
            Assert.Null(RadialGeometry.Select(Origin, Pointers.At(Origin, direction, 29), 30));
            Assert.Equal(direction, RadialGeometry.Select(Origin, Pointers.At(Origin, direction, 31), 30));
        }
    }

    [Fact]
    public void Select_DoesNotOverflowWithHugeDistances()
    {
        Assert.Equal(RadialDirection.East, RadialGeometry.Select(new PointD(0, 0), new PointD(1e300, 0), 30));
        Assert.Equal(RadialDirection.West, RadialGeometry.Select(new PointD(-1e308, 0), new PointD(-double.MaxValue, 0), 30));
    }

    [Fact]
    public void Select_AgreesWithReferenceTable_ForEveryTenthOfADegree()
    {
        // Referencia independiente de la fórmula: umbrales escritos a mano. Se omiten los 8 límites exactos,
        // que tienen su propia prueba, porque ahí el cálculo con senos puede caer en cualquiera de los lados.
        double[] upperBounds = [22.5, 67.5, 112.5, 157.5, 202.5, 247.5, 292.5, 337.5];
        for (var tenth = 0; tenth < 3600; tenth++)
        {
            var angle = tenth / 10.0;
            if (upperBounds.Contains(angle))
            {
                continue;
            }

            // Cuántos límites ya se han rebasado; tras el último (337.5°) se vuelve al norte.
            var expected = angle >= 337.5 ? RadialDirection.North : (RadialDirection)upperBounds.Count(b => b <= angle);

            Assert.Equal(expected, RadialGeometry.DirectionFromAngle(angle));
            Assert.Equal(expected, RadialGeometry.Select(Origin, Pointers.AtAngle(Origin, angle, 100), 30));
        }
    }

    [Fact]
    public void NonFiniteAngle_IsRejected()
    {
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RadialGeometry.NormalizeDegrees(invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => RadialGeometry.DirectionFromAngle(invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => RadialGeometry.AngleDifference(invalid, 0));
        }
    }

    [Fact]
    public void NonFinitePointsOrRadius_AreRejected()
    {
        var good = new PointD(1, 1);
        var bad = new[] { new PointD(double.NaN, 0), new PointD(0, double.PositiveInfinity), new PointD(double.NegativeInfinity, 1) };

        foreach (var point in bad)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RadialGeometry.Select(point, good, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => RadialGeometry.Select(good, point, 30));
        }

        foreach (var radius in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RadialGeometry.Select(Origin, good, radius));
        }
    }

    [Fact]
    public void CenterAngle_RejectsUndefinedDirection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialGeometry.CenterAngle((RadialDirection)8));
    }
}
