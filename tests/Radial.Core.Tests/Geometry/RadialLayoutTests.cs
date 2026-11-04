using Radial.Core.Geometry;

namespace Radial.Core.Tests.Geometry;

public class RadialLayoutTests
{
    private static readonly PointD Center = new(200, 200);
    private const double Tolerance = 1e-9;

    public static TheoryData<RadialDirection> AllDirections => new(Enum.GetValues<RadialDirection>());

    private static void AssertPoint(double x, double y, PointD actual)
    {
        Assert.Equal(x, actual.X, Tolerance);
        Assert.Equal(y, actual.Y, Tolerance);
    }

    // PointAt: norte = -Y, horario

    [Theory]
    [InlineData(0, 200, 100)]
    [InlineData(90, 300, 200)]
    [InlineData(180, 200, 300)]
    [InlineData(270, 100, 200)]
    [InlineData(-90, 100, 200)]
    [InlineData(360, 200, 100)]
    public void PointAt_PutsTheCardinalPointsWhereTheyBelong(double angle, double x, double y) =>
        AssertPoint(x, y, RadialLayout.PointAt(Center, 100, angle));

    [Fact]
    public void PointAt_WithRadiusZero_IsTheCenter() => AssertPoint(200, 200, RadialLayout.PointAt(Center, 0, 123));

    [Fact]
    public void PointAt_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialLayout.PointAt(Center, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialLayout.PointAt(Center, double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialLayout.PointAt(Center, 10, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialLayout.PointAt(new PointD(double.NaN, 0), 10, 0));
    }

    // Coherencia con la selección: lo dibujado en una dirección es lo que el motor selecciona

    [Theory]
    [MemberData(nameof(AllDirections))]
    public void TheLabelAnchor_IsSelectedAsItsOwnDirection(RadialDirection direction)
    {
        var anchor = RadialLayout.LabelAnchor(Center, direction, 120);

        Assert.Equal(direction, RadialGeometry.Select(Center, anchor, deadZoneRadius: 30));
    }

    [Theory]
    [MemberData(nameof(AllDirections))]
    public void TheCornersOfASector_AreSelectedAsThatSector(RadialDirection direction)
    {
        const double gap = 4;
        var shape = RadialLayout.CreateSector(Center, direction, 60, 180, gap);

        // Las esquinas quedan dentro del sector (el hueco las aleja de los límites con los vecinos).
        foreach (var corner in new[] { shape.OuterStart, shape.OuterEnd, shape.InnerEnd, shape.InnerStart })
        {
            Assert.Equal(direction, RadialGeometry.Select(Center, corner, deadZoneRadius: 30));
        }
    }

    // CreateSector

    [Fact]
    public void ASectorWithoutGap_SpansExactly45Degrees_CenteredOnItsDirection()
    {
        var shape = RadialLayout.CreateSector(Center, RadialDirection.East, 60, 180, 0);

        Assert.Equal(45, shape.SweepAngle, Tolerance);
        Assert.Equal(67.5, shape.StartAngle, Tolerance);
        Assert.Equal(60, shape.InnerRadius);
        Assert.Equal(180, shape.OuterRadius);
    }

    [Fact]
    public void TheGap_IsSplitEvenlyBetweenBothSides()
    {
        var shape = RadialLayout.CreateSector(Center, RadialDirection.East, 60, 180, 6);

        Assert.Equal(39, shape.SweepAngle, Tolerance);
        Assert.Equal(70.5, shape.StartAngle, Tolerance); // 90 - 19.5
    }

    [Fact]
    public void TheNorthSector_StartsBeforeNorth_AndItsAngleIsNormalized()
    {
        var shape = RadialLayout.CreateSector(Center, RadialDirection.North, 60, 180, 0);

        Assert.Equal(337.5, shape.StartAngle, Tolerance);
        Assert.Equal(45, shape.SweepAngle, Tolerance);
    }

    [Theory]
    [MemberData(nameof(AllDirections))]
    public void ThePointsOfASector_LieOnTheirRadii(RadialDirection direction)
    {
        var shape = RadialLayout.CreateSector(Center, direction, 60, 180, 5);

        Assert.Equal(180, Distance(shape.OuterStart), 1e-9);
        Assert.Equal(180, Distance(shape.OuterEnd), 1e-9);
        Assert.Equal(60, Distance(shape.InnerStart), 1e-9);
        Assert.Equal(60, Distance(shape.InnerEnd), 1e-9);
    }

    [Fact]
    public void TheEastSector_HasItsEndPointsAboveAndBelowTheHorizontalAxis()
    {
        var shape = RadialLayout.CreateSector(Center, RadialDirection.East, 60, 180, 0);

        // Este: el borde de salida (en sentido horario) queda al norte del eje y el de llegada al sur.
        Assert.True(shape.OuterStart.Y < Center.Y);
        Assert.True(shape.OuterEnd.Y > Center.Y);
        Assert.Equal(shape.OuterStart.X, shape.OuterEnd.X, 1e-9);
        Assert.True(shape.OuterStart.X > Center.X);
    }

    [Fact]
    public void TheEightSectors_TileTheRing_WithoutOverlappingWhenThereIsAGap()
    {
        var shapes = Enum.GetValues<RadialDirection>().Select(d => RadialLayout.CreateSector(Center, d, 60, 180, 4)).ToList();

        var totalSweep = shapes.Sum(s => s.SweepAngle);
        Assert.Equal(8 * 41, totalSweep, Tolerance);

        for (var i = 0; i < shapes.Count; i++)
        {
            var next = shapes[(i + 1) % shapes.Count];
            var end = shapes[i].StartAngle + shapes[i].SweepAngle;
            var gap = RadialGeometry.NormalizeDegrees(next.StartAngle - end);
            Assert.Equal(4, gap, 1e-9);
        }
    }

    [Fact]
    public void CreateSector_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialLayout.CreateSector(Center, RadialDirection.North, -1, 100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialLayout.CreateSector(Center, RadialDirection.North, 100, 100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialLayout.CreateSector(Center, RadialDirection.North, 100, 50, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialLayout.CreateSector(Center, RadialDirection.North, 10, 50, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialLayout.CreateSector(Center, RadialDirection.North, 10, 50, 45));
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialLayout.CreateSector(Center, RadialDirection.North, 10, double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialLayout.CreateSector(Center, (RadialDirection)99, 10, 50, 0));
    }

    private static double Distance(PointD point) => double.Hypot(point.X - Center.X, point.Y - Center.Y);
}
