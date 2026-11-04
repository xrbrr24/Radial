namespace Radial.Core.Geometry;

/// <summary>
/// Un sector anular (un «trozo de rosca») del radial: el arco exterior de <see cref="OuterStart"/> a <see cref="OuterEnd"/> y el
/// interior de <see cref="InnerEnd"/> a <see cref="InnerStart"/>, ambos en sentido horario al recorrer el exterior. Las
/// coordenadas siguen la convención de <see cref="PointD"/> (Y crece hacia abajo). Solo describe la forma; dibujarla es de la interfaz.
/// </summary>
/// <param name="StartAngle">Ángulo del borde de salida, en grados desde el norte en sentido horario.</param>
/// <param name="SweepAngle">Apertura angular del sector, en grados (positiva).</param>
public readonly record struct SectorShape(
    PointD OuterStart,
    PointD OuterEnd,
    PointD InnerEnd,
    PointD InnerStart,
    double InnerRadius,
    double OuterRadius,
    double StartAngle,
    double SweepAngle);

/// <summary>
/// Disposición del radial: dónde caen los ocho sectores y sus etiquetas. Funciones puras y sin estado. Es la contraparte
/// de dibujo de <see cref="RadialGeometry"/>: usa los mismos ángulos (norte = 0°, sentido horario), así que lo que se dibuja en
/// una dirección es exactamente lo que <see cref="RadialGeometry.Select"/> devuelve para un cursor sobre ese dibujo.
/// </summary>
public static class RadialLayout
{
    /// <summary>Punto a <paramref name="radius"/> del centro en la dirección <paramref name="angleDegrees"/> (norte = 0°, horario).</summary>
    public static PointD PointAt(PointD center, double radius, double angleDegrees)
    {
        if (!center.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(center), center, "El centro debe tener coordenadas finitas.");
        }

        if (!double.IsFinite(radius) || radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "El radio debe ser finito y no negativo.");
        }

        var radians = RadialGeometry.NormalizeDegrees(angleDegrees) * Math.PI / 180.0;

        // El norte es -Y: con 0° el punto queda justo encima del centro.
        return new PointD(center.X + radius * Math.Sin(radians), center.Y - radius * Math.Cos(radians));
    }

    /// <summary>Punto central de un sector a <paramref name="radius"/> del centro; ahí se coloca su etiqueta.</summary>
    public static PointD LabelAnchor(PointD center, RadialDirection direction, double radius) =>
        PointAt(center, radius, RadialGeometry.CenterAngle(direction));

    /// <summary>
    /// Forma del sector de <paramref name="direction"/> entre dos radios. <paramref name="gapDegrees"/> es la separación angular
    /// total con cada vecino: se quita la mitad a cada lado, así que el sector abarca 45° menos el hueco, centrado en su dirección.
    /// </summary>
    public static SectorShape CreateSector(PointD center, RadialDirection direction, double innerRadius, double outerRadius, double gapDegrees)
    {
        if (!double.IsFinite(innerRadius) || innerRadius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(innerRadius), innerRadius, "El radio interior debe ser finito y no negativo.");
        }

        if (!double.IsFinite(outerRadius) || outerRadius <= innerRadius)
        {
            throw new ArgumentOutOfRangeException(nameof(outerRadius), outerRadius, "El radio exterior debe ser finito y mayor que el interior.");
        }

        if (!double.IsFinite(gapDegrees) || gapDegrees < 0 || gapDegrees >= RadialGeometry.SectorSizeDegrees)
        {
            throw new ArgumentOutOfRangeException(nameof(gapDegrees), gapDegrees, $"El hueco debe estar en [0, {RadialGeometry.SectorSizeDegrees}) grados.");
        }

        var sweep = RadialGeometry.SectorSizeDegrees - gapDegrees;
        var start = RadialGeometry.NormalizeDegrees(RadialGeometry.CenterAngle(direction) - sweep / 2);
        var end = start + sweep;

        return new SectorShape(
            OuterStart: PointAt(center, outerRadius, start),
            OuterEnd: PointAt(center, outerRadius, end),
            InnerEnd: PointAt(center, innerRadius, end),
            InnerStart: PointAt(center, innerRadius, start),
            InnerRadius: innerRadius,
            OuterRadius: outerRadius,
            StartAngle: start,
            SweepAngle: sweep);
    }
}
