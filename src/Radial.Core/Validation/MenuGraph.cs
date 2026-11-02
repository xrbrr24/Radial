using Radial.Core.Model;

namespace Radial.Core.Validation;

/// <summary>
/// Análisis de las referencias entre menús: ciclos, profundidad y alcanzabilidad.
/// Recorrido iterativo para que una configuración importada con cadenas muy largas no desborde la pila.
/// </summary>
internal sealed class MenuGraph
{
    private readonly Dictionary<MenuId, List<MenuId>> _edges = new();
    private readonly List<MenuId> _order = new();

    public MenuGraph(RadialConfiguration configuration)
    {
        // Con ids duplicados prevalece el primer menú; el duplicado ya se informa por separado.
        foreach (var menu in configuration.Menus)
        {
            if (_edges.ContainsKey(menu.Id))
            {
                continue;
            }

            _order.Add(menu.Id);
            _edges[menu.Id] = new List<MenuId>();
        }

        foreach (var menu in configuration.Menus)
        {
            foreach (var item in menu.Items)
            {
                if (item.Action is OpenMenuAction open && _edges.ContainsKey(open.TargetMenuId))
                {
                    _edges[menu.Id].Add(open.TargetMenuId);
                }
            }
        }
    }

    /// <summary>Ciclos encontrados, cada uno como la lista de menús que lo forman en orden de recorrido.</summary>
    public List<IReadOnlyList<MenuId>> Cycles { get; } = new();

    /// <summary>Menús alcanzables desde la raíz.</summary>
    public HashSet<MenuId> Reachable { get; } = new();

    /// <summary>Número de menús de la cadena más larga que empieza en la raíz. 0 si la raíz no existe.</summary>
    public int Depth { get; private set; }

    public void Analyze(MenuId root)
    {
        var state = new Dictionary<MenuId, VisitState>();
        var height = new Dictionary<MenuId, int>();

        if (_edges.ContainsKey(root))
        {
            Visit(root, state, height);
            Reachable.UnionWith(state.Keys);
            Depth = height[root];
        }

        foreach (var id in _order)
        {
            if (!state.ContainsKey(id))
            {
                Visit(id, state, height);
            }
        }
    }

    private void Visit(MenuId start, Dictionary<MenuId, VisitState> state, Dictionary<MenuId, int> height)
    {
        var path = new List<(MenuId Id, int NextEdge)> { (start, 0) };
        state[start] = VisitState.InProgress;

        while (path.Count > 0)
        {
            var (id, next) = path[^1];
            var edges = _edges[id];

            if (next < edges.Count)
            {
                path[^1] = (id, next + 1);
                var child = edges[next];

                if (!state.TryGetValue(child, out var childState))
                {
                    state[child] = VisitState.InProgress;
                    path.Add((child, 0));
                }
                else if (childState == VisitState.InProgress)
                {
                    var from = path.FindIndex(p => p.Id == child);
                    Cycles.Add(path.Skip(from).Select(p => p.Id).ToList());
                }

                continue;
            }

            // Los hijos aún en curso (aristas de ciclo) no tienen altura y se ignoran.
            height[id] = 1 + edges.Where(height.ContainsKey).Select(c => height[c]).DefaultIfEmpty(0).Max();
            state[id] = VisitState.Done;
            path.RemoveAt(path.Count - 1);
        }
    }

    private enum VisitState
    {
        InProgress,
        Done,
    }
}
