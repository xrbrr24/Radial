namespace Radial.Core.Geometry;

/// <summary>Nombres de las ocho direcciones para mostrarlas al usuario.</summary>
public static class DirectionNames
{
    public static string Spanish(RadialDirection direction) => direction switch
    {
        RadialDirection.North => "norte",
        RadialDirection.NorthEast => "noreste",
        RadialDirection.East => "este",
        RadialDirection.SouthEast => "sureste",
        RadialDirection.South => "sur",
        RadialDirection.SouthWest => "suroeste",
        RadialDirection.West => "oeste",
        RadialDirection.NorthWest => "noroeste",
        _ => direction.ToString(),
    };
}
