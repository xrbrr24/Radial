using System.Globalization;
using System.Text;
using Radial.Core.Model;
using Radial.Core.Validation;

namespace Radial.Core.Catalog;

/// <summary>
/// Pone de acuerdo la biblioteca de acciones y las colocaciones de los menús. La biblioteca es la fuente de verdad: este tipo
/// regenera la vista materializada de cada colocación enlazada (<see cref="Resolve"/>) y, al guardar, convierte las colocaciones
/// sueltas (con su acción puesta directamente en el elemento) en definiciones de la biblioteca sin duplicar las que ya existen
/// (<see cref="Normalize"/>). Funciones puras: no modifican la configuración que reciben.
/// </summary>
public static class LibraryResolver
{
    private const string LooseIdPrefix = "custom.";
    private const int MaxSlugLength = ConfigurationLimits.MaxIdLength - 8;

    /// <summary>La vista materializada de una colocación sobre su definición (nombre e icono propios si los tiene).</summary>
    public static RadialItem Materialize(RadialItem placement, ActionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(definition);

        return placement with
        {
            Name = placement.LabelOverride ?? definition.Name,
            Action = definition.Action,
            Icon = placement.IconOverride ?? definition.Icon,
            DefinitionId = definition.Id,
        };
    }

    /// <summary>Regenera todas las colocaciones enlazadas a partir de la biblioteca. Las que apuntan a una definición inexistente no se tocan (la validación las señala).</summary>
    public static RadialConfiguration Resolve(RadialConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var byId = IndexById(configuration.Actions);
        var changed = false;
        var menus = new List<RadialMenu>(configuration.Menus.Count);
        foreach (var menu in configuration.Menus)
        {
            var items = new List<RadialItem>(menu.Items.Count);
            foreach (var item in menu.Items)
            {
                if (item.DefinitionId is not null && !item.IsGroup && byId.TryGetValue(item.DefinitionId, out var definition))
                {
                    var materialized = Materialize(item, definition);
                    changed |= !materialized.Equals(item);
                    items.Add(materialized);
                }
                else
                {
                    items.Add(item);
                }
            }

            menus.Add(menu with { Items = items });
        }

        return changed ? configuration with { Menus = menus } : configuration;
    }

    /// <summary>
    /// Devuelve una configuración en la que TODA acción conocida de un menú está enlazada a una definición de la biblioteca: las
    /// colocaciones sueltas (o con una referencia que no existe) crean su definición, reutilizando una idéntica si ya la hay
    /// (mismo nombre, icono y acción). Los grupos y las acciones de tipo desconocido se dejan como están. Determinista: el mismo
    /// resultado para la misma entrada.
    /// </summary>
    public static RadialConfiguration Normalize(RadialConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var actions = configuration.Actions.ToList();
        var byId = IndexById(actions);
        var used = new HashSet<string>(byId.Keys, StringComparer.Ordinal);
        var changed = false;
        var menus = new List<RadialMenu>(configuration.Menus.Count);

        foreach (var menu in configuration.Menus)
        {
            var items = new List<RadialItem>(menu.Items.Count);
            foreach (var item in menu.Items)
            {
                if (item.IsGroup || item.IsUnsupported || (item.DefinitionId is not null && byId.ContainsKey(item.DefinitionId)))
                {
                    items.Add(item);
                    continue;
                }

                var icon = item.Icon ?? DefaultIcon(item.Action);
                var match = actions.FirstOrDefault(d => d.Action.Equals(item.Action) && d.Name == item.Name && d.Icon.Equals(icon));
                if (match is null)
                {
                    var id = item.DefinitionId is not null && ActionDefinition.IsValidId(item.DefinitionId) && !used.Contains(item.DefinitionId)
                        ? item.DefinitionId
                        : UniqueId(LooseIdPrefix + Slug(item.Name), used);
                    match = new ActionDefinition(id, item.Name, InferCategory(item.Action), string.Empty, icon, item.Action);
                    actions.Add(match);
                    byId[id] = match;
                    used.Add(id);
                }

                changed = true;
                items.Add(item with { DefinitionId = match.Id, Icon = icon });
            }

            menus.Add(menu with { Items = items });
        }

        return changed ? configuration with { Actions = actions, Menus = menus } : configuration;
    }

    /// <summary>Categoría con la que se archiva una acción creada sin elegir categoría.</summary>
    public static ActionCategory InferCategory(RadialAction action) => action switch
    {
        LaunchApplicationAction => ActionCategory.Applications,
        OpenFileAction or OpenFolderAction => ActionCategory.Files,
        OpenUrlAction => ActionCategory.Browser,
        VolumeAction => ActionCategory.Multimedia,
        WindowControlAction => ActionCategory.System,
        _ => ActionCategory.Custom,
    };

    /// <summary>Nombre de glifo con el que se dibuja una acción sin icono propio.</summary>
    public static IconReference DefaultIcon(RadialAction action) => new(IconSource.Glyph, action switch
    {
        SendKeysAction => "Keyboard",
        LaunchApplicationAction => "Apps",
        OpenFileAction => "Document",
        OpenFolderAction => "Folder",
        OpenUrlAction => "Link",
        TypeTextAction => "Keyboard",
        VolumeAction => "VolumeUp",
        WindowControlAction => "TaskView",
        _ => "More",
    });

    /// <summary>
    /// Texto reducido a minúsculas ASCII, dígitos y «-»: sin tildes, sin signos y con los huecos convertidos en un solo guion.
    /// Vacío se convierte en <c>accion</c>. Sirve para derivar identificadores de nombres.
    /// </summary>
    public static string Slug(string? text)
    {
        var decomposed = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(lower);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxSlugLength)
        {
            slug = slug[..MaxSlugLength].TrimEnd('-');
        }

        return slug.Length == 0 ? "accion" : slug;
    }

    /// <summary><paramref name="baseId"/> si está libre; si no, con sufijo <c>-2</c>, <c>-3</c>... respetando la longitud máxima. No lo añade a <paramref name="used"/>.</summary>
    public static string UniqueId(string baseId, ISet<string> used)
    {
        var candidate = baseId.Length > ConfigurationLimits.MaxIdLength ? baseId[..ConfigurationLimits.MaxIdLength] : baseId;
        for (var attempt = 2; used.Contains(candidate); attempt++)
        {
            var suffix = "-" + attempt.ToString(CultureInfo.InvariantCulture);
            var stem = baseId.Length + suffix.Length > ConfigurationLimits.MaxIdLength ? baseId[..(ConfigurationLimits.MaxIdLength - suffix.Length)] : baseId;
            candidate = stem + suffix;
        }

        return candidate;
    }

    private static Dictionary<string, ActionDefinition> IndexById(IEnumerable<ActionDefinition> definitions)
    {
        var index = new Dictionary<string, ActionDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            index.TryAdd(definition.Id, definition); // con ids repetidos manda el primero; el duplicado lo señala la validación
        }

        return index;
    }
}
