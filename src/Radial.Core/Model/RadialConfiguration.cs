using System.Diagnostics.CodeAnalysis;
using Radial.Core.Catalog;

namespace Radial.Core.Model;

public sealed record RadialConfiguration
{
    public required MenuId RootMenuId { get; init; }

    public IReadOnlyList<RadialMenu> Menus { get; init; } = [];

    /// <summary>
    /// La biblioteca de acciones (<see cref="ActionDefinition"/>): fuente de verdad de lo que hace cada acción. Se guarda aunque
    /// ninguna colocación la use; los elementos de los menús la referencian por <see cref="RadialItem.DefinitionId"/>.
    /// </summary>
    public IReadOnlyList<ActionDefinition> Actions { get; init; } = [];

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

    public bool TryGetDefinition(string? id, [NotNullWhen(true)] out ActionDefinition? definition)
    {
        if (id is not null)
        {
            foreach (var candidate in Actions)
            {
                if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
                {
                    definition = candidate;
                    return true;
                }
            }
        }

        definition = null;
        return false;
    }

    /// <summary>Configuración mínima válida: un único menú raíz vacío, sin biblioteca y con valores predeterminados.</summary>
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
