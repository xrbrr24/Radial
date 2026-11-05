namespace Radial.Core.Validation;

public static class ValidationCodes
{
    public const string RootEmpty = "root.empty";
    public const string RootMissing = "root.missing";

    public const string IdEmpty = "id.empty";
    public const string IdTooLong = "id.too-long";
    public const string MenuDuplicateId = "menu.duplicate-id";
    public const string ItemDuplicateId = "item.duplicate-id";

    public const string NameEmpty = "name.empty";
    public const string NameTooLong = "name.too-long";

    public const string ItemPositionInvalid = "item.position-invalid";
    public const string ItemPositionDuplicate = "item.position-duplicate";
    public const string IconInvalid = "icon.invalid";

    public const string ActionMissing = "action.missing";
    public const string ActionInvalid = "action.invalid";
    public const string ActionMenuMissing = "action.menu-missing";

    /// <summary>Aviso: el tipo de acción es desconocido para esta versión; se conserva pero no se ejecuta.</summary>
    public const string ActionUnknownType = "action.unknown-type";

    public const string ItemDefinitionIdInvalid = "item.definition-id-invalid";

    /// <summary>Una colocación referencia una definición que no está en la biblioteca.</summary>
    public const string ItemDefinitionMissing = "item.definition-missing";

    /// <summary>La vista materializada de una colocación enlazada no coincide con su definición (dos fuentes de verdad contradictorias).</summary>
    public const string ItemDefinitionConflict = "item.definition-conflict";

    /// <summary>Un grupo (estructura del menú) no puede estar enlazado a una definición.</summary>
    public const string ItemGroupWithDefinition = "item.group-with-definition";

    public const string LibraryDuplicateId = "library.duplicate-id";
    public const string LibraryDefinitionInvalid = "library.definition-invalid";
    public const string LibraryTooLarge = "library.too-large";

    public const string MenuCycle = "menu.cycle";
    public const string MenuTooDeep = "menu.too-deep";
    public const string MenuUnreachable = "menu.unreachable";

    public const string GeometryDeadZoneInvalid = "geometry.dead-zone-invalid";
    public const string GeometryHysteresisInvalid = "geometry.hysteresis-invalid";

    public const string NavigationSubmenuDwellInvalid = "navigation.submenu-dwell-invalid";
    public const string NavigationBackDwellInvalid = "navigation.back-dwell-invalid";
    public const string NavigationMaxDepthInvalid = "navigation.max-depth-invalid";
}
