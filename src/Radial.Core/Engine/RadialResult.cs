using Radial.Core.Model;

namespace Radial.Core.Engine;

public enum RadialResultKind
{
    /// <summary>El motor estaba inactivo; la operación se ignoró.</summary>
    NotActive,

    /// <summary>La interacción terminó con un elemento ejecutable. El llamante decide cómo ejecutarlo.</summary>
    Execute,

    /// <summary>La interacción terminó sin ejecutar nada.</summary>
    Cancelled,
}

public enum CancelReason
{
    None,

    /// <summary>El cursor estaba en la zona central.</summary>
    NoSelection,

    /// <summary>El cursor estaba sobre una posición sin elemento.</summary>
    EmptySlot,

    /// <summary>Se soltó sobre un grupo antes de completar el tiempo de entrada.</summary>
    GroupNotEntered,
}

/// <summary>Resultado de <see cref="RadialEngine.Confirm"/>. El motor nunca ejecuta acciones: solo las indica.</summary>
public sealed record RadialResult(RadialResultKind Kind, RadialItem? Item = null, CancelReason Reason = CancelReason.None)
{
    public static RadialResult Inactive { get; } = new(RadialResultKind.NotActive);

    public static RadialResult Executed(RadialItem item) => new(RadialResultKind.Execute, item);

    public static RadialResult Cancelled(CancelReason reason) => new(RadialResultKind.Cancelled, Reason: reason);
}
