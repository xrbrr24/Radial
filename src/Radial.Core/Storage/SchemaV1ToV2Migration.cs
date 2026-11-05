using System.Globalization;
using System.Text.Json.Nodes;
using Radial.Core.Catalog;
using Radial.Core.Validation;

namespace Radial.Core.Storage;

/// <summary>
/// Migración de la versión 1 (cada elemento lleva su acción y, a veces, un <c>definitionId</c>) a la 2 (biblioteca de acciones
/// independiente en <c>actions</c>; los elementos solo la referencian con <c>actionId</c>). Trabaja sobre el árbol JSON y no pierde
/// nada:
/// - Conserva todos los menús, todas las posiciones y todos los grupos tal cual.
/// - Cada acción conocida de un elemento pasa a ser una definición de la biblioteca: la del <c>definitionId</c> si es válido, o una
///   generada a partir del nombre. Elementos con la misma definición y la misma acción comparten definición; elementos sin
///   <c>definitionId</c> idénticos (mismo nombre, icono y acción) también: no se duplican definiciones. Si el nombre o el icono de
///   un elemento difieren de la definición, se conservan como ajustes propios de la colocación (<c>label</c>, <c>icon</c>).
/// - Incoherencias: un <c>definitionId</c> repetido con OTRA acción crea una definición nueva (<c>id-2</c>); un <c>definitionId</c> con
///   formato no válido se sustituye por uno generado. Ambos casos se comunican como avisos, y nada se descarta.
/// - Las acciones de tipo desconocido, los grupos (<c>openMenu</c>) y cualquier elemento malformado se dejan intactos (el lector de
///   la versión 2 los conserva o los rechaza igual que antes).
/// </summary>
internal sealed class SchemaV1ToV2Migration : IConfigurationMigration
{
    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        ActionJson.LaunchApplication, ActionJson.OpenFile, ActionJson.OpenFolder, ActionJson.OpenUrl, ActionJson.SendKeys,
    };

    // Las definiciones de la configuración inicial de las fases 4 y 5 que no son atajos (esos están en FactoryActions).
    private static readonly Dictionary<string, string> LegacyFactory = new(StringComparer.Ordinal)
    {
        ["web.browser"] = "browser",
        ["web.url"] = "browser",
        ["files.folder"] = "files",
    };

    public int FromVersion => 1;

    public void Migrate(JsonObject root, ICollection<ValidationIssue> notes)
    {
        var state = new State(notes);

        if (root["menus"] is JsonArray menus)
        {
            for (var m = 0; m < menus.Count; m++)
            {
                if (menus[m] is not JsonObject menu || menu["items"] is not JsonArray items)
                {
                    continue;
                }

                var menuLabel = Text(menu["id"]) is { Length: > 0 } id ? id : m.ToString(CultureInfo.InvariantCulture);
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i] is JsonObject item)
                    {
                        var itemLabel = Text(item["id"]) is { Length: > 0 } itemId ? itemId : i.ToString(CultureInfo.InvariantCulture);
                        MigrateItem(item, $"menus[{menuLabel}].items[{itemLabel}]", state);
                    }
                }
            }
        }

        var library = new JsonArray();
        foreach (var definition in state.Definitions)
        {
            library.Add(definition);
        }

        root["actions"] = library;
    }

    private static void MigrateItem(JsonObject item, string path, State state)
    {
        if (item["action"] is not JsonObject action
            || action["type"] is not JsonValue typeValue
            || !typeValue.TryGetValue<string>(out var type)
            || !KnownTypes.Contains(type))
        {
            return; // grupo, acción desconocida o elemento malformado: se deja como está
        }

        var name = Text(item["name"]) ?? string.Empty;
        var icon = item["icon"];
        var declared = Text(item["definitionId"]);
        var contentKey = string.Join('\u0001', action.ToJsonString(), name, icon?.ToJsonString() ?? string.Empty);

        string id;
        if (declared is not null && ActionDefinition.IsValidId(declared))
        {
            if (!state.ById.TryGetValue(declared, out var existing))
            {
                id = state.Create(declared, name, icon, action, type);
            }
            else if (JsonNode.DeepEquals(existing["action"], action))
            {
                id = declared; // misma definición y misma acción: se reutiliza
            }
            else
            {
                id = state.Create(LibraryResolver.UniqueId(declared + "-2", state.Used), name, icon, action, type);
                state.Notes.Add(Note(path, $"La acción '{declared}' aparecía con contenidos distintos; se conservó cada uno como definición propia ('{declared}' y '{id}')."));
            }
        }
        else
        {
            if (declared is not null)
            {
                state.Notes.Add(Note(path, $"El identificador de acción '{Truncate(declared)}' no es válido; se generó uno a partir del nombre."));
            }

            id = state.ByContent.TryGetValue(contentKey, out var shared)
                ? shared
                : state.Create(LibraryResolver.UniqueId("custom." + LibraryResolver.Slug(name), state.Used), name, icon, action, type);
        }

        state.ByContent[contentKey] = id;

        // La colocación solo referencia la definición; nombre e icono distintos de ella quedan como ajustes propios.
        var definition = state.ById[id];
        item.Remove("action");
        item.Remove("definitionId");
        item.Remove("name");
        item["actionId"] = id;
        if (name != Text(definition["name"]) && !(name.Length == 0 && Text(definition["name"]) == FallbackName))
        {
            item["label"] = name;
        }

        if (icon is not null && JsonNode.DeepEquals(icon, definition["icon"]))
        {
            item.Remove("icon");
        }
    }

    private static ValidationIssue Note(string path, string message) =>
        new(ValidationSeverity.Warning, ConfigurationCodes.MigrationNote, message, path);

    private static string Truncate(string text) => text.Length <= 40 ? text : text[..40] + "…";

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private const string FallbackName = "Acción";

    private sealed class State(ICollection<ValidationIssue> notes)
    {
        public ICollection<ValidationIssue> Notes { get; } = notes;

        public List<JsonObject> Definitions { get; } = [];

        public Dictionary<string, JsonObject> ById { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> ByContent { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Used { get; } = new(StringComparer.Ordinal);

        public string Create(string id, string name, JsonNode? icon, JsonObject action, string type)
        {
            var factory = FactoryActions.Shortcuts.FirstOrDefault(d => d.Id == id);
            var isFactory = factory is not null || LegacyFactory.ContainsKey(id);
            var category = factory?.Category.Id ?? (LegacyFactory.TryGetValue(id, out var legacy) ? legacy : CategoryOf(type));

            var definition = new JsonObject
            {
                ["id"] = id,
                ["name"] = name.Length > 0 ? name : FallbackName,
                ["category"] = category,
                ["icon"] = icon?.DeepClone() ?? new JsonObject { ["source"] = "glyph", ["value"] = GlyphOf(type) },
                ["action"] = action.DeepClone(),
            };

            if (factory is not null && factory.Description.Length > 0)
            {
                definition["description"] = factory.Description;
            }

            if (factory is { TagList.Count: > 0 })
            {
                var tags = new JsonArray();
                foreach (var tag in factory.TagList)
                {
                    tags.Add(tag);
                }

                definition["tags"] = tags;
            }

            if (isFactory)
            {
                definition["builtIn"] = true;
            }

            Definitions.Add(definition);
            ById[id] = definition;
            Used.Add(id);
            return id;
        }

        private static string CategoryOf(string type) => type switch
        {
            ActionJson.LaunchApplication => ActionCategory.Applications.Id,
            ActionJson.OpenFile or ActionJson.OpenFolder => ActionCategory.Files.Id,
            ActionJson.OpenUrl => ActionCategory.Browser.Id,
            _ => ActionCategory.Custom.Id,
        };

        private static string GlyphOf(string type) => type switch
        {
            ActionJson.LaunchApplication => "Apps",
            ActionJson.OpenFile => "Document",
            ActionJson.OpenFolder => "Folder",
            ActionJson.OpenUrl => "Link",
            _ => "Keyboard",
        };
    }
}
