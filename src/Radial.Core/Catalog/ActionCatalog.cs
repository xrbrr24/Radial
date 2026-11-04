using System.Diagnostics.CodeAnalysis;

namespace Radial.Core.Catalog;

/// <summary>
/// Conjunto validado de <see cref="ActionDefinition"/>: identificadores únicos y definiciones correctas. Es lo que consulta quien
/// construye un menú (hoy el provisional; después la configuración y el editor). Inmutable una vez creado.
/// </summary>
public sealed class ActionCatalog
{
    private readonly Dictionary<string, ActionDefinition> _byId;

    /// <exception cref="ArgumentException">Una definición tiene problemas o dos comparten identificador.</exception>
    public ActionCatalog(IEnumerable<ActionDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var list = definitions.ToList();
        var byId = new Dictionary<string, ActionDefinition>(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var definition in list)
        {
            if (definition is null)
            {
                errors.Add("Hay una definición nula.");
                continue;
            }

            var label = string.IsNullOrWhiteSpace(definition.Id) ? "(sin identificador)" : definition.Id;
            errors.AddRange(definition.GetProblems().Select(problem => $"{label}: {problem}"));

            if (!string.IsNullOrWhiteSpace(definition.Id) && !byId.TryAdd(definition.Id, definition))
            {
                errors.Add($"{label}: el identificador está repetido.");
            }
        }

        if (errors.Count > 0)
        {
            throw new ArgumentException("El repertorio de acciones no es válido: " + string.Join(" ", errors), nameof(definitions));
        }

        _byId = byId;
        Definitions = list.AsReadOnly();
    }

    /// <summary>Las definiciones, en el orden en que se dieron.</summary>
    public IReadOnlyList<ActionDefinition> Definitions { get; }

    public bool TryGet(string id, [NotNullWhen(true)] out ActionDefinition? definition)
    {
        if (id is null)
        {
            definition = null;
            return false;
        }

        return _byId.TryGetValue(id, out definition);
    }

    /// <exception cref="KeyNotFoundException">No hay ninguna definición con ese identificador.</exception>
    public ActionDefinition Get(string id) =>
        TryGet(id, out var definition) ? definition : throw new KeyNotFoundException($"No existe la acción '{id}'.");

    public IEnumerable<ActionDefinition> InCategory(ActionCategory category) => Definitions.Where(d => d.Category == category);
}
