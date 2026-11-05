using Radial.Core.Catalog;
using Radial.Core.Editing;
using Radial.Core.Geometry;
using Radial.Core.Model;

namespace Radial.Windows.Presentation;

/// <summary>Un menú (el raíz o un grupo) en el árbol de jerarquía del editor.</summary>
public sealed class TreeNodeViewModel : ObservableObject
{
    private readonly Action<TreeNodeViewModel> _onSelected;
    private readonly Action<TreeNodeViewModel, bool> _onExpanded;
    private bool _isSelected;
    private bool _isExpanded;

    public TreeNodeViewModel(MenuId menuId, ItemId? groupItemId, string name, string iconName, int depth, int itemCount, Action<TreeNodeViewModel> onSelected, Action<TreeNodeViewModel, bool> onExpanded)
    {
        MenuId = menuId;
        GroupItemId = groupItemId;
        Name = name;
        IconName = iconName;
        Depth = depth;
        ItemCount = itemCount;
        _onSelected = onSelected;
        _onExpanded = onExpanded;
    }

    public MenuId MenuId { get; }

    /// <summary>El elemento-grupo del menú padre que abre este menú; <c>null</c> en el raíz.</summary>
    public ItemId? GroupItemId { get; }

    public string Name { get; }

    public string IconName { get; }

    /// <summary>1 para el menú raíz.</summary>
    public int Depth { get; }

    public int ItemCount { get; }

    public bool IsRoot => GroupItemId is null;

    public string CountText => ItemCount == 1 ? "1 elemento" : $"{ItemCount} elementos";

    public List<TreeNodeViewModel> Children { get; } = [];

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value) && value)
            {
                _onSelected(this);
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value))
            {
                _onExpanded(this, value);
            }
        }
    }

    /// <summary>Marca la selección sin avisar al editor (se usa al reconstruir el árbol).</summary>
    internal void SetSelectedSilently(bool selected)
    {
        _isSelected = selected;
        Raise(nameof(IsSelected));
    }

    internal void SetExpandedSilently(bool expanded)
    {
        _isExpanded = expanded;
        Raise(nameof(IsExpanded));
    }
}

/// <summary>Uno de los ocho sectores del menú que se está editando.</summary>
public sealed class SectorViewModel : ObservableObject
{
    public SectorViewModel(RadialDirection direction, RadialItem? item, string description, bool isSelected)
    {
        Direction = direction;
        Item = item;
        DirectionName = UiText.Direction(direction);
        IsSelected = isSelected;
        IsEmpty = item is null;
        IsGroup = item?.IsGroup ?? false;
        IsUnsupported = item?.IsUnsupported ?? false;
        Title = item?.Name ?? "Vacío";
        Subtitle = item is null ? "Sector libre" : description;
        IconName = item is null ? string.Empty : item.Icon is { Source: IconSource.Glyph } icon ? icon.Value : "Apps";
    }

    public RadialDirection Direction { get; }

    public RadialItem? Item { get; }

    public string DirectionName { get; }

    public bool IsSelected { get; }

    public bool IsEmpty { get; }

    public bool IsGroup { get; }

    public bool IsUnsupported { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string IconName { get; }
}

/// <summary>Un tramo del camino desde el menú raíz hasta el menú que se edita (para el recorrido clicable).</summary>
public sealed record PathSegment(MenuId MenuId, string Name, bool IsCurrent);

/// <summary>
/// El editor radial: árbol de la jerarquía, los ocho sectores del menú abierto, el inspector del sector elegido y todas las
/// operaciones de colocación y de grupos. No dibuja nada ni usa WPF: la vista dibuja el MISMO radial que el overlay a partir de
/// <see cref="CurrentMenu"/> y avisa por <see cref="PreviewInvalidated"/> de cuándo redibujarlo. No activa ningún hook ni ejecuta
/// ninguna acción. Las reglas (ids únicos, ciclos, profundidad, sector ocupado, referencias rotas) las impone
/// <see cref="ConfigurationEditor"/>: este modelo solo traduce su resultado a mensajes para el usuario.
/// </summary>
public sealed class EditorViewModel : ObservableObject
{
    private static readonly RadialDirection[] Directions = Enum.GetValues<RadialDirection>();

    private readonly ConfigurationSession _session;
    private readonly UiServices _services;
    private readonly HashSet<MenuId> _collapsed = [];
    private MenuId _currentMenuId;
    private RadialDirection? _selected;
    private IReadOnlyList<TreeNodeViewModel> _tree = [];
    private IReadOnlyList<SectorViewModel> _sectors = [];
    private IReadOnlyList<PathSegment> _path = [];
    private IReadOnlyList<LibraryRowViewModel> _pickerResults = [];
    private string _pickerSearch = string.Empty;
    private string _labelText = string.Empty;
    private string _status = string.Empty;
    private bool _statusIsError;
    private bool _rebuilding;
    private bool _operating;

    public EditorViewModel(ConfigurationSession session, UiServices services)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(services);
        _session = session;
        _services = services;
        _currentMenuId = session.Current.RootMenuId;

        Up = new RelayCommand(GoUp, () => _path.Count > 1);
        Enter = new RelayCommand(EnterSelectedGroup, () => SelectedItem is { IsGroup: true });
        CreateGroup = new RelayCommand(CreateGroupHere, () => FirstFreeDirection() is not null);
        Duplicate = new RelayCommand(DuplicateSelected, () => SelectedItem is { IsUnsupported: false } && FirstFreeDirection() is not null);
        Remove = new AsyncCommand(RemoveSelectedAsync, () => SelectedItem is not null);
        CommitLabel = new RelayCommand(() => CommitLabelText(), () => SelectedItem is not null);
        ResetLabel = new RelayCommand(ResetLabelOverride, () => SelectedItem is { IsLinked: true, LabelOverride: not null });
        ConvertToGroup = new RelayCommand(ConvertSelectedToGroup, () => SelectedItem is { IsGroup: false, IsUnsupported: false });
        Place = new RelayCommand(parameter =>
        {
            if (parameter is string id)
            {
                PlaceDefinition(id);
            }
        });

        _session.Changed += OnSessionChanged;
        Rebuild();
    }

    /// <summary>Hay que redibujar el radial de la vista previa (cambió el menú, un sector, la geometría o la selección).</summary>
    public event Action? PreviewInvalidated;

    public RelayCommand Up { get; }

    public RelayCommand Enter { get; }

    public RelayCommand CreateGroup { get; }

    public RelayCommand Duplicate { get; }

    public AsyncCommand Remove { get; }

    public RelayCommand CommitLabel { get; }

    public RelayCommand ResetLabel { get; }

    public RelayCommand ConvertToGroup { get; }

    public RelayCommand Place { get; }

    // ───────────────────────── Estado ─────────────────────────

    public RadialConfiguration Configuration => _session.Current;

    public MenuId CurrentMenuId => _currentMenuId;

    public RadialMenu CurrentMenu => _session.Current.TryGetMenu(_currentMenuId, out var menu) ? menu : _session.Current.Menus[0];

    /// <summary>Menús apilados hasta el abierto (1 = raíz); la vista previa lo usa para dibujar la flecha de volver.</summary>
    public int Depth => Math.Max(1, _path.Count);

    public GeometrySettings Geometry => _session.Current.Geometry;

    /// <summary>El árbol, con el menú raíz como único nodo de primer nivel.</summary>
    public IReadOnlyList<TreeNodeViewModel> Tree
    {
        get => _tree;
        private set => Set(ref _tree, value);
    }

    public IReadOnlyList<SectorViewModel> Sectors
    {
        get => _sectors;
        private set => Set(ref _sectors, value);
    }

    public IReadOnlyList<PathSegment> Path
    {
        get => _path;
        private set => Set(ref _path, value);
    }

    public string MenuTitle => CurrentMenu.Name;

    public int MaxDepth => _session.Current.Navigation.MaxDepth;

    public RadialDirection? SelectedDirection
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                SelectionChanged();
            }
        }
    }

    public RadialItem? SelectedItem => _selected is { } direction ? CurrentMenu.GetItem(direction) : null;

    public bool HasSelection => _selected is not null;

    public bool IsEmptySectorSelected => _selected is not null && SelectedItem is null;

    public bool IsItemSelected => SelectedItem is not null;

    public bool IsGroupSelected => SelectedItem is { IsGroup: true };

    public bool IsActionSelected => SelectedItem is { IsGroup: false };

    public bool IsUnsupportedSelected => SelectedItem is { IsUnsupported: true };

    // ───────────────────────── Inspector ─────────────────────────

    public string SelectedDirectionName => _selected is { } d ? UiText.Direction(d) : string.Empty;

    /// <summary>El nombre editable del sector (el de la colocación; en un grupo, el del grupo).</summary>
    public string LabelText
    {
        get => _labelText;
        set => Set(ref _labelText, value);
    }

    public string InspectorKind => SelectedItem switch
    {
        null => string.Empty,
        { IsGroup: true } => "Grupo",
        { } item => UiText.KindName(ActionKinds.Of(item.Action)),
    };

    public string InspectorSummary => SelectedItem is { } item ? UiText.Summary(item.Action, _services.Labels.GetLabel) : string.Empty;

    public string InspectorCategory => Definition is { } d ? UiText.CategoryName(d.Category) : string.Empty;

    public string InspectorDescription => Definition?.Description ?? string.Empty;

    public string InspectorDefinitionName => Definition?.Name ?? string.Empty;

    public string InspectorIconName => SelectedItem is { } item
        ? (item.IconOverride ?? item.Icon) is { Source: IconSource.Glyph } icon ? icon.Value : "Document"
        : string.Empty;

    /// <summary>Cuántas veces más está colocada la misma acción en otros sectores.</summary>
    public string InspectorUsage
    {
        get
        {
            if (SelectedItem is not { DefinitionId: { } id })
            {
                return string.Empty;
            }

            var total = _session.Editor.PlacementsOf(id).Count;
            return total <= 1 ? "Solo se usa en este sector." : $"Está colocada en {total} sectores en total.";
        }
    }

    public string GroupContentsText
    {
        get
        {
            if (SelectedItem is not { IsGroup: true } group)
            {
                return string.Empty;
            }

            var contents = _session.Editor.DescribeGroup(group.Id);
            return contents.Placements == 0
                ? "El grupo está vacío."
                : $"Contiene {contents.Placements} elemento(s)" + (contents.Groups > 0 ? $", {contents.Groups} grupo(s) anidado(s)." : ".");
        }
    }

    public bool HasLabelOverride => SelectedItem is { IsLinked: true, LabelOverride: not null };

    private ActionDefinition? Definition =>
        SelectedItem is { DefinitionId: { } id } && _session.Current.TryGetDefinition(id, out var d) ? d : null;

    // ───────────────────────── Selector de acciones para un sector vacío ─────────────────────────

    public string PickerSearch
    {
        get => _pickerSearch;
        set
        {
            if (Set(ref _pickerSearch, value ?? string.Empty))
            {
                RefreshPicker();
            }
        }
    }

    public IReadOnlyList<LibraryRowViewModel> PickerResults
    {
        get => _pickerResults;
        private set => Set(ref _pickerResults, value);
    }

    // ───────────────────────── Mensajes ─────────────────────────

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => Set(ref _statusIsError, value);
    }

    // ───────────────────────── Navegación ─────────────────────────

    /// <summary>Abre un menú (el raíz o un grupo) para editarlo. Un menú que no existe se ignora.</summary>
    public void OpenMenu(MenuId menuId)
    {
        if (menuId == _currentMenuId || !_session.Current.TryGetMenu(menuId, out _))
        {
            return;
        }

        _currentMenuId = menuId;
        _selected = null;
        Rebuild();
    }

    public void GoUp()
    {
        if (_path.Count > 1)
        {
            OpenMenu(_path[^2].MenuId);
        }
    }

    public void EnterSelectedGroup()
    {
        if (SelectedItem is { Action: OpenMenuAction open })
        {
            OpenMenu(open.TargetMenuId);
        }
    }

    /// <summary>Elige un sector (<c>null</c> deja la selección vacía). Elegir el que ya estaba lo mantiene.</summary>
    public void SelectDirection(RadialDirection? direction) => SelectedDirection = direction;

    // ───────────────────────── Colocar y mover ─────────────────────────

    /// <summary>
    /// Coloca una acción de la biblioteca en un sector del menú abierto: el indicado, o el elegido si está libre, o el primero libre.
    /// Si el sector indicado está ocupado no se pisa nada.
    /// </summary>
    public EditResult PlaceDefinition(string definitionId, RadialDirection? target = null)
    {
        var position = target ?? (_selected is { } s && CurrentMenu.GetItem(s) is null ? s : FirstFreeDirection());
        if (position is null)
        {
            return Report(EditResult.Fail(EditCodes.Occupied, "El menú tiene los ocho sectores ocupados. Libera uno para colocar la acción."));
        }

        var result = Apply(e => e.PlaceDefinition(_currentMenuId, position.Value, definitionId));
        if (result.Success)
        {
            var name = _session.Current.TryGetDefinition(definitionId, out var d) ? d.Name : definitionId;
            _selected = position;
            Rebuild();
            Say($"«{name}» colocada en {UiText.Direction(position.Value).ToLowerInvariant()}.");
        }
        else
        {
            Report(result);
        }

        return result;
    }

    /// <summary>Mueve el elemento de un sector a otro del mismo menú; si el destino está ocupado, los intercambia.</summary>
    public EditResult MoveSector(RadialDirection from, RadialDirection to)
    {
        if (from == to)
        {
            return EditResult.Ok;
        }

        if (CurrentMenu.GetItem(from) is not { } item)
        {
            return Report(EditResult.Fail(EditCodes.NotFound, "Ese sector está vacío: no hay nada que mover."));
        }

        var result = Apply(e => e.MoveItem(item.Id, _currentMenuId, to, swap: true));
        if (result.Success)
        {
            _selected = to;
            Rebuild();
            Say($"«{item.Name}» movida a {UiText.Direction(to).ToLowerInvariant()}.");
        }
        else
        {
            Report(result);
        }

        return result;
    }

    /// <summary>Mueve un elemento (acción o grupo) a otro menú, al primer sector libre de este.</summary>
    public EditResult MoveToMenu(ItemId itemId, MenuId targetMenu)
    {
        if (_session.Editor.FindItem(itemId) is not { } found)
        {
            return Report(EditResult.Fail(EditCodes.NotFound, "El elemento ya no existe."));
        }

        if (found.Menu == targetMenu)
        {
            return EditResult.Ok;
        }

        if (_session.Editor.GetMenu(targetMenu) is not { } menu)
        {
            return Report(EditResult.Fail(EditCodes.NotFound, "El menú de destino ya no existe."));
        }

        var free = Directions.Cast<RadialDirection?>().FirstOrDefault(d => menu.GetItem(d!.Value) is null);
        if (free is null)
        {
            return Report(EditResult.Fail(EditCodes.Occupied, $"El menú «{menu.Name}» no tiene sectores libres."));
        }

        var result = Apply(e => e.MoveItem(itemId, targetMenu, free.Value));
        if (result.Success)
        {
            _selected = null;
            Rebuild();
            Say($"«{found.Item.Name}» movido a «{menu.Name}».");
        }
        else
        {
            Report(result);
        }

        return result;
    }

    private void DuplicateSelected()
    {
        if (SelectedItem is not { } item || FirstFreeDirection() is not { } free)
        {
            return;
        }

        var result = Apply(e => e.DuplicatePlacement(item.Id, free));
        if (result.Success)
        {
            _selected = free;
            Rebuild();
            Say($"«{item.Name}» duplicada en {UiText.Direction(free).ToLowerInvariant()}.");
        }
        else
        {
            Report(result);
        }
    }

    private async Task RemoveSelectedAsync()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        if (item.IsGroup)
        {
            await DeleteGroupAsync(item);
            return;
        }

        var result = Apply(e => e.RemovePlacement(item.Id));
        if (result.Success)
        {
            Rebuild();
            Say($"«{item.Name}» se quitó del sector. Sigue en la biblioteca.");
        }
        else
        {
            Report(result);
        }
    }

    /// <summary>Sustituye lo que hay en el sector elegido por otra acción de la biblioteca (si era un grupo, pide confirmación si tiene contenido).</summary>
    public async Task<EditResult> ReplaceSelectedAsync(string definitionId)
    {
        if (SelectedItem is not { } item)
        {
            return Report(EditResult.Fail(EditCodes.NotFound, "Elige primero un sector."));
        }

        EditResult result;
        if (item.IsGroup)
        {
            var contents = _session.Editor.DescribeGroup(item.Id);
            var deleteContents = contents.Placements > 0;
            if (deleteContents && !await _services.Dialogs.ConfirmAsync(
                    "Sustituir un grupo con contenido",
                    $"El grupo «{item.Name}» contiene {contents.Placements} elemento(s). Si lo sustituyes por una acción, el grupo y su contenido se quitarán de este menú (las acciones siguen en la biblioteca).",
                    "Sustituir",
                    danger: true))
            {
                return EditResult.Fail(EditCodes.NotAllowed, "Cancelado.");
            }

            result = Apply(e => e.ConvertGroupToAction(item.Id, definitionId, deleteContents));
        }
        else
        {
            result = Apply(e => e.ReplaceAction(item.Id, definitionId));
        }

        if (result.Success)
        {
            Rebuild();
            Say("Acción sustituida.");
        }
        else
        {
            Report(result);
        }

        return result;
    }

    // ───────────────────────── Grupos ─────────────────────────

    /// <summary>Crea un grupo en el sector elegido (si está libre) o en el primero libre, y lo deja elegido para renombrarlo.</summary>
    public EditResult CreateGroupAt(RadialDirection? target = null, string? name = null)
    {
        var position = target ?? (_selected is { } s && CurrentMenu.GetItem(s) is null ? s : FirstFreeDirection());
        if (position is null)
        {
            return Report(EditResult.Fail(EditCodes.Occupied, "El menú tiene los ocho sectores ocupados."));
        }

        var groupName = string.IsNullOrWhiteSpace(name) ? NextGroupName() : name;
        var result = Apply(e => e.CreateGroup(_currentMenuId, position.Value, groupName));
        if (result.Success)
        {
            _selected = position;
            Rebuild();
            Say($"Grupo «{groupName}» creado. Escribe su nombre a la derecha.");
        }
        else
        {
            Report(result);
        }

        return result;
    }

    private void CreateGroupHere() => CreateGroupAt();

    private void ConvertSelectedToGroup()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        var result = Apply(e => e.ConvertToGroup(item.Id, item.Name));
        if (result.Success)
        {
            Rebuild();
            Say($"«{item.Name}» ahora es un grupo.");
        }
        else
        {
            Report(result);
        }
    }

    private async Task DeleteGroupAsync(RadialItem group)
    {
        var contents = _session.Editor.DescribeGroup(group.Id);
        var mode = GroupDeletion.RefuseIfNotEmpty;
        if (contents.Placements > 0)
        {
            var detail = contents.Groups > 0
                ? $"Contiene {contents.Placements} elemento(s), con {contents.Groups} grupo(s) anidado(s)."
                : $"Contiene {contents.Placements} elemento(s).";
            var confirmed = await _services.Dialogs.ConfirmAsync(
                "Eliminar un grupo con contenido",
                $"{detail} Se eliminará el grupo «{group.Name}» y todo lo que contiene de los menús. Las acciones seguirán en la biblioteca.",
                "Eliminar el grupo",
                danger: true);
            if (!confirmed)
            {
                return;
            }

            mode = GroupDeletion.Cascade;
        }

        var result = Apply(e => e.DeleteGroup(group.Id, mode));
        if (result.Success)
        {
            _selected = null;
            Rebuild();
            Say($"Grupo «{group.Name}» eliminado.");
        }
        else
        {
            Report(result);
        }
    }

    // ───────────────────────── Nombre e icono ─────────────────────────

    /// <summary>Aplica el nombre escrito: el nombre del grupo, o el nombre propio de esta colocación (la definición no cambia).</summary>
    public EditResult CommitLabelText()
    {
        if (SelectedItem is not { } item)
        {
            return EditResult.Ok;
        }

        var text = _labelText.Trim();
        if (text == item.Name)
        {
            return EditResult.Ok;
        }

        var result = item.IsGroup ? Apply(e => e.RenameGroup(item.Id, text)) : Apply(e => e.SetLabel(item.Id, text.Length == 0 ? null : text));
        if (result.Success)
        {
            Rebuild();
        }
        else
        {
            Report(result);
            _labelText = item.Name;
            Raise(nameof(LabelText));
        }

        return result;
    }

    private void ResetLabelOverride()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        var result = Apply(e => e.SetLabel(item.Id, null));
        if (result.Success)
        {
            Rebuild();
            Say("Se restauró el nombre de la acción.");
        }
        else
        {
            Report(result);
        }
    }

    /// <summary>Cambia el icono del sector elegido (en un grupo, el del grupo; en una acción, solo en esta colocación).</summary>
    public EditResult SetSelectedIcon(string iconName)
    {
        if (SelectedItem is not { } item)
        {
            return EditResult.Ok;
        }

        var result = Apply(e => e.SetIcon(item.Id, new IconReference(IconSource.Glyph, iconName)));
        if (result.Success)
        {
            Rebuild();
        }
        else
        {
            Report(result);
        }

        return result;
    }

    // ───────────────────────── Interno ─────────────────────────

    /// <summary>Aplica una edición propia: la reconstrucción la hace este modelo después, con la selección ya decidida.</summary>
    private EditResult Apply(Func<ConfigurationEditor, EditResult> operation)
    {
        _operating = true;
        try
        {
            return operation(_session.Editor);
        }
        finally
        {
            _operating = false;
        }
    }

    private void OnSessionChanged()
    {
        // Un cambio que no viene de este modelo (la biblioteca, los ajustes, descartar): se reconstruye y se conserva lo posible.
        if (!_rebuilding && !_operating)
        {
            Rebuild();
        }
    }

    private RadialDirection? FirstFreeDirection()
    {
        var menu = CurrentMenu;
        foreach (var direction in Directions)
        {
            if (menu.GetItem(direction) is null)
            {
                return direction;
            }
        }

        return null;
    }

    private string NextGroupName()
    {
        var names = _session.Current.Menus.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var number = 1; ; number++)
        {
            var candidate = number == 1 ? "Grupo nuevo" : $"Grupo nuevo {number}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void Say(string message)
    {
        StatusIsError = false;
        Status = message;
    }

    private EditResult Report(EditResult result)
    {
        if (!result.Success)
        {
            StatusIsError = true;
            Status = result.Message;
            _services.Toasts.Show(result.Message, isError: true);
        }

        return result;
    }

    private void SelectionChanged()
    {
        _labelText = SelectedItem?.Name ?? string.Empty;
        RaiseSelection();
        RebuildSectors();
        PreviewInvalidated?.Invoke();
    }

    private void RaiseSelection()
    {
        foreach (var name in new[]
                 {
                     nameof(LabelText), nameof(SelectedItem), nameof(HasSelection), nameof(IsEmptySectorSelected), nameof(IsItemSelected), nameof(IsGroupSelected),
                     nameof(IsActionSelected), nameof(IsUnsupportedSelected), nameof(SelectedDirectionName), nameof(InspectorKind), nameof(InspectorSummary),
                     nameof(InspectorCategory), nameof(InspectorDescription), nameof(InspectorDefinitionName), nameof(InspectorIconName), nameof(InspectorUsage),
                     nameof(GroupContentsText), nameof(HasLabelOverride),
                 })
        {
            Raise(name);
        }

        Enter.RaiseCanExecuteChanged();
        Duplicate.RaiseCanExecuteChanged();
        Remove.RaiseCanExecuteChanged();
        CommitLabel.RaiseCanExecuteChanged();
        ResetLabel.RaiseCanExecuteChanged();
        ConvertToGroup.RaiseCanExecuteChanged();
        CreateGroup.RaiseCanExecuteChanged();
    }

    /// <summary>Reconstruye todo lo derivado de la configuración (árbol, sectores, camino, selector) y avisa a la vista previa.</summary>
    private void Rebuild()
    {
        _rebuilding = true;
        try
        {
            var configuration = _session.Current;
            if (!configuration.TryGetMenu(_currentMenuId, out _))
            {
                _currentMenuId = configuration.RootMenuId;
                _selected = null;
            }

            var chain = _session.Editor.PathTo(_currentMenuId);
            if (chain.Count == 0)
            {
                // El menú abierto ya no se alcanza desde la raíz (p. ej. se eliminó su grupo): se vuelve a la raíz.
                _currentMenuId = configuration.RootMenuId;
                _selected = null;
                chain = [configuration.RootMenuId];
            }

            Path = chain.Select(id => new PathSegment(id, _session.Editor.GetMenu(id)?.Name ?? id.Value, id == _currentMenuId)).ToList();
            Tree = BuildTree();
            _labelText = SelectedItem?.Name ?? string.Empty;
            RebuildSectors();
            RefreshPicker();

            Raise(nameof(CurrentMenuId));
            Raise(nameof(CurrentMenu));
            Raise(nameof(Depth));
            Raise(nameof(Geometry));
            Raise(nameof(MenuTitle));
            Raise(nameof(MaxDepth));
            Raise(nameof(SelectedDirection));
            RaiseSelection();
            Up.RaiseCanExecuteChanged();
        }
        finally
        {
            _rebuilding = false;
        }

        PreviewInvalidated?.Invoke();
    }

    private void RebuildSectors()
    {
        var menu = CurrentMenu;
        Sectors = Directions
            .Select(d => new SectorViewModel(
                d,
                menu.GetItem(d),
                menu.GetItem(d) is { } item ? UiText.Summary(item.Action, _services.Labels.GetLabel) : string.Empty,
                _selected == d))
            .ToList();
    }

    private void RefreshPicker()
    {
        var keyLabel = (string name) => _services.Labels.GetLabel(name);
        var counts = _session.Current.Menus.SelectMany(m => m.Items).Where(i => i.DefinitionId is not null).GroupBy(i => i.DefinitionId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        PickerResults = ActionSearch.Query(_session.Current.Actions, _pickerSearch, new ActionFilter(Kind: null), keyLabel, UiText.CategoryName, counts.Keys.ToHashSet(StringComparer.Ordinal))
            .Where(d => d.Action is not UnknownAction)
            .Take(60)
            .Select(d => new LibraryRowViewModel(d, counts.GetValueOrDefault(d.Id), keyLabel))
            .ToList();
    }

    private IReadOnlyList<TreeNodeViewModel> BuildTree()
    {
        var configuration = _session.Current;
        if (!configuration.TryGetMenu(configuration.RootMenuId, out var rootMenu))
        {
            return [];
        }

        var visited = new HashSet<MenuId> { rootMenu.Id };
        var root = NewNode(rootMenu, null, "Apps", 1);
        var stack = new Stack<TreeNodeViewModel>();
        stack.Push(root);

        // Recorrido iterativo con control de visitados: la configuración puede venir de un archivo que no es de confianza.
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!configuration.TryGetMenu(node.MenuId, out var menu))
            {
                continue;
            }

            foreach (var item in menu.Items.OrderBy(i => (int)i.Position))
            {
                if (item.Action is OpenMenuAction open && configuration.TryGetMenu(open.TargetMenuId, out var child) && visited.Add(child.Id))
                {
                    var childNode = NewNode(child, item.Id, item.Icon is { Source: IconSource.Glyph } icon ? icon.Value : "Apps", node.Depth + 1);
                    node.Children.Add(childNode);
                    stack.Push(childNode);
                }
            }
        }

        return [root];
    }

    private TreeNodeViewModel NewNode(RadialMenu menu, ItemId? groupItem, string icon, int depth)
    {
        var node = new TreeNodeViewModel(menu.Id, groupItem, menu.Name, icon, depth, menu.Items.Count, OnNodeSelected, OnNodeExpanded);
        node.SetExpandedSilently(!_collapsed.Contains(menu.Id));
        node.SetSelectedSilently(menu.Id == _currentMenuId);
        return node;
    }

    private void OnNodeSelected(TreeNodeViewModel node)
    {
        if (!_rebuilding)
        {
            OpenMenu(node.MenuId);
        }
    }

    private void OnNodeExpanded(TreeNodeViewModel node, bool expanded)
    {
        if (expanded)
        {
            _collapsed.Remove(node.MenuId);
        }
        else
        {
            _collapsed.Add(node.MenuId);
        }
    }
}
