using System.Diagnostics.CodeAnalysis;

namespace Radial.Core.Model;

public sealed record RadialConfiguration
{
    public required MenuId RootMenuId { get; init; }

    public IReadOnlyList<RadialMenu> Menus { get; init; } = [];

    public GeometrySettings Geometry { get; init; } = new();

    public NavigationSettings Navigation { get; init; } = new();

    public bool TryGetMenu(MenuId id, [NotNullWhen(true)] out RadialMenu? menu)
    {
        foreach (var candidate in Menus)
        {
            if (candidate.Id == id)
            {
                menu = candidate;
                return true;
            }
        }

        menu = null;
        return false;
    }

    /// <summary>Configuración mínima válida: un único menú raíz vacío y valores predeterminados.</summary>
    public static RadialConfiguration CreateEmpty()
    {
        var root = new MenuId("main");
        return new RadialConfiguration
        {
            RootMenuId = root,
            Menus = [new RadialMenu(root, "Main", [])],
        };
    }
}
