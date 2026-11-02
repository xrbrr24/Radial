namespace Radial.Core.Geometry;

/// <summary>
/// Estabiliza la selección de sector con histéresis angular. Mantiene el sector actual mientras el cursor
/// no se aleje de su ángulo central más de media apertura (22.5°) más el margen configurado.
/// Al entrar desde la zona muerta no hay histéresis: se toma el sector bajo el cursor.
/// </summary>
public sealed class SectorSelector
{
    /// <summary>
    /// Límite exclusivo del margen. Con un margen de 22.5° o más, el sector vecino solo se alcanzaría
    /// pasando de su propio centro; por debajo de este valor siempre se puede cambiar de sector.
    /// </summary>
    public const double MaxHysteresisDegrees = RadialGeometry.HalfSectorDegrees;

    public SectorSelector(double hysteresisDegrees)
    {
        if (!double.IsFinite(hysteresisDegrees) || hysteresisDegrees < 0 || hysteresisDegrees >= MaxHysteresisDegrees)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hysteresisDegrees),
                hysteresisDegrees,
                $"La histéresis debe estar en [0, {MaxHysteresisDegrees}) grados.");
        }

        HysteresisDegrees = hysteresisDegrees;
    }

    public double HysteresisDegrees { get; }

    /// <summary>Sector seleccionado tras la última actualización; <c>null</c> en la zona muerta.</summary>
    public RadialDirection? Current { get; private set; }

    public RadialDirection? Update(PointD origin, PointD pointer, double deadZoneRadius)
    {
        if (!RadialGeometry.TryGetAngle(origin, pointer, deadZoneRadius, out var angle))
        {
            Current = null;
            return null;
        }

        if (Current is { } current &&
            RadialGeometry.AngleDifference(angle, RadialGeometry.CenterAngle(current)) < RadialGeometry.HalfSectorDegrees + HysteresisDegrees)
        {
            return current;
        }

        Current = RadialGeometry.DirectionFromAngle(angle);
        return Current;
    }

    public void Reset() => Current = null;
}
