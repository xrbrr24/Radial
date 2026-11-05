using Radial.Core.Geometry;
using Radial.Core.Model;

namespace Radial.Core.Validation;

/// <summary>Valida una configuración sin lanzar excepciones por contenido inválido: devuelve todos los problemas a la vez.</summary>
public static class ConfigurationValidator
{
    public static ValidationResult Validate(RadialConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var issues = new List<ValidationIssue>();
        ValidateGeometry(configuration.Geometry, issues);
        ValidateNavigation(configuration.Navigation, issues);
        ValidateRoot(configuration, issues);
        ValidateMenus(configuration, issues);
        ValidateMenuGraph(configuration, issues);
        return new ValidationResult(issues);
    }

    private static void ValidateGeometry(GeometrySettings geometry, List<ValidationIssue> issues)
    {
        const string path = "geometry";

        var deadZone = geometry.DeadZoneRadius;
        if (!double.IsFinite(deadZone) || deadZone <= 0 || deadZone > ConfigurationLimits.MaxDeadZoneRadius)
        {
            Error(issues, ValidationCodes.GeometryDeadZoneInvalid, $"{path}.deadZoneRadius",
                $"El radio de la zona central debe ser mayor que 0 y como máximo {ConfigurationLimits.MaxDeadZoneRadius}.");
        }

        var hysteresis = geometry.HysteresisDegrees;
        if (!double.IsFinite(hysteresis) || hysteresis < 0 || hysteresis >= SectorSelector.MaxHysteresisDegrees)
        {
            Error(issues, ValidationCodes.GeometryHysteresisInvalid, $"{path}.hysteresisDegrees",
                $"La histéresis debe estar entre 0 (incluido) y {SectorSelector.MaxHysteresisDegrees} (excluido) grados; " +
                "con un valor mayor no se podría cambiar de sector.");
        }
    }

    private static void ValidateNavigation(NavigationSettings navigation, List<ValidationIssue> issues)
    {
        const string path = "navigation";

        if (!IsValidDwell(navigation.SubmenuDwell))
        {
            Error(issues, ValidationCodes.NavigationSubmenuDwellInvalid, $"{path}.submenuDwell", DwellMessage());
        }

        if (!IsValidDwell(navigation.BackDwell))
        {
            Error(issues, ValidationCodes.NavigationBackDwellInvalid, $"{path}.backDwell", DwellMessage());
        }

        if (navigation.MaxDepth < 1 || navigation.MaxDepth > ConfigurationLimits.MaxDepthLimit)
        {
            Error(issues, ValidationCodes.NavigationMaxDepthInvalid, $"{path}.maxDepth",
                $"La profundidad máxima debe estar entre 1 y {ConfigurationLimits.MaxDepthLimit}.");
        }
    }

    private static bool IsValidDwell(TimeSpan value) => value >= ConfigurationLimits.MinDwell && value <= ConfigurationLimits.MaxDwell;

    private static string DwellMessage() =>
        $"El tiempo de permanencia debe estar entre {ConfigurationLimits.MinDwell.TotalMilliseconds} ms y {ConfigurationLimits.MaxDwell.TotalSeconds} s.";

    private static void ValidateRoot(RadialConfiguration configuration, List<ValidationIssue> issues)
    {
        var root = configuration.RootMenuId;
        if (string.IsNullOrWhiteSpace(root.Value))
        {
            Error(issues, ValidationCodes.RootEmpty, "rootMenuId", "Falta el identificador del menú raíz.");
        }
        else if (!configuration.TryGetMenu(root, out _))
        {
            Error(issues, ValidationCodes.RootMissing, "rootMenuId", $"El menú raíz '{root}' no existe.");
        }
    }

    private static void ValidateMenus(RadialConfiguration configuration, List<ValidationIssue> issues)
    {
        var menuIds = new HashSet<string>(StringComparer.Ordinal);
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        var knownMenus = configuration.Menus.Select(m => m.Id).ToHashSet();

        for (var index = 0; index < configuration.Menus.Count; index++)
        {
            var menu = configuration.Menus[index];
            var menuPath = $"menus[{Label(menu.Id.Value, index)}]";

            if (ValidateId(menu.Id.Value, $"{menuPath}.id", issues) && !menuIds.Add(menu.Id.Value))
            {
                Error(issues, ValidationCodes.MenuDuplicateId, $"{menuPath}.id", $"Ya existe un menú con el identificador '{menu.Id}'.");
            }

            ValidateName(menu.Name, $"{menuPath}.name", issues);
            ValidateItems(knownMenus, menu, menuPath, itemIds, issues);
        }
    }

    private static void ValidateItems(
        HashSet<MenuId> knownMenus,
        RadialMenu menu,
        string menuPath,
        HashSet<string> itemIds,
        List<ValidationIssue> issues)
    {
        var usedPositions = new HashSet<RadialDirection>();

        for (var index = 0; index < menu.Items.Count; index++)
        {
            var item = menu.Items[index];
            var path = $"{menuPath}.items[{Label(item.Id.Value, index)}]";

            if (ValidateId(item.Id.Value, $"{path}.id", issues) && !itemIds.Add(item.Id.Value))
            {
                Error(issues, ValidationCodes.ItemDuplicateId, $"{path}.id", $"Ya existe un elemento con el identificador '{item.Id}'.");
            }

            ValidateName(item.Name, $"{path}.name", issues);

            if (!Enum.IsDefined(item.Position))
            {
                Error(issues, ValidationCodes.ItemPositionInvalid, $"{path}.position", "La posición no es una de las ocho direcciones.");
            }
            else if (!usedPositions.Add(item.Position))
            {
                Error(issues, ValidationCodes.ItemPositionDuplicate, $"{path}.position",
                    $"La posición {item.Position} ya está ocupada por otro elemento de este menú.");
            }

            ValidateDefinitionId(item.DefinitionId, $"{path}.definitionId", issues);
            ValidateIcon(item.Icon, $"{path}.icon", issues);
            ValidateAction(knownMenus, item.Action, $"{path}.action", issues);
        }
    }

    private static void ValidateDefinitionId(string? definitionId, string path, List<ValidationIssue> issues)
    {
        if (definitionId is not null && (string.IsNullOrWhiteSpace(definitionId) || definitionId.Length > ConfigurationLimits.MaxIdLength))
        {
            Error(issues, ValidationCodes.ItemDefinitionIdInvalid, path,
                $"El identificador de la definición no puede estar vacío ni superar los {ConfigurationLimits.MaxIdLength} caracteres.");
        }
    }

    private static void ValidateIcon(IconReference? icon, string path, List<ValidationIssue> issues)
    {
        if (icon is null)
        {
            return;
        }

        if (!Enum.IsDefined(icon.Source) || string.IsNullOrWhiteSpace(icon.Value) || icon.Value.Length > ConfigurationLimits.MaxIconValueLength)
        {
            Error(issues, ValidationCodes.IconInvalid, path,
                $"El icono necesita un origen válido y un valor de hasta {ConfigurationLimits.MaxIconValueLength} caracteres.");
        }
    }

    private static void ValidateAction(HashSet<MenuId> knownMenus, RadialAction? action, string path, List<ValidationIssue> issues)
    {
        if (action is null)
        {
            Error(issues, ValidationCodes.ActionMissing, path, "El elemento no tiene acción.");
            return;
        }

        if (action is UnknownAction unknown)
        {
            // Se conserva tal cual y no se ejecuta: es un aviso, no un error (documento técnico, 7.1).
            if (string.IsNullOrWhiteSpace(unknown.Type))
            {
                Error(issues, ValidationCodes.ActionInvalid, path, "El tipo de la acción desconocida es obligatorio.");
            }
            else
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, ValidationCodes.ActionUnknownType,
                    "Esta acción es de un tipo que esta versión de Radial no conoce: se conserva en la configuración, pero no se puede ejecutar.", path));
            }

            return;
        }

        foreach (var problem in action.GetProblems())
        {
            Error(issues, ValidationCodes.ActionInvalid, path, problem);
        }

        if (action is OpenMenuAction open &&
            !string.IsNullOrWhiteSpace(open.TargetMenuId.Value) &&
            !knownMenus.Contains(open.TargetMenuId))
        {
            Error(issues, ValidationCodes.ActionMenuMissing, path, $"El submenú '{open.TargetMenuId}' no existe.");
        }
    }

    private static void ValidateMenuGraph(RadialConfiguration configuration, List<ValidationIssue> issues)
    {
        var graph = new MenuGraph(configuration);
        graph.Analyze(configuration.RootMenuId);

        foreach (var cycle in graph.Cycles)
        {
            var route = string.Join(" → ", cycle.Append(cycle[0]));
            Error(issues, ValidationCodes.MenuCycle, $"menus[{cycle[0]}]", $"Los menús forman un ciclo: {route}.");
        }

        var maxDepth = configuration.Navigation.MaxDepth;
        if (maxDepth >= 1 && graph.Depth > maxDepth)
        {
            Error(issues, ValidationCodes.MenuTooDeep, "navigation.maxDepth",
                $"La cadena de submenús desde la raíz alcanza {graph.Depth} niveles y el máximo es {maxDepth}.");
        }

        if (configuration.TryGetMenu(configuration.RootMenuId, out _))
        {
            foreach (var menu in configuration.Menus.Where(m => !graph.Reachable.Contains(m.Id)))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, ValidationCodes.MenuUnreachable,
                    $"El menú '{menu.Id}' no se puede alcanzar desde el menú raíz.", $"menus[{menu.Id}]"));
            }
        }
    }

    private static bool ValidateId(string? value, string path, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Error(issues, ValidationCodes.IdEmpty, path, "El identificador no puede estar vacío.");
            return false;
        }

        if (value.Length > ConfigurationLimits.MaxIdLength)
        {
            Error(issues, ValidationCodes.IdTooLong, path, $"El identificador supera los {ConfigurationLimits.MaxIdLength} caracteres.");
            return false;
        }

        return true;
    }

    private static void ValidateName(string? value, string path, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Error(issues, ValidationCodes.NameEmpty, path, "El nombre no puede estar vacío.");
        }
        else if (value.Length > ConfigurationLimits.MaxNameLength)
        {
            Error(issues, ValidationCodes.NameTooLong, path, $"El nombre supera los {ConfigurationLimits.MaxNameLength} caracteres.");
        }
    }

    private static string Label(string? id, int index) => string.IsNullOrWhiteSpace(id) ? $"#{index}" : id;

    private static void Error(List<ValidationIssue> issues, string code, string path, string message) =>
        issues.Add(new ValidationIssue(ValidationSeverity.Error, code, message, path));
}
