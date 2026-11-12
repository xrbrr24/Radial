using Radial.Core.Catalog;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Validation;

namespace Radial.Core.Editing;

/// <summary>
/// Edición de una configuración: la biblioteca de acciones, los menús, los grupos y las colocaciones. Trabaja sobre la
/// configuración inmutable y SOLO la sustituye cuando el resultado es válido: cada operación construye una candidata, regenera las
/// colocaciones enlazadas a partir de la biblioteca (una sola fuente de verdad) y la valida con
/// <see cref="ConfigurationValidator"/>; si algo falla devuelve un <see cref="EditResult"/> con el motivo y no cambia nada. Así el
/// editor no puede producir identificadores duplicados, referencias rotas, grupos huérfanos, ciclos, profundidades superiores al
/// límite ni dos elementos en el mismo sector. Es lógica pura, sin interfaz: la usan los modelos de vista y las pruebas.
///
/// Las acciones de la biblioteca nunca se borran por eliminar una colocación o un grupo; borrar una definición que está colocada
/// exige decirlo (<see cref="DeleteDefinition"/>). Eliminar un grupo con contenido exige <see cref="GroupDeletion.Cascade"/>.
/// </summary>
public sealed class ConfigurationEditor
{
    private RadialConfiguration _current;

    public ConfigurationEditor(RadialConfiguration initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _current = LibraryResolver.Resolve(initial);
    }

    /// <summary>La configuración actual (siempre válida si lo era la inicial).</summary>
    public RadialConfiguration Current => _current;

    /// <summary>Se produce después de cada edición que cambia la configuración.</summary>
    public event Action? Changed;

    // Consultas

    public RadialMenu? GetMenu(MenuId id) => _current.TryGetMenu(id, out var menu) ? menu : null;

    public PlacementRef? FindItem(ItemId id)
    {
        foreach (var menu in _current.Menus)
        {
            foreach (var item in menu.Items)
            {
                if (item.Id == id)
                {
                    return new PlacementRef(menu.Id, item);
                }
            }
        }

        return null;
    }

    /// <summary>Todas las colocaciones enlazadas a una definición, en el orden de los menús.</summary>
    public IReadOnlyList<PlacementRef> PlacementsOf(string definitionId) =>
        _current.Menus.SelectMany(m => m.Items.Where(i => i.DefinitionId == definitionId).Select(i => new PlacementRef(m.Id, i))).ToList();

    /// <summary>Profundidad de un menú: 1 el raíz, 2 un submenú de él...; 0 si no se alcanza desde la raíz.</summary>
    public int DepthOf(MenuId menu)
    {
        var depth = Depths();
        return depth.TryGetValue(menu, out var value) ? value : 0;
    }

    /// <summary>Niveles que ocupa un menú con todos sus submenús: 1 si no tiene ninguno.</summary>
    public int HeightOf(MenuId menu)
    {
        var height = 1;
        var level = new List<MenuId> { menu };
        var seen = new HashSet<MenuId> { menu };
        while (true)
        {
            var next = new List<MenuId>();
            foreach (var id in level)
            {
                foreach (var child in ChildMenus(id))
                {
                    if (seen.Add(child))
                    {
                        next.Add(child);
                    }
                }
            }

            if (next.Count == 0)
            {
                return height;
            }

            height++;
            level = next;
        }
    }

    /// <summary>La cadena de menús desde la raíz hasta <paramref name="menu"/>, por el primer grupo que lleva a cada uno. Vacía si no se alcanza.</summary>
    public IReadOnlyList<MenuId> PathTo(MenuId menu)
    {
        var path = new List<MenuId>();
        var seen = new HashSet<MenuId>();
        var cursor = menu;
        while (seen.Add(cursor))
        {
            path.Add(cursor);
            if (cursor == _current.RootMenuId)
            {
                path.Reverse();
                return path;
            }

            var parent = ParentGroupOf(cursor);
            if (parent is null)
            {
                return [];
            }

            cursor = parent.Menu;
        }

        return [];
    }

    /// <summary>El primer grupo que abre <paramref name="menu"/>, o <c>null</c> (el raíz).</summary>
    public PlacementRef? ParentGroupOf(MenuId menu)
    {
        foreach (var candidate in _current.Menus)
        {
            foreach (var item in candidate.Items)
            {
                if (item.Action is OpenMenuAction open && open.TargetMenuId == menu)
                {
                    return new PlacementRef(candidate.Id, item);
                }
            }
        }

        return null;
    }

    /// <summary>Qué contiene un grupo (submenús, elementos y grupos anidados), para avisar antes de eliminarlo.</summary>
    public GroupContents DescribeGroup(ItemId groupItem)
    {
        if (FindItem(groupItem) is not { Item.Action: OpenMenuAction open })
        {
            return new GroupContents(0, 0, 0);
        }

        var menus = new HashSet<MenuId> { open.TargetMenuId };
        menus.UnionWith(Descendants(open.TargetMenuId));
        var placements = 0;
        var groups = 0;
        foreach (var id in menus)
        {
            if (GetMenu(id) is { } menu)
            {
                placements += menu.Items.Count;
                groups += menu.Items.Count(i => i.IsGroup);
            }
        }

        return new GroupContents(menus.Count, placements, groups);
    }

    // Biblioteca

    /// <summary>Un identificador libre para una definición nueva con ese nombre (<c>custom.nombre</c>, <c>custom.nombre-2</c>...).</summary>
    public string NewDefinitionId(string name) =>
        LibraryResolver.UniqueId("custom." + LibraryResolver.Slug(name), _current.Actions.Select(a => a.Id).ToHashSet(StringComparer.Ordinal));

    public EditResult AddDefinition(ActionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (Problems(definition) is { } problem)
        {
            return EditResult.Fail(EditCodes.Invalid, problem);
        }

        if (_current.TryGetDefinition(definition.Id, out _))
        {
            return EditResult.Fail(EditCodes.Duplicate, $"Ya existe una acción con el identificador '{definition.Id}'.");
        }

        if (_current.Actions.Count >= ConfigurationLimits.MaxLibraryActions)
        {
            return EditResult.Fail(EditCodes.LibraryFull, $"La biblioteca ya tiene el máximo de {ConfigurationLimits.MaxLibraryActions} acciones.");
        }

        return Commit(_current with { Actions = [.. _current.Actions, definition] });
    }

    /// <summary>Sustituye la definición con ese identificador y regenera TODAS sus colocaciones, sin perder ninguna referencia.</summary>
    public EditResult UpdateDefinition(ActionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_current.TryGetDefinition(definition.Id, out _))
        {
            return EditResult.Fail(EditCodes.NotFound, $"La acción '{definition.Id}' no existe en la biblioteca.");
        }

        if (Problems(definition) is { } problem)
        {
            return EditResult.Fail(EditCodes.Invalid, problem);
        }

        return Commit(_current with { Actions = _current.Actions.Select(a => a.Id == definition.Id ? definition : a).ToList() });
    }

    /// <summary>
    /// Elimina una definición. Si está colocada y <paramref name="removePlacements"/> es falso, no hace nada y lo dice (con dónde);
    /// con <paramref name="removePlacements"/> se quitan también sus colocaciones.
    /// </summary>
    public EditResult DeleteDefinition(string definitionId, bool removePlacements)
    {
        if (!_current.TryGetDefinition(definitionId, out var definition))
        {
            return EditResult.Fail(EditCodes.NotFound, $"La acción '{definitionId}' no existe en la biblioteca.");
        }

        var placements = PlacementsOf(definitionId);
        if (placements.Count > 0 && !removePlacements)
        {
            var where = string.Join(", ", placements.Take(3).Select(p => $"«{GetMenu(p.Menu)?.Name ?? p.Menu.Value}»"));
            return EditResult.Fail(
                EditCodes.InUse,
                $"La acción «{definition.Name}» está colocada {placements.Count} vez/veces (en {where}{(placements.Count > 3 ? "…" : string.Empty)}). Quita antes sus colocaciones o confirma que se eliminen también.");
        }

        var menus = _current.Menus.Select(m => m with { Items = m.Items.Where(i => i.DefinitionId != definitionId).ToList() }).ToList();
        return Commit(_current with { Actions = _current.Actions.Where(a => a.Id != definitionId).ToList(), Menus = menus });
    }

    /// <summary>Crea una copia de la definición con otro identificador y el nombre «X (copia)». No se coloca en ningún menú.</summary>
    public EditResult DuplicateDefinition(string definitionId, out ActionDefinition? copy)
    {
        copy = null;
        if (!_current.TryGetDefinition(definitionId, out var original))
        {
            return EditResult.Fail(EditCodes.NotFound, $"La acción '{definitionId}' no existe en la biblioteca.");
        }

        var used = _current.Actions.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var name = $"{original.Name} (copia)";
        var candidate = original with
        {
            Id = LibraryResolver.UniqueId(original.Id + "-copia", used),
            Name = name.Length > ConfigurationLimits.MaxNameLength ? name[..ConfigurationLimits.MaxNameLength] : name,
            IsBuiltIn = false,
        };

        var result = AddDefinition(candidate);
        if (result.Success)
        {
            copy = candidate;
        }

        return result;
    }

    /// <summary>Vuelve a añadir las definiciones de fábrica que faltan (por id). Devuelve cuántas se añadieron.</summary>
    public int RestoreFactory(IEnumerable<ActionDefinition> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var missing = factory.Where(d => !_current.TryGetDefinition(d.Id, out _) && !d.GetProblems().Any()).ToList();
        if (missing.Count == 0 || _current.Actions.Count + missing.Count > ConfigurationLimits.MaxLibraryActions)
        {
            return 0;
        }

        return Commit(_current with { Actions = [.. _current.Actions, .. missing] }).Success ? missing.Count : 0;
    }

    // Colocaciones

    /// <summary>Coloca una definición de la biblioteca en un sector libre de un menú.</summary>
    public EditResult PlaceDefinition(MenuId menuId, RadialDirection position, string definitionId, ItemId? itemId = null)
    {
        if (GetMenu(menuId) is not { } menu)
        {
            return EditResult.Fail(EditCodes.NotFound, $"El menú '{menuId}' no existe.");
        }

        if (!_current.TryGetDefinition(definitionId, out var definition))
        {
            return EditResult.Fail(EditCodes.NotFound, $"La acción '{definitionId}' no existe en la biblioteca.");
        }

        if (definition.Action is UnknownAction)
        {
            return EditResult.Fail(EditCodes.NotAllowed, $"La acción «{definition.Name}» no se puede usar (es de un tipo que esta versión no conoce o no es válida) y no se puede colocar.");
        }

        if (OccupiedMessage(menu, position, null) is { } occupied)
        {
            return EditResult.Fail(EditCodes.Occupied, occupied);
        }

        var id = itemId ?? new ItemId(LibraryResolver.UniqueId($"{menuId.Value}.{definition.Id}", AllItemIds()));
        if (itemId is not null && AllItemIds().Contains(itemId.Value.Value))
        {
            return EditResult.Fail(EditCodes.Duplicate, $"Ya existe un elemento con el identificador '{itemId}'.");
        }

        return Commit(WithItems(menuId, [.. menu.Items, definition.ToItem(id, position)]));
    }

    /// <summary>Quita una colocación (no una definición ni un grupo: para los grupos, <see cref="DeleteGroup"/>).</summary>
    public EditResult RemovePlacement(ItemId itemId)
    {
        if (FindItem(itemId) is not { } found)
        {
            return EditResult.Fail(EditCodes.NotFound, "El elemento no existe.");
        }

        if (found.Item.IsGroup)
        {
            return EditResult.Fail(EditCodes.NotAllowed, "Un grupo se elimina con «Eliminar grupo», que avisa de su contenido.");
        }

        return Commit(WithItems(found.Menu, GetMenu(found.Menu)!.Items.Where(i => i.Id != itemId).ToList()));
    }

    /// <summary>
    /// Mueve un elemento (acción o grupo) a otro sector, del mismo menú o de otro. Si el sector de destino está ocupado, solo se puede
    /// intercambiar (<paramref name="swap"/>) dentro del mismo menú. Un grupo no puede moverse dentro de sí mismo ni superar la
    /// profundidad máxima.
    /// </summary>
    public EditResult MoveItem(ItemId itemId, MenuId toMenu, RadialDirection toPosition, bool swap = false)
    {
        if (FindItem(itemId) is not { } found)
        {
            return EditResult.Fail(EditCodes.NotFound, "El elemento no existe.");
        }

        if (GetMenu(toMenu) is not { } destination)
        {
            return EditResult.Fail(EditCodes.NotFound, $"El menú '{toMenu}' no existe.");
        }

        if (found.Menu == toMenu && found.Item.Position == toPosition)
        {
            return EditResult.Ok;
        }

        var occupant = destination.GetItem(toPosition);
        if (occupant is not null)
        {
            if (!swap)
            {
                return EditResult.Fail(EditCodes.Occupied, OccupiedMessage(destination, toPosition, null)!);
            }

            if (found.Menu != toMenu)
            {
                return EditResult.Fail(EditCodes.NotAllowed, "Solo se pueden intercambiar dos sectores del mismo menú.");
            }

            var swapped = destination.Items
                .Select(i => i.Id == itemId ? i with { Position = toPosition } : i.Id == occupant.Id ? i with { Position = found.Item.Position } : i)
                .ToList();
            return Commit(WithItems(toMenu, swapped));
        }

        if (found.Item.IsGroup && found.Menu != toMenu)
        {
            var target = ((OpenMenuAction)found.Item.Action).TargetMenuId;
            if (toMenu == target || Descendants(target).Contains(toMenu))
            {
                return EditResult.Fail(EditCodes.Cycle, "Un grupo no se puede mover dentro de sí mismo ni de sus submenús.");
            }

            if (DepthOf(toMenu) + HeightOf(target) > _current.Navigation.MaxDepth)
            {
                return EditResult.Fail(EditCodes.TooDeep, $"Al mover ahí el grupo se superaría la profundidad máxima ({_current.Navigation.MaxDepth}).");
            }
        }

        var moved = found.Item with { Position = toPosition };
        if (found.Menu == toMenu)
        {
            return Commit(WithItems(toMenu, destination.Items.Select(i => i.Id == itemId ? moved : i).ToList()));
        }

        var candidate = WithItems(found.Menu, GetMenu(found.Menu)!.Items.Where(i => i.Id != itemId).ToList());
        candidate = candidate with
        {
            Menus = candidate.Menus.Select(m => m.Id == toMenu ? m with { Items = [.. m.Items, moved] } : m).ToList(),
        };
        return Commit(candidate);
    }

    /// <summary>Copia una colocación (con sus ajustes propios) a otro sector libre del mismo menú. Los grupos no se duplican.</summary>
    public EditResult DuplicatePlacement(ItemId itemId, RadialDirection toPosition)
    {
        if (FindItem(itemId) is not { } found)
        {
            return EditResult.Fail(EditCodes.NotFound, "El elemento no existe.");
        }

        if (found.Item.IsGroup || found.Item.IsUnsupported)
        {
            return EditResult.Fail(EditCodes.NotAllowed, found.Item.IsGroup ? "Un grupo no se puede duplicar." : "Una acción de tipo desconocido no se puede duplicar.");
        }

        var menu = GetMenu(found.Menu)!;
        if (OccupiedMessage(menu, toPosition, null) is { } occupied)
        {
            return EditResult.Fail(EditCodes.Occupied, occupied);
        }

        var id = new ItemId(LibraryResolver.UniqueId(itemId.Value + "-copia", AllItemIds()));
        return Commit(WithItems(found.Menu, [.. menu.Items, found.Item with { Id = id, Position = toPosition }]));
    }

    /// <summary>Sustituye la acción de una colocación por otra de la biblioteca; se pierden sus ajustes propios (nombre e icono).</summary>
    public EditResult ReplaceAction(ItemId itemId, string definitionId)
    {
        if (FindItem(itemId) is not { } found)
        {
            return EditResult.Fail(EditCodes.NotFound, "El elemento no existe.");
        }

        if (found.Item.IsGroup)
        {
            return EditResult.Fail(EditCodes.NotAllowed, "Un grupo no tiene acción: conviértelo en acción para asignarle una.");
        }

        if (!_current.TryGetDefinition(definitionId, out var definition) || definition.Action is UnknownAction)
        {
            return EditResult.Fail(EditCodes.NotFound, $"La acción '{definitionId}' no existe en la biblioteca o no se puede colocar.");
        }

        var replacement = definition.ToItem(itemId, found.Item.Position);
        return Commit(WithItems(found.Menu, GetMenu(found.Menu)!.Items.Select(i => i.Id == itemId ? replacement : i).ToList()));
    }

    /// <summary>Cambia el nombre que se ve en ESTA colocación (<c>null</c> o vacío = el de la acción). En un grupo, lo renombra.</summary>
    public EditResult SetLabel(ItemId itemId, string? label)
    {
        if (FindItem(itemId) is not { } found)
        {
            return EditResult.Fail(EditCodes.NotFound, "El elemento no existe.");
        }

        if (found.Item.IsGroup)
        {
            return RenameGroup(itemId, label ?? string.Empty);
        }

        var text = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        return Commit(WithItems(found.Menu, GetMenu(found.Menu)!.Items.Select(i => i.Id == itemId ? i with { LabelOverride = text } : i).ToList()));
    }

    /// <summary>Cambia el icono de ESTA colocación (<c>null</c> = el de la acción). En un grupo, su icono.</summary>
    public EditResult SetIcon(ItemId itemId, IconReference? icon)
    {
        if (FindItem(itemId) is not { } found)
        {
            return EditResult.Fail(EditCodes.NotFound, "El elemento no existe.");
        }

        var items = GetMenu(found.Menu)!.Items.Select(i =>
            i.Id != itemId ? i : i.IsGroup ? i with { Icon = icon ?? new IconReference(IconSource.Glyph, "Apps") } : i with { IconOverride = icon }).ToList();
        return Commit(WithItems(found.Menu, items));
    }

    // Grupos

    /// <summary>Crea un grupo (y su submenú, vacío) en un sector libre. Respeta la profundidad máxima.</summary>
    public EditResult CreateGroup(MenuId parentMenu, RadialDirection position, string name, IconReference? icon = null)
    {
        if (GetMenu(parentMenu) is not { } parent)
        {
            return EditResult.Fail(EditCodes.NotFound, $"El menú '{parentMenu}' no existe.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > ConfigurationLimits.MaxNameLength)
        {
            return EditResult.Fail(EditCodes.Invalid, $"El nombre del grupo es obligatorio y tiene como máximo {ConfigurationLimits.MaxNameLength} caracteres.");
        }

        if (OccupiedMessage(parent, position, null) is { } occupied)
        {
            return EditResult.Fail(EditCodes.Occupied, occupied);
        }

        if (DepthOf(parentMenu) + 1 > _current.Navigation.MaxDepth)
        {
            return EditResult.Fail(EditCodes.TooDeep, $"No se puede crear un grupo aquí: se superaría la profundidad máxima ({_current.Navigation.MaxDepth}). Puedes aumentarla en Ajustes.");
        }

        var trimmed = name.Trim();
        var menuId = new MenuId(LibraryResolver.UniqueId("menu." + LibraryResolver.Slug(trimmed), _current.Menus.Select(m => m.Id.Value).ToHashSet(StringComparer.Ordinal)));
        var itemId = new ItemId(LibraryResolver.UniqueId($"{parentMenu.Value}.{menuId.Value}", AllItemIds()));
        var group = new RadialItem(itemId, trimmed, position, new OpenMenuAction(menuId), icon ?? new IconReference(IconSource.Glyph, "Apps"));

        var candidate = WithItems(parentMenu, [.. parent.Items, group]);
        return Commit(candidate with { Menus = [.. candidate.Menus, new RadialMenu(menuId, trimmed, [])] });
    }

    /// <summary>Renombra un grupo y su submenú.</summary>
    public EditResult RenameGroup(ItemId groupItem, string name)
    {
        if (FindItem(groupItem) is not { Item: { Action: OpenMenuAction open } } found)
        {
            return EditResult.Fail(EditCodes.NotFound, "El grupo no existe.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > ConfigurationLimits.MaxNameLength)
        {
            return EditResult.Fail(EditCodes.Invalid, $"El nombre del grupo es obligatorio y tiene como máximo {ConfigurationLimits.MaxNameLength} caracteres.");
        }

        var trimmed = name.Trim();
        var candidate = WithItems(found.Menu, GetMenu(found.Menu)!.Items.Select(i => i.Id == groupItem ? i with { Name = trimmed } : i).ToList());
        return Commit(candidate with { Menus = candidate.Menus.Select(m => m.Id == open.TargetMenuId ? m with { Name = trimmed } : m).ToList() });
    }

    public EditResult RenameRootMenu(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > ConfigurationLimits.MaxNameLength)
        {
            return EditResult.Fail(EditCodes.Invalid, $"El nombre es obligatorio y tiene como máximo {ConfigurationLimits.MaxNameLength} caracteres.");
        }

        return Commit(_current with { Menus = _current.Menus.Select(m => m.Id == _current.RootMenuId ? m with { Name = name.Trim() } : m).ToList() });
    }

    /// <summary>
    /// Elimina un grupo. Con <see cref="GroupDeletion.RefuseIfNotEmpty"/> solo si su submenú está vacío. Con
    /// <see cref="GroupDeletion.Cascade"/> elimina también los submenús que solo dependían de él (un submenú que otro grupo sigue
    /// usando se conserva). Las acciones de la biblioteca no se tocan: solo desaparecen sus colocaciones dentro de lo eliminado.
    /// </summary>
    public EditResult DeleteGroup(ItemId groupItem, GroupDeletion mode)
    {
        if (FindItem(groupItem) is not { Item: { Action: OpenMenuAction open } } found)
        {
            return EditResult.Fail(EditCodes.NotFound, "El grupo no existe.");
        }

        var target = GetMenu(open.TargetMenuId);
        if (mode == GroupDeletion.RefuseIfNotEmpty && target is { Items.Count: > 0 })
        {
            var contents = DescribeGroup(groupItem);
            return EditResult.Fail(
                EditCodes.GroupNotEmpty,
                $"El grupo «{found.Item.Name}» contiene {contents.Placements} elemento(s) en {contents.Menus} menú(s). Elimínalo con su contenido si es lo que quieres: las acciones seguirán en la biblioteca.");
        }

        var scope = new HashSet<MenuId> { open.TargetMenuId };
        scope.UnionWith(Descendants(open.TargetMenuId));
        var doomed = new HashSet<MenuId>();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var menuId in scope)
            {
                if (doomed.Contains(menuId) || menuId == _current.RootMenuId)
                {
                    continue;
                }

                var referrers = _current.Menus.SelectMany(m => m.Items.Where(i => i.Action is OpenMenuAction o && o.TargetMenuId == menuId).Select(i => (Menu: m.Id, Item: i)));
                if (referrers.All(r => r.Item.Id == groupItem || doomed.Contains(r.Menu)))
                {
                    doomed.Add(menuId);
                    changed = true;
                }
            }
        }

        var menus = _current.Menus
            .Where(m => !doomed.Contains(m.Id))
            .Select(m => m.Id == found.Menu ? m with { Items = m.Items.Where(i => i.Id != groupItem).ToList() } : m)
            .ToList();
        return Commit(_current with { Menus = menus });
    }

    /// <summary>Convierte una colocación de acción en un grupo vacío (en el mismo sector). La acción sigue en la biblioteca.</summary>
    public EditResult ConvertToGroup(ItemId itemId, string name)
    {
        if (FindItem(itemId) is not { } found)
        {
            return EditResult.Fail(EditCodes.NotFound, "El elemento no existe.");
        }

        if (found.Item.IsGroup)
        {
            return EditResult.Fail(EditCodes.NotAllowed, "El elemento ya es un grupo.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > ConfigurationLimits.MaxNameLength)
        {
            return EditResult.Fail(EditCodes.Invalid, $"El nombre del grupo es obligatorio y tiene como máximo {ConfigurationLimits.MaxNameLength} caracteres.");
        }

        if (DepthOf(found.Menu) + 1 > _current.Navigation.MaxDepth)
        {
            return EditResult.Fail(EditCodes.TooDeep, $"No se puede crear un grupo aquí: se superaría la profundidad máxima ({_current.Navigation.MaxDepth}).");
        }

        var trimmed = name.Trim();
        var menuId = new MenuId(LibraryResolver.UniqueId("menu." + LibraryResolver.Slug(trimmed), _current.Menus.Select(m => m.Id.Value).ToHashSet(StringComparer.Ordinal)));
        var group = new RadialItem(itemId, trimmed, found.Item.Position, new OpenMenuAction(menuId), new IconReference(IconSource.Glyph, "Apps"));
        var candidate = WithItems(found.Menu, GetMenu(found.Menu)!.Items.Select(i => i.Id == itemId ? group : i).ToList());
        return Commit(candidate with { Menus = [.. candidate.Menus, new RadialMenu(menuId, trimmed, [])] });
    }

    /// <summary>
    /// Convierte un grupo en una colocación de acción de la biblioteca (en el mismo sector). Si el grupo tiene contenido, exige
    /// <paramref name="deleteContents"/>.
    /// </summary>
    public EditResult ConvertGroupToAction(ItemId groupItem, string definitionId, bool deleteContents)
    {
        if (FindItem(groupItem) is not { Item: { Action: OpenMenuAction } } found)
        {
            return EditResult.Fail(EditCodes.NotFound, "El grupo no existe.");
        }

        if (!_current.TryGetDefinition(definitionId, out var definition) || definition.Action is UnknownAction)
        {
            return EditResult.Fail(EditCodes.NotFound, $"La acción '{definitionId}' no existe en la biblioteca o no se puede colocar.");
        }

        var before = _current;
        var deletion = DeleteGroup(groupItem, deleteContents ? GroupDeletion.Cascade : GroupDeletion.RefuseIfNotEmpty);
        if (!deletion.Success)
        {
            return deletion;
        }

        var placement = PlaceDefinition(found.Menu, found.Item.Position, definitionId, groupItem);
        if (!placement.Success)
        {
            _current = before; // no se deja a medias
            return placement;
        }

        return EditResult.Ok;
    }

    // Ajustes de navegación y geometría

    public EditResult SetNavigation(NavigationSettings navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        return Commit(_current with { Navigation = navigation });
    }

    public EditResult SetGeometry(GeometrySettings geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return Commit(_current with { Geometry = geometry });
    }

    // Interno

    private EditResult Commit(RadialConfiguration candidate)
    {
        candidate = LibraryResolver.Resolve(candidate);
        var validation = ConfigurationValidator.Validate(candidate);
        if (!validation.IsValid)
        {
            return EditResult.Fail(EditCodes.Invalid, validation.Errors.First().Message);
        }

        _current = candidate;
        Changed?.Invoke();
        return EditResult.Ok;
    }

    private RadialConfiguration WithItems(MenuId menuId, IReadOnlyList<RadialItem> items) =>
        _current with { Menus = _current.Menus.Select(m => m.Id == menuId ? m with { Items = items } : m).ToList() };

    private HashSet<string> AllItemIds() => _current.Menus.SelectMany(m => m.Items).Select(i => i.Id.Value).ToHashSet(StringComparer.Ordinal);

    private static string? Problems(ActionDefinition definition)
    {
        var problems = definition.GetProblems().ToList();
        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    private static string? OccupiedMessage(RadialMenu menu, RadialDirection position, ItemId? ignore)
    {
        var occupant = menu.GetItem(position);
        return occupant is not null && occupant.Id != ignore
            ? $"El sector {DirectionNames.Spanish(position)} ya está ocupado por «{occupant.Name}»."
            : null;
    }

    private IEnumerable<MenuId> ChildMenus(MenuId menu) =>
        GetMenu(menu)?.Items.Where(i => i.Action is OpenMenuAction).Select(i => ((OpenMenuAction)i.Action).TargetMenuId) ?? [];

    private HashSet<MenuId> Descendants(MenuId start)
    {
        var found = new HashSet<MenuId>();
        var pending = new Stack<MenuId>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            foreach (var child in ChildMenus(pending.Pop()))
            {
                if (child != start && found.Add(child))
                {
                    pending.Push(child);
                }
            }
        }

        return found;
    }

    private Dictionary<MenuId, int> Depths()
    {
        var depths = new Dictionary<MenuId, int> { [_current.RootMenuId] = 1 };
        var queue = new Queue<MenuId>();
        queue.Enqueue(_current.RootMenuId);
        while (queue.Count > 0)
        {
            var menu = queue.Dequeue();
            foreach (var child in ChildMenus(menu))
            {
                if (depths.TryAdd(child, depths[menu] + 1))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return depths;
    }
}
