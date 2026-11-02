namespace Radial.Core.Model;

/// <summary>Parámetros geométricos. Las distancias están en las mismas unidades que las posiciones del cursor.</summary>
public sealed record GeometrySettings
{
    /// <summary>Radio de la zona central. Dentro de ella no hay selección y se activa la navegación hacia atrás.</summary>
    public double DeadZoneRadius { get; init; } = 30;

    /// <summary>Margen angular de histéresis (sensibilidad). 0 desactiva la estabilización.</summary>
    public double HysteresisDegrees { get; init; } = 5;
}

public sealed record NavigationSettings
{
    /// <summary>Permanencia sobre un grupo para entrar en su submenú.</summary>
    public TimeSpan SubmenuDwell { get; init; } = TimeSpan.FromMilliseconds(600);

    /// <summary>Permanencia en la zona central para volver al menú anterior.</summary>
    public TimeSpan BackDwell { get; init; } = TimeSpan.FromMilliseconds(800);

    /// <summary>Máximo de menús apilados, contando el raíz: 3 permite raíz, submenú y sub-submenú.</summary>
    public int MaxDepth { get; init; } = 3;
}
