using Radial.Core.Geometry;
using Radial.Core.Tests.Support;

namespace Radial.Core.Tests.Geometry;

public class SectorSelectorTests
{
    private static readonly PointD Origin = new(500, 500);
    private const double DeadZone = 30;

    private static RadialDirection? Feed(SectorSelector selector, double angle, double distance = 100) =>
        selector.Update(Origin, Pointers.AtAngle(Origin, angle, distance), DeadZone);

    private static int CountChanges(SectorSelector selector, IEnumerable<double> angles)
    {
        var changes = 0;
        RadialDirection? previous = null;
        foreach (var angle in angles)
        {
            var current = Feed(selector, angle);
            if (current != previous)
            {
                changes++;
            }

            previous = current;
        }

        return changes;
    }

    [Fact]
    public void Update_NormalChangeBetweenSectors_FollowsThePointer()
    {
        var selector = new SectorSelector(5);

        Assert.Equal(RadialDirection.North, Feed(selector, 0));
        Assert.Equal(RadialDirection.East, Feed(selector, 90));
        Assert.Equal(RadialDirection.South, Feed(selector, 180));
        Assert.Equal(RadialDirection.NorthWest, Feed(selector, 315));
    }

    [Fact]
    public void Update_KeepsCurrentSector_InsideTheHysteresisMargin()
    {
        var selector = new SectorSelector(5);
        Feed(selector, 10);

        // 24° ya es del sector noreste, pero dista 24° del centro norte: < 22.5 + 5.
        Assert.Equal(RadialDirection.North, Feed(selector, 24));
        Assert.Equal(RadialDirection.North, Feed(selector, 27.4));
    }

    [Fact]
    public void Update_SwitchesToAdjacentSector_OnceMarginIsExceeded()
    {
        var selector = new SectorSelector(5);
        Feed(selector, 10);

        Assert.Equal(RadialDirection.NorthEast, Feed(selector, 27.6));
    }

    [Fact]
    public void Update_Hysteresis_AppliesInBothDirectionsAfterSwitching()
    {
        var selector = new SectorSelector(5);
        Feed(selector, 10);
        Feed(selector, 30);

        // Volver a 20° es del sector norte, pero dista 25° del centro noreste (< 27.5).
        Assert.Equal(RadialDirection.NorthEast, Feed(selector, 20));
        Assert.Equal(RadialDirection.North, Feed(selector, 17));
    }

    [Fact]
    public void Update_Oscillation_NearBoundary_DoesNotFlicker()
    {
        var withHysteresis = new SectorSelector(5);
        var withoutHysteresis = new SectorSelector(0);
        var oscillation = Enumerable.Range(0, 40).Select(i => i % 2 == 0 ? 20.0 : 25.0).ToList();

        var stableChanges = CountChanges(withHysteresis, oscillation);
        var flickeringChanges = CountChanges(withoutHysteresis, oscillation);

        Assert.Equal(1, stableChanges);
        Assert.True(flickeringChanges > 20, "Sin histéresis la selección debe cambiar en cada oscilación.");
    }

    [Fact]
    public void Update_NoisyJitterAroundBoundary_StaysOnTheSameSector()
    {
        var selector = new SectorSelector(3);
        var jitter = new[] { 21.0, 23.5, 22.0, 24.9, 21.5, 24.0, 22.4, 25.0 };

        Assert.Equal(1, CountChanges(selector, jitter));
    }

    [Fact]
    public void Update_AfterCrossingMargin_StaysOnNewSectorDespiteJitterBack()
    {
        var selector = new SectorSelector(5);
        Feed(selector, 10);
        Feed(selector, 30);

        foreach (var angle in new[] { 26.0, 29.0, 25.0, 30.0 })
        {
            Assert.Equal(RadialDirection.NorthEast, Feed(selector, angle));
        }
    }

    [Fact]
    public void Update_Hysteresis_WorksAcrossZeroDegrees()
    {
        var selector = new SectorSelector(5);
        Assert.Equal(RadialDirection.NorthWest, Feed(selector, 330));

        Assert.Equal(RadialDirection.NorthWest, Feed(selector, 340)); // 25° del centro 315
        Assert.Equal(RadialDirection.North, Feed(selector, 345));     // 30° > 27.5
        Assert.Equal(RadialDirection.North, Feed(selector, 335));     // 25° del centro 0 (360)
    }

    [Fact]
    public void Update_EnteringFromDeadZone_UsesRawSectorWithoutHysteresis()
    {
        var selector = new SectorSelector(5);
        Feed(selector, 10);
        Assert.Null(Feed(selector, 10, distance: 10));
        Assert.Null(selector.Current);

        // Sin sector previo, 24° ya es noreste aunque antes estuviera en norte.
        Assert.Equal(RadialDirection.NorthEast, Feed(selector, 24));
    }

    [Fact]
    public void Update_ZeroHysteresis_MatchesStatelessGeometry()
    {
        var selector = new SectorSelector(0);
        for (var angle = 0.0; angle < 360.0; angle += 3.7)
        {
            var point = Pointers.AtAngle(Origin, angle, 100);

            Assert.Equal(RadialGeometry.Select(Origin, point, DeadZone), selector.Update(Origin, point, DeadZone));
        }
    }

    [Fact]
    public void Update_ExtremeHysteresis_StillAllowsReachingTheNeighbourSector()
    {
        var selector = new SectorSelector(22.4);
        Feed(selector, 0);

        Assert.Equal(RadialDirection.North, Feed(selector, 44.8));
        Assert.Equal(RadialDirection.NorthEast, Feed(selector, 45));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(10.0)]
    [InlineData(22.49)]
    public void Update_EverySectorIsReachableFromAnotherAtItsCenter(double hysteresis)
    {
        var selector = new SectorSelector(hysteresis);
        foreach (var from in Enum.GetValues<RadialDirection>())
        {
            foreach (var to in Enum.GetValues<RadialDirection>())
            {
                selector.Reset();
                Feed(selector, RadialGeometry.CenterAngle(from));

                Assert.Equal(to, Feed(selector, RadialGeometry.CenterAngle(to)));
            }
        }
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(22.5)]
    [InlineData(30.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Constructor_RejectsMarginsThatWouldMakeSwitchingImpossibleOrMeaningless(double hysteresis)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SectorSelector(hysteresis));
    }

    [Fact]
    public void Reset_ClearsCurrentSelection()
    {
        var selector = new SectorSelector(5);
        Feed(selector, 90);

        selector.Reset();

        Assert.Null(selector.Current);
    }
}
