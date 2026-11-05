using System.Text.Json.Nodes;

namespace Radial.Core.Storage;

// Forma del archivo config.json. Todos los campos son opcionales para poder leer archivos incompletos o editados a mano: lo que
// falta se informa al convertir al modelo o lo detecta el validador, nunca una excepción. La acción de cada elemento es un
// objeto JSON abierto (JsonObject) porque su tipo se resuelve a mano (ActionJson), sección 7.1 del documento técnico.

internal sealed class ConfigFileDto
{
    public int? SchemaVersion { get; set; }

    public string? RootMenuId { get; set; }

    public GeometryDto? Geometry { get; set; }

    public NavigationDto? Navigation { get; set; }

    public List<ActionDefDto>? Actions { get; set; }

    public List<MenuDto>? Menus { get; set; }
}

// Una definición de la biblioteca de acciones (versión 2 del esquema).
internal sealed class ActionDefDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? Category { get; set; }

    public List<string>? Tags { get; set; }

    public bool? BuiltIn { get; set; }

    public IconDto? Icon { get; set; }

    public JsonObject? Action { get; set; }
}

internal sealed class GeometryDto
{
    public double? DeadZoneRadius { get; set; }

    public double? HysteresisDegrees { get; set; }
}

internal sealed class NavigationDto
{
    public double? SubmenuDwellMs { get; set; }

    public double? BackDwellMs { get; set; }

    public int? MaxDepth { get; set; }
}

internal sealed class MenuDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public List<ItemDto>? Items { get; set; }
}

internal sealed class ItemDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Position { get; set; }

    public IconDto? Icon { get; set; }

    public string? DefinitionId { get; set; }

    // Colocación enlazada a la biblioteca (versión 2): la acción vive en la definición; Label e Icon son ajustes propios de la colocación.
    public string? ActionId { get; set; }

    public string? Label { get; set; }

    public JsonObject? Action { get; set; }
}

internal sealed class IconDto
{
    public string? Source { get; set; }

    public string? Value { get; set; }
}
