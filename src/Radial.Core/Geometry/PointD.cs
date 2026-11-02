namespace Radial.Core.Geometry;

/// <summary>
/// Punto en un plano donde X crece hacia la derecha e Y crece hacia abajo.
/// Las unidades las decide quien llama; el núcleo solo exige coherencia entre origen, cursor y zona muerta.
/// </summary>
public readonly record struct PointD(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}
