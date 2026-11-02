namespace Radial.Core.Geometry;

/// <summary>
/// Geometría básica y sin estado del radial. El norte es 0° y el ángulo crece en sentido horario.
/// Cada sector abarca 45° centrados en su dirección y los límites son intervalos <c>[inicio, fin)</c>:
/// un ángulo exactamente en un límite pertenece al sector siguiente en sentido horario.
/// </summary>
public static class RadialGeometry
{
    public const int SectorCount = 8;
    public const double SectorSizeDegrees = 45.0;
    public const double HalfSectorDegrees = SectorSizeDegrees / 2;

    /// <summary>Normaliza un ángulo al intervalo [0, 360).</summary>
    public static double NormalizeDegrees(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "El ángulo debe ser un número finito.");
        }

        // En C#, % conserva el signo del dividendo: -90 % 360 == -90. Se corrige a mano.
        var normalized = degrees % 360.0;
        if (normalized < 0)
        {
            normalized += 360.0;
        }

        // Un negativo diminuto (p. ej. -1e-20) redondea a 360.0 al sumar 360.
        return normalized >= 360.0 ? 0.0 : normalized;
    }

    /// <summary>Devuelve el sector que contiene el ángulo dado (cualquier valor finito).</summary>
    public static RadialDirection DirectionFromAngle(double degrees)
    {
        var normalized = NormalizeDegrees(degrees);

        // normalized está en [0, 360), así que el cociente es 0..8 y % no ve negativos.
        var sector = (int)Math.Floor((normalized + HalfSectorDegrees) / SectorSizeDegrees);
        return (RadialDirection)(sector % SectorCount);
    }

    /// <summary>Ángulo central de un sector, en [0, 360).</summary>
    public static double CenterAngle(RadialDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Dirección desconocida.");
        }

        return (int)direction * SectorSizeDegrees;
    }

    /// <summary>Diferencia angular más corta entre dos ángulos, en [0, 180].</summary>
    public static double AngleDifference(double firstDegrees, double secondDegrees)
    {
        var difference = Math.Abs(NormalizeDegrees(firstDegrees) - NormalizeDegrees(secondDegrees));
        return difference > 180.0 ? 360.0 - difference : difference;
    }

    /// <summary>
    /// Calcula el ángulo del cursor respecto al origen. Devuelve <c>false</c> si el cursor está en la zona muerta
    /// (distancia estrictamente menor que el radio) o coincide con el origen, aunque el radio sea 0.
    /// Una distancia igual al radio ya está fuera de la zona muerta.
    /// </summary>
    public static bool TryGetAngle(PointD origin, PointD pointer, double deadZoneRadius, out double angleDegrees)
    {
        ValidateInputs(origin, pointer, deadZoneRadius);
        angleDegrees = 0;

        var dx = pointer.X - origin.X;
        var dy = pointer.Y - origin.Y;
        var distance = double.Hypot(dx, dy);
        if (distance == 0 || distance < deadZoneRadius)
        {
            return false;
        }

        // Y crece hacia abajo: el norte es -Y. atan2(dx, -dy) vale 0 al norte y crece en sentido horario.
        angleDegrees = NormalizeDegrees(Math.Atan2(dx, -dy) * 180.0 / Math.PI);
        return true;
    }

    /// <summary>Sector bajo el cursor, o <c>null</c> si está en la zona muerta.</summary>
    public static RadialDirection? Select(PointD origin, PointD pointer, double deadZoneRadius) =>
        TryGetAngle(origin, pointer, deadZoneRadius, out var angle) ? DirectionFromAngle(angle) : null;

    internal static void ValidateInputs(PointD origin, PointD pointer, double deadZoneRadius)
    {
        if (!origin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "El origen debe tener coordenadas finitas.");
        }

        if (!pointer.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(pointer), pointer, "El cursor debe tener coordenadas finitas.");
        }

        if (!double.IsFinite(deadZoneRadius) || deadZoneRadius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deadZoneRadius), deadZoneRadius, "El radio de la zona muerta debe ser finito y no negativo.");
        }
    }
}
