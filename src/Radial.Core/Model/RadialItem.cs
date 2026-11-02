using Radial.Core.Geometry;

namespace Radial.Core.Model;

public sealed record RadialItem(ItemId Id, string Name, RadialDirection Position, RadialAction Action, IconReference? Icon = null)
{
    /// <summary>Un grupo abre otro menú en lugar de producir un resultado ejecutable.</summary>
    public bool IsGroup => Action is OpenMenuAction;
}
