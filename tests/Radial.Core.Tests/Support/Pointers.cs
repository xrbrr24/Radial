using Radial.Core.Geometry;

namespace Radial.Core.Tests.Support;

internal static class Pointers
{
    /// <summary>Punto a <paramref name="distance"/> del origen en el ángulo dado (0° = norte, sentido horario, Y hacia abajo).</summary>
    public static PointD AtAngle(PointD origin, double degrees, double distance)
    {
        var radians = degrees * Math.PI / 180.0;
        return new PointD(origin.X + distance * Math.Sin(radians), origin.Y - distance * Math.Cos(radians));
    }

    /// <summary>Punto en el centro del sector <paramref name="direction"/>.</summary>
    public static PointD At(PointD origin, RadialDirection direction, double distance) =>
        AtAngle(origin, RadialGeometry.CenterAngle(direction), distance);
}
