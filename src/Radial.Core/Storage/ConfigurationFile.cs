using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public const int CurrentSchemaVersion = 1;

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
            Menus = [.. configuration.Menus.Select(ToDto)],
        };

        return JsonSerializer.Serialize(dto, ConfigurationJsonContext.Default.ConfigFileDto) + "\n";
    }

    public static ConfigurationParseResult Parse(string json, ConfigurationMigrator? migrator = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json), migrator);
    }

    public static ConfigurationParseResult Parse(ReadOnlySpan<byte> utf8Json, ConfigurationMigrator? migrator = null)
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

        try
        {
            migrator.Migrate(root, version);
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

        var issues = new List<ValidationIssue>();
        var configuration = ToModel(dto, issues);
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

    private static MenuDto ToDto(RadialMenu menu) => new()
    {
        Id = menu.Id.Value,
        Name = menu.Name,
        Items = [.. menu.Items.Select(ToDto)],
    };

    private static ItemDto ToDto(RadialItem item) => new()
    {
        Id = item.Id.Value,
        Name = item.Name,
        Position = item.Position.ToString(),
        Icon = item.Icon is null
            ? null
            : new IconDto { Source = item.Icon.Source == IconSource.File ? "file" : "glyph", Value = item.Icon.Value },
        DefinitionId = item.DefinitionId,
        Action = ActionJson.ToJson(item.Action),
    };

    // DTO -> modelo. Devuelve null si hubo errores de lectura (están en issues).

    private static RadialConfiguration? ToModel(ConfigFileDto dto, List<ValidationIssue> issues)
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
                var item = ToItem(menuDto.Items![itemIndex], $"menus[{menuLabel}].items[{itemIndex}]", issues);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            menus.Add(new RadialMenu(new MenuId(menuDto.Id ?? string.Empty), menuDto.Name ?? string.Empty, items));
        }

        return issues.Count > errorsBefore
            ? null
            : new RadialConfiguration
            {
                RootMenuId = new MenuId(dto.RootMenuId ?? string.Empty),
                Menus = menus,
                Geometry = geometry,
                Navigation = navigation,
            };
    }

    private static RadialItem? ToItem(ItemDto? dto, string path, List<ValidationIssue> issues)
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

        var position = RadialDirection.North;
        if (!TryParseDirection(dto.Position, out position))
        {
            issues.Add(FieldError($"{path}.position", "La posición debe ser una de: " + string.Join(", ", Enum.GetNames<RadialDirection>()) + "."));
        }

        IconReference? icon = null;
        if (dto.Icon is { } iconDto)
        {
            var source = iconDto.Source?.ToLowerInvariant() switch
            {
                "glyph" => IconSource.Glyph,
                "file" => IconSource.File,
                _ => (IconSource?)null,
            };

            if (source is null)
            {
                issues.Add(FieldError($"{path}.icon.source", "El origen del icono debe ser «glyph» o «file»."));
            }
            else
            {
                icon = new IconReference(source.Value, iconDto.Value ?? string.Empty);
            }
        }

        RadialAction? action = null;
        if (dto.Action is null)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, ValidationCodes.ActionMissing, "El elemento no tiene acción.", $"{path}.action"));
        }
        else
        {
            action = ActionJson.FromJson(dto.Action, $"{path}.action", issues);
        }

        return issues.Count > before || action is null
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

    private static ValidationIssue FieldError(string path, string message) =>
        new(ValidationSeverity.Error, ConfigurationCodes.FieldInvalid, message, path);
}
