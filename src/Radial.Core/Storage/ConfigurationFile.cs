using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Radial.Core.Catalog;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Validation;

namespace Radial.Core.Storage;

public enum ConfigurationParseStatus
{
    /// <summary>Configuración leída y válida (puede haber avisos).</summary>
    Ok,

    /// <summary>Era JSON con la estructura esperada, pero con errores de contenido o de validación.</summary>
    Invalid,

    /// <summary>No es utilizable como configuración: demasiado grande, JSON mal formado o demasiado profundo, o sin estructura.</summary>
    Corrupt,

    /// <summary>La versión del esquema es posterior a la que conoce esta versión de Radial. No se interpreta ni se debe sobrescribir.</summary>
    FutureVersion,
}

/// <param name="Configuration">Solo con <see cref="ConfigurationParseStatus.Ok"/>.</param>
/// <param name="Issues">Errores y avisos (códigos de <see cref="ConfigurationCodes"/> y de <see cref="ValidationCodes"/>).</param>
/// <param name="FileVersion"><c>schemaVersion</c> del archivo, si se pudo leer.</param>
public sealed record ConfigurationParseResult(
    ConfigurationParseStatus Status,
    RadialConfiguration? Configuration,
    IReadOnlyList<ValidationIssue> Issues,
    int? FileVersion = null)
{
    public bool IsOk => Status == ConfigurationParseStatus.Ok;
}

/// <summary>
/// Lectura y escritura del JSON de la configuración, sin tocar el disco. Lectura: límite de tamaño y de profundidad, versión del
/// esquema (futura = rechazada sin interpretarla), migraciones encadenadas, conversión al modelo y validación con
/// <see cref="ConfigurationValidator"/>; nunca lanza por el contenido, lo devuelve como <see cref="ConfigurationParseResult"/>.
/// Nunca se ejecuta nada durante la lectura. Escritura: siempre la versión actual, con las acciones desconocidas reescritas tal
/// cual las leyó. Lo que no forma parte del esquema (propiedades que Radial no conoce) NO se conserva al reescribir; sí las
/// acciones desconocidas completas (sección 7.1).
/// </summary>
public static class ConfigurationFile
{
    /// <summary>Versión del esquema que escribe esta versión de Radial.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Tamaño máximo de un archivo de configuración, en bytes.</summary>
    public const int MaxBytes = 1_048_576;

    /// <summary>Profundidad máxima de anidamiento del JSON.</summary>
    public const int MaxJsonDepth = 32;

    private const double MaxDwellMilliseconds = 3_600_000;
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Escribe la configuración como JSON con sangrías (UTF-8 sin BOM al guardarlo). No valida: quien guarda valida antes.
    /// </summary>
    /// <exception cref="NotSupportedException">Hay un tipo de acción sin serialización definida.</exception>
    public static string Serialize(RadialConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Toda acción conocida de un menú queda enlazada a la biblioteca: la acción se guarda una sola vez.
        configuration = LibraryResolver.Normalize(configuration);
        var library = IndexLibrary(configuration);

        var dto = new ConfigFileDto
        {
            SchemaVersion = CurrentSchemaVersion,
            RootMenuId = configuration.RootMenuId.Value,
            Geometry = new GeometryDto
            {
                DeadZoneRadius = configuration.Geometry.DeadZoneRadius,
                HysteresisDegrees = configuration.Geometry.HysteresisDegrees,
            },
            Navigation = new NavigationDto
            {
                SubmenuDwellMs = configuration.Navigation.SubmenuDwell.TotalMilliseconds,
                BackDwellMs = configuration.Navigation.BackDwell.TotalMilliseconds,
                MaxDepth = configuration.Navigation.MaxDepth,
            },
            Actions = [.. configuration.Actions.Select(ToDto)],
            Menus = [.. configuration.Menus.Select(menu => ToDto(menu, library))],
        };

        return JsonSerializer.Serialize(dto, ConfigurationJsonContext.Default.ConfigFileDto) + "\n";
    }

    /// <param name="salvageInvalidActions">
    /// Carga tolerante (la usa el almacén en disco): una acción de tipo conocido que no es válida por sí misma se neutraliza (se conserva
    /// su JSON y no se ejecuta, con un aviso) en lugar de invalidar toda la configuración. Por defecto, estricto: lo que valida el editor.
    /// </param>
    public static ConfigurationParseResult Parse(string json, ConfigurationMigrator? migrator = null, bool salvageInvalidActions = false)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json), migrator, salvageInvalidActions);
    }

    public static ConfigurationParseResult Parse(ReadOnlySpan<byte> utf8Json, ConfigurationMigrator? migrator = null, bool salvageInvalidActions = false)
    {
        migrator ??= ConfigurationMigrator.Default;

        if (utf8Json.Length > MaxBytes)
        {
            return Fail(ConfigurationParseStatus.Corrupt, ConfigurationCodes.TooLarge, "$",
                $"El archivo supera el tamaño máximo de {MaxBytes / 1024} KiB.");
        }

        if (utf8Json.StartsWith(Utf8Bom))
        {
            utf8Json = utf8Json[Utf8Bom.Length..];
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(
                utf8Json,
                nodeOptions: null,
                new JsonDocumentOptions { MaxDepth = MaxJsonDepth, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        }
        catch (JsonException)
        {
            return Fail(ConfigurationParseStatus.Corrupt, ConfigurationCodes.JsonInvalid, "$",
                $"El archivo no es JSON válido o supera la profundidad máxima de {MaxJsonDepth} niveles.");
        }

        if (node is not JsonObject root)
        {
            return Fail(ConfigurationParseStatus.Corrupt, ConfigurationCodes.RootInvalid, "$", "El contenido del archivo debe ser un objeto JSON.");
        }

        if (!root.TryGetPropertyValue("schemaVersion", out var versionNode) || versionNode is null)
        {
            return Fail(ConfigurationParseStatus.Corrupt, ConfigurationCodes.SchemaVersionMissing, "schemaVersion", "Falta el campo «schemaVersion».");
        }

        if (versionNode is not JsonValue versionValue || !versionValue.TryGetValue<int>(out var version) || version < 1)
        {
            return Fail(ConfigurationParseStatus.Corrupt, ConfigurationCodes.SchemaVersionInvalid, "schemaVersion", "«schemaVersion» debe ser un entero positivo.");
        }

        if (version > migrator.TargetVersion)
        {
            return new ConfigurationParseResult(
                ConfigurationParseStatus.FutureVersion,
                null,
                [new ValidationIssue(
                    ValidationSeverity.Error,
                    ConfigurationCodes.SchemaVersionFuture,
                    $"La configuración es de una versión posterior del esquema ({version}); esta versión de Radial solo conoce hasta la {migrator.TargetVersion}.",
                    "schemaVersion")],
                version);
        }

        var notes = new List<ValidationIssue>();
        try
        {
            migrator.Migrate(root, version, notes);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or ArgumentException or NotSupportedException)
        {
            return Fail(ConfigurationParseStatus.Invalid, ConfigurationCodes.MigrationFailed, "schemaVersion",
                $"No se pudo migrar la configuración desde la versión {version}.", version);
        }

        ConfigFileDto? dto;
        try
        {
            dto = root.Deserialize(ConfigurationJsonContext.Default.ConfigFileDto);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            return Fail(ConfigurationParseStatus.Corrupt, ConfigurationCodes.StructureInvalid, "$",
                "La estructura del archivo no corresponde a una configuración de Radial.", version);
        }

        if (dto is null)
        {
            return Fail(ConfigurationParseStatus.Corrupt, ConfigurationCodes.StructureInvalid, "$", "El archivo está vacío.", version);
        }

        var issues = new List<ValidationIssue>(notes);
        var configuration = ToModel(dto, issues, salvageInvalidActions);
        if (configuration is null)
        {
            return new ConfigurationParseResult(ConfigurationParseStatus.Invalid, null, issues, version);
        }

        var validation = ConfigurationValidator.Validate(configuration);
        issues.AddRange(validation.Issues);
        return validation.IsValid
            ? new ConfigurationParseResult(ConfigurationParseStatus.Ok, configuration, issues, version)
            : new ConfigurationParseResult(ConfigurationParseStatus.Invalid, null, issues, version);
    }

    private static ConfigurationParseResult Fail(ConfigurationParseStatus status, string code, string path, string message, int? version = null) =>
        new(status, null, [new ValidationIssue(ValidationSeverity.Error, code, message, path)], version);

    // Modelo -> DTO

    private static MenuDto ToDto(RadialMenu menu, Dictionary<string, ActionDefinition> library) => new()
    {
        Id = menu.Id.Value,
        Name = menu.Name,
        Items = [.. menu.Items.Select(item => ToDto(item, library))],
    };

    private static ActionDefDto ToDto(ActionDefinition definition) => new()
    {
        Id = definition.Id,
        Name = definition.Name,
        Description = definition.Description.Length > 0 ? definition.Description : null,
        Category = definition.Category.Id,
        Tags = definition.TagList.Count > 0 ? [.. definition.TagList] : null,
        BuiltIn = definition.IsBuiltIn ? true : null,
        Icon = ToDto(definition.Icon),
        Action = ActionJson.ToJson(definition.Action),
    };

    private static IconDto? ToDto(IconReference? icon) =>
        icon is null ? null : new IconDto { Source = icon.Source == IconSource.File ? "file" : "glyph", Value = icon.Value };

    private static Dictionary<string, ActionDefinition> IndexLibrary(RadialConfiguration configuration)
    {
        var index = new Dictionary<string, ActionDefinition>(StringComparer.Ordinal);
        foreach (var definition in configuration.Actions)
        {
            index.TryAdd(definition.Id, definition); // con ids repetidos manda el primero, como al leer
        }

        return index;
    }

    /// <summary>
    /// Una colocación se guarda como referencia a la biblioteca (<c>actionId</c> + ajustes propios) cuando no es un grupo y, si su acción es
    /// de tipo desconocido o está neutralizada, SOLO si la definición de la biblioteca tiene esa misma acción. Así una acción inerte no
    /// pierde su identidad ni su vínculo al guardar (el JSON vive una sola vez, en la biblioteca), y borrar la definición sigue
    /// llevándose sus colocaciones. Una colocación de tipo desconocido con otra acción distinta se queda en línea, como siempre.
    /// </summary>
    private static bool IsLinked(RadialItem item, Dictionary<string, ActionDefinition> library)
    {
        if (item.DefinitionId is null || item.IsGroup)
        {
            return false;
        }

        return !item.IsUnsupported || (library.TryGetValue(item.DefinitionId, out var definition) && definition.Action.Equals(item.Action));
    }

    private static ItemDto ToDto(RadialItem item, Dictionary<string, ActionDefinition> library)
    {
        // Colocación enlazada: solo la referencia y los ajustes propios; la acción vive en la biblioteca.
        if (IsLinked(item, library))
        {
            return new ItemDto
            {
                Id = item.Id.Value,
                Position = item.Position.ToString(),
                ActionId = item.DefinitionId,
                Label = item.LabelOverride,
                Icon = ToDto(item.IconOverride),
            };
        }

        // Grupo, o acción de tipo desconocido que no es la de su definición: la acción va en el propio elemento.
        return new ItemDto
        {
            Id = item.Id.Value,
            Name = item.Name,
            Position = item.Position.ToString(),
            Icon = ToDto(item.Icon),
            Action = ActionJson.ToJson(item.Action),
        };
    }

    // DTO -> modelo. Devuelve null si hubo errores de lectura (están en issues).

    private static RadialConfiguration? ToModel(ConfigFileDto dto, List<ValidationIssue> issues, bool salvage)
    {
        var errorsBefore = issues.Count;
        var geometry = new GeometrySettings();
        if (dto.Geometry is { } g)
        {
            geometry = new GeometrySettings
            {
                DeadZoneRadius = g.DeadZoneRadius ?? geometry.DeadZoneRadius,
                HysteresisDegrees = g.HysteresisDegrees ?? geometry.HysteresisDegrees,
            };
        }

        var navigation = new NavigationSettings();
        if (dto.Navigation is { } n)
        {
            navigation = new NavigationSettings
            {
                SubmenuDwell = ToDwell(n.SubmenuDwellMs, navigation.SubmenuDwell, "navigation.submenuDwellMs", issues),
                BackDwell = ToDwell(n.BackDwellMs, navigation.BackDwell, "navigation.backDwellMs", issues),
                MaxDepth = n.MaxDepth ?? navigation.MaxDepth,
            };
        }

        var library = new List<ActionDefinition>();
        var libraryById = new Dictionary<string, ActionDefinition>(StringComparer.Ordinal);
        for (var index = 0; index < (dto.Actions?.Count ?? 0); index++)
        {
            var definition = ToDefinition(dto.Actions![index], $"actions[{index}]", issues, salvage);
            if (definition is not null)
            {
                library.Add(definition);
                libraryById.TryAdd(definition.Id, definition); // con ids repetidos manda el primero; la validación señala el duplicado
            }
        }

        var menus = new List<RadialMenu>();
        for (var menuIndex = 0; menuIndex < (dto.Menus?.Count ?? 0); menuIndex++)
        {
            var menuDto = dto.Menus![menuIndex];
            if (menuDto is null)
            {
                issues.Add(FieldError($"menus[{menuIndex}]", "Hay un menú vacío (null)."));
                continue;
            }

            var menuLabel = string.IsNullOrWhiteSpace(menuDto.Id) ? menuIndex.ToString() : menuDto.Id;
            var items = new List<RadialItem>();
            for (var itemIndex = 0; itemIndex < (menuDto.Items?.Count ?? 0); itemIndex++)
            {
                var item = ToItem(menuDto.Items![itemIndex], $"menus[{menuLabel}].items[{itemIndex}]", libraryById, issues, salvage);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            menus.Add(new RadialMenu(new MenuId(menuDto.Id ?? string.Empty), menuDto.Name ?? string.Empty, items));
        }

        return HasErrorsSince(issues, errorsBefore)
            ? null
            : new RadialConfiguration
            {
                RootMenuId = new MenuId(dto.RootMenuId ?? string.Empty),
                Menus = menus,
                Actions = library,
                Geometry = geometry,
                Navigation = navigation,
            };
    }

    private static ActionDefinition? ToDefinition(ActionDefDto? dto, string path, List<ValidationIssue> issues, bool salvage)
    {
        if (dto is null)
        {
            issues.Add(FieldError(path, "Hay una acción vacía (null) en la biblioteca."));
            return null;
        }

        if (!string.IsNullOrWhiteSpace(dto.Id))
        {
            path = $"actions[{dto.Id}]";
        }

        var before = issues.Count;
        var icon = ToIcon(dto.Icon, $"{path}.icon", issues);
        if (dto.Icon is null)
        {
            issues.Add(FieldError($"{path}.icon", "Falta el icono de la acción."));
        }

        RadialAction? action = null;
        if (dto.Action is null)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, ValidationCodes.ActionMissing, "La acción de la biblioteca no tiene acción.", $"{path}.action"));
        }
        else
        {
            action = ActionJson.FromJson(dto.Action, $"{path}.action", issues, salvage);
        }

        return HasErrorsSince(issues, before) || action is null || icon is null
            ? null
            : new ActionDefinition(
                dto.Id ?? string.Empty,
                dto.Name ?? string.Empty,
                new ActionCategory(dto.Category ?? ActionCategory.Custom.Id),
                dto.Description ?? string.Empty,
                icon,
                action,
                dto.Tags is { Count: > 0 } ? dto.Tags : null,
                dto.BuiltIn ?? false);
    }

    private static IconReference? ToIcon(IconDto? iconDto, string path, List<ValidationIssue> issues)
    {
        if (iconDto is null)
        {
            return null;
        }

        var source = iconDto.Source?.ToLowerInvariant() switch
        {
            "glyph" => IconSource.Glyph,
            "file" => IconSource.File,
            _ => (IconSource?)null,
        };

        if (source is null)
        {
            issues.Add(FieldError($"{path}.source", "El origen del icono debe ser «glyph» o «file»."));
            return null;
        }

        return new IconReference(source.Value, iconDto.Value ?? string.Empty);
    }

    private static RadialItem? ToItem(ItemDto? dto, string path, Dictionary<string, ActionDefinition> library, List<ValidationIssue> issues, bool salvage)
    {
        if (dto is null)
        {
            issues.Add(FieldError(path, "Hay un elemento vacío (null)."));
            return null;
        }

        if (!string.IsNullOrWhiteSpace(dto.Id))
        {
            path = path[..path.LastIndexOf('[')] + $"[{dto.Id}]";
        }

        var before = issues.Count;

        if (!TryParseDirection(dto.Position, out var position))
        {
            issues.Add(FieldError($"{path}.position", "La posición debe ser una de: " + string.Join(", ", Enum.GetNames<RadialDirection>()) + "."));
        }

        var icon = ToIcon(dto.Icon, $"{path}.icon", issues);

        // Colocación enlazada a la biblioteca: la acción, el nombre y el icono salen de la definición (salvo los propios).
        if (dto.ActionId is not null)
        {
            if (dto.Action is not null)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, ConfigurationCodes.ItemActionConflict,
                    "El elemento tiene «actionId» y «action» a la vez: la acción tiene que estar en un solo sitio.", path));
                return null;
            }

            if (!library.TryGetValue(dto.ActionId, out var definition))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, ConfigurationCodes.ActionRefMissing,
                    $"El elemento referencia la acción '{dto.ActionId}', que no está en la biblioteca.", $"{path}.actionId"));
                return null;
            }

            if (HasErrorsSince(issues, before))
            {
                return null;
            }

            var placement = new RadialItem(new ItemId(dto.Id ?? string.Empty), dto.Label ?? definition.Name, position, definition.Action, null, definition.Id)
            {
                LabelOverride = dto.Label,
                IconOverride = icon,
            };
            return LibraryResolver.Materialize(placement, definition);
        }

        RadialAction? action = null;
        if (dto.Action is null)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, ValidationCodes.ActionMissing, "El elemento no tiene acción.", $"{path}.action"));
        }
        else
        {
            action = ActionJson.FromJson(dto.Action, $"{path}.action", issues, salvage);
        }

        return HasErrorsSince(issues, before) || action is null
            ? null
            : new RadialItem(new ItemId(dto.Id ?? string.Empty), dto.Name ?? string.Empty, position, action, icon, dto.DefinitionId);
    }

    private static bool TryParseDirection(string? text, out RadialDirection direction)
    {
        foreach (var name in Enum.GetNames<RadialDirection>())
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                direction = Enum.Parse<RadialDirection>(name);
                return true;
            }
        }

        direction = default;
        return false;
    }

    private static TimeSpan ToDwell(double? milliseconds, TimeSpan fallback, string path, List<ValidationIssue> issues)
    {
        if (milliseconds is null)
        {
            return fallback;
        }

        if (!double.IsFinite(milliseconds.Value) || milliseconds.Value < 0 || milliseconds.Value > MaxDwellMilliseconds)
        {
            issues.Add(FieldError(path, "El tiempo de permanencia debe ser un número de milisegundos razonable."));
            return fallback;
        }

        return TimeSpan.FromMilliseconds(milliseconds.Value);
    }

    // Los avisos (migración, acciones neutralizadas) no son errores de lectura: solo estos descartan el elemento.
    private static bool HasErrorsSince(List<ValidationIssue> issues, int start)
    {
        for (var index = start; index < issues.Count; index++)
        {
            if (issues[index].Severity == ValidationSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private static ValidationIssue FieldError(string path, string message) =>
        new(ValidationSeverity.Error, ConfigurationCodes.FieldInvalid, message, path);
}
