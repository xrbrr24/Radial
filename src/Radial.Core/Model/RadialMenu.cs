using Radial.Core.Geometry;

namespace Radial.Core.Model;

/// <summary>Menú de hasta ocho elementos, uno por posición. Las posiciones sin elemento quedan vacías.</summary>
public sealed record RadialMenu(MenuId Id, string Name, IReadOnlyList<RadialItem> Items)
{
    /// <summary>Elemento en una posición, o <c>null</c> si está vacía.</summary>
    public RadialItem? GetItem(RadialDirection position)
    {
        foreach (var item in Items)
        {
            if (item.Position == position)
            {
                return item;
            }
        }

        return null;
    }
}
