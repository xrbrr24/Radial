using Radial.Core.Catalog;
using Radial.Core.Editing;
using Radial.Core.Model;

namespace Radial.Windows.Presentation;

/// <summary>Una fila de la biblioteca: lo que se muestra de una definición, ya calculado.</summary>
public sealed class LibraryRowViewModel
{
    public LibraryRowViewModel(ActionDefinition definition, int placements, Func<string, string?> keyLabel)
    {
        Definition = definition;
        Id = definition.Id;
        Name = definition.Name;
        Description = definition.Description ?? string.Empty;
        Kind = ActionKinds.Of(definition.Action);
        KindName = UiText.KindName(Kind);
        CategoryName = UiText.CategoryName(definition.Category);
        Summary = UiText.Summary(definition.Action, keyLabel);
        IconName = definition.Icon.Source == IconSource.Glyph ? definition.Icon.Value : "Document";
        IsBuiltIn = definition.IsBuiltIn;
        IsUnsupported = Kind == ActionKind.Unsupported;
        Placements = placements;
        TagsText = string.Join(", ", definition.TagList);
        SectionTitle = IsBuiltIn ? "De fábrica" : "Mis acciones";
        PlacedText = placements switch
        {
            0 => "Sin colocar",
            1 => "En 1 sector",
            _ => $"En {placements} sectores",
        };
    }

    public ActionDefinition Definition { get; }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public ActionKind Kind { get; }

    public string KindName { get; }

    public string CategoryName { get; }

    /// <summary>La combinación, el programa, la ruta o la dirección.</summary>
    public string Summary { get; }

    public string IconName { get; }

    public bool IsBuiltIn { get; }

    /// <summary>De un tipo que esta versión no conoce: se conserva, pero no se puede editar ni usar.</summary>
    public bool IsUnsupported { get; }

    public int Placements { get; }

    public bool IsPlaced => Placements > 0;

    public string PlacedText { get; }

    public string TagsText { get; }

    public string SectionTitle { get; }
}

/// <summary>Una opción de un filtro de la biblioteca (el valor <c>null</c> es «todos»).</summary>
public sealed record FilterOption<T>(string Label, T? Value)
    where T : struct;

/// <summary>
/// La biblioteca de acciones: búsqueda propia (nombre, descripción, categoría, etiquetas, destino y combinación de teclas), filtros,
/// secciones de fábrica y propias, y crear/editar/duplicar/eliminar. Trabaja sobre el editor de la sesión: todo cambio entra en el
/// mismo flujo de «cambios sin guardar» que el editor radial. Nada de esto usa WPF.
/// </summary>
public sealed class LibraryViewModel : ObservableObject
{
    private readonly ConfigurationSession _session;
    private readonly UiServices _services;
    private string _search = string.Empty;
    private FilterOption<ActionKind> _kindFilter;
    private FilterOption<ActionCategory> _categoryFilter;
    private FilterOption<bool> _placedFilter;
    private IReadOnlyList<LibraryRowViewModel> _rows = [];
    private LibraryRowViewModel? _selected;
    private ActionFormViewModel? _form;
    private string _status = string.Empty;

    public LibraryViewModel(ConfigurationSession session, UiServices services)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(services);
        _session = session;
        _services = services;

        KindFilters = [new FilterOption<ActionKind>("Todos los tipos", null), .. ActionFormViewModel.CreatableKinds.Select(k => new FilterOption<ActionKind>(UiText.KindName(k), k))];
        CategoryFilters = [new FilterOption<ActionCategory>("Todas las categorías", null), .. ActionCategory.Standard.Select(c => new FilterOption<ActionCategory>(UiText.CategoryName(c), c))];
        PlacedFilters = [new FilterOption<bool>("Todas", null), new FilterOption<bool>("Colocadas", true), new FilterOption<bool>("Sin colocar", false)];
        _kindFilter = KindFilters[0];
        _categoryFilter = CategoryFilters[0];
        _placedFilter = PlacedFilters[0];

        New = new RelayCommand(parameter => BeginNew(parameter is ActionKind kind ? kind : ActionKind.Shortcut));
        Edit = new RelayCommand(BeginEdit, () => _selected is { IsUnsupported: false });
        Duplicate = new RelayCommand(DuplicateSelected, () => _selected is { IsUnsupported: false });
        Delete = new AsyncCommand(DeleteSelectedAsync, () => _selected is not null);
        SaveForm = new RelayCommand(SaveCurrentForm, () => _form is not null);
        CancelForm = new AsyncCommand(CancelFormAsync, () => _form is not null);
        ClearSearch = new RelayCommand(() => SearchText = string.Empty);
        RestoreFactory = new RelayCommand(RestoreFactoryActions, () => _services.Factory is not null);

        _session.Changed += Refresh;
        Refresh();
    }

    public IReadOnlyList<FilterOption<ActionKind>> KindFilters { get; }

    public IReadOnlyList<FilterOption<ActionCategory>> CategoryFilters { get; }

    public IReadOnlyList<FilterOption<bool>> PlacedFilters { get; }

    public RelayCommand New { get; }

    public RelayCommand Edit { get; }

    public RelayCommand Duplicate { get; }

    public AsyncCommand Delete { get; }

    public RelayCommand SaveForm { get; }

    public AsyncCommand CancelForm { get; }

    public RelayCommand ClearSearch { get; }

    public RelayCommand RestoreFactory { get; }

    /// <summary>Se produce cuando el usuario pide usar una acción (p. ej. colocarla en el editor).</summary>
    public event Action<string>? PlaceRequested;

    public string SearchText
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? string.Empty))
            {
                Refresh();
            }
        }
    }

    public FilterOption<ActionKind> KindFilter
    {
        get => _kindFilter;
        set
        {
            if (value is not null && Set(ref _kindFilter, value))
            {
                Refresh();
            }
        }
    }

    public FilterOption<ActionCategory> CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            if (value is not null && Set(ref _categoryFilter, value))
            {
                Refresh();
            }
        }
    }

    public FilterOption<bool> PlacedFilter
    {
        get => _placedFilter;
        set
        {
            if (value is not null && Set(ref _placedFilter, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>Los resultados, en orden: primero las propias y luego las de fábrica; dentro de cada sección, por relevancia.</summary>
    public IReadOnlyList<LibraryRowViewModel> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    public int TotalCount => _session.Current.Actions.Count;

    public bool HasResults => _rows.Count > 0;

    public bool IsFiltering => _search.Length > 0 || _kindFilter.Value is not null || _categoryFilter.Value is not null || _placedFilter.Value is not null;

    public string ResultText => _rows.Count == TotalCount
        ? $"{TotalCount} acciones"
        : $"{_rows.Count} de {TotalCount} acciones";

    public LibraryRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                Edit.RaiseCanExecuteChanged();
                Duplicate.RaiseCanExecuteChanged();
                Delete.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>El formulario abierto (nueva o en edición), o <c>null</c>.</summary>
    public ActionFormViewModel? Form
    {
        get => _form;
        private set
        {
            if (Set(ref _form, value))
            {
                Raise(nameof(IsFormOpen));
                SaveForm.RaiseCanExecuteChanged();
                CancelForm.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsFormOpen => _form is not null;

    /// <summary>Último resultado de una operación (guardado, eliminado, error...), para mostrarlo bajo la lista.</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Pide al editor radial colocar la acción (el editor decide dónde).</summary>
    public void RequestPlace(string definitionId) => PlaceRequested?.Invoke(definitionId);

    public void SelectById(string id) => Selected = _rows.FirstOrDefault(r => r.Id == id);

    private void BeginNew(ActionKind kind)
    {
        if (_form is { HasChanges: true })
        {
            Status = "Termina o cancela la acción que estás editando antes de crear otra.";
            return;
        }

        OpenForm(ActionFormViewModel.ForNew(_session.Editor, _services, kind));
    }

    private void BeginEdit()
    {
        if (_selected is not { IsUnsupported: false } row)
        {
            return;
        }

        if (_form is { HasChanges: true })
        {
            Status = "Termina o cancela la acción que estás editando antes de abrir otra.";
            return;
        }

        OpenForm(ActionFormViewModel.ForEdit(_session.Editor, _services, row.Definition));
    }

    private void OpenForm(ActionFormViewModel form)
    {
        form.Saved += OnFormSaved;
        Form = form;
        Status = string.Empty;
    }

    private void OnFormSaved(ActionDefinition definition)
    {
        var wasNew = _form?.IsNew ?? false;
        CloseForm();
        Status = wasNew ? $"«{definition.Name}» se añadió a la biblioteca." : $"«{definition.Name}» se actualizó.";
        SearchText = string.Empty;
        SelectById(definition.Id);
    }

    private void SaveCurrentForm()
    {
        if (_form is null)
        {
            return;
        }

        var result = _form.Save();
        if (!result.Success)
        {
            Status = result.Message;
        }
    }

    private async Task CancelFormAsync()
    {
        if (_form is null)
        {
            return;
        }

        if (_form.HasChanges)
        {
            var discard = await _services.Dialogs.ConfirmAsync("¿Descartar los cambios?", "Has modificado esta acción y no la has guardado.", "Descartar", danger: true);
            if (!discard)
            {
                return;
            }
        }

        CloseForm();
    }

    private void CloseForm()
    {
        if (_form is not null)
        {
            _form.Key.StopRecording();
        }

        Form = null;
    }

    private void DuplicateSelected()
    {
        if (_selected is null)
        {
            return;
        }

        var result = _session.Editor.DuplicateDefinition(_selected.Id, out var copy);
        if (!result.Success || copy is null)
        {
            Status = result.Message;
            _services.Toasts.Show(result.Message, isError: true);
            return;
        }

        Status = $"Se creó «{copy.Name}».";
        SearchText = string.Empty;
        SelectById(copy.Id);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selected is not { } row)
        {
            return;
        }

        var placements = _session.Editor.PlacementsOf(row.Id).Count;
        var removePlacements = false;
        if (placements > 0)
        {
            var message = $"«{row.Name}» está colocada en {placements} sector(es) de tus menús. Si la eliminas, también se quitará de ellos.";
            if (!await _services.Dialogs.ConfirmAsync("Eliminar una acción en uso", message, "Eliminar y quitar de los menús", danger: true))
            {
                return;
            }

            removePlacements = true;
        }
        else if (!await _services.Dialogs.ConfirmAsync("Eliminar acción", $"Se eliminará «{row.Name}» de la biblioteca.", "Eliminar", danger: true))
        {
            return;
        }

        var result = _session.Editor.DeleteDefinition(row.Id, removePlacements);
        Status = result.Success ? $"«{row.Name}» se eliminó." : result.Message;
        if (!result.Success)
        {
            _services.Toasts.Show(result.Message, isError: true);
        }
        else if (_form?.EditingId == row.Id)
        {
            CloseForm();
        }
    }

    private void RestoreFactoryActions()
    {
        if (_services.Factory is not { } factory)
        {
            return;
        }

        var added = _session.Editor.RestoreFactory(factory());
        Status = added == 0 ? "Ya están todas las acciones de fábrica." : $"Se restauraron {added} acciones de fábrica.";
    }

    private void Refresh()
    {
        var keyLabel = (string name) => _services.Labels.GetLabel(name);
        var current = _session.Current;
        var counts = current.Menus
            .SelectMany(m => m.Items)
            .Where(i => i.DefinitionId is not null)
            .GroupBy(i => i.DefinitionId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var placedIds = counts.Keys.ToHashSet(StringComparer.Ordinal);
        var filter = new ActionFilter(_categoryFilter.Value, _kindFilter.Value, null, _placedFilter.Value);
        var found = ActionSearch.Query(current.Actions, _search, filter, keyLabel, UiText.CategoryName, placedIds);

        // Primero las propias y después las de fábrica; el orden relativo (relevancia) se conserva.
        var rows = found
            .Select(d => new LibraryRowViewModel(d, counts.GetValueOrDefault(d.Id), keyLabel))
            .ToList();
        Rows = [.. rows.Where(r => !r.IsBuiltIn), .. rows.Where(r => r.IsBuiltIn)];

        if (_selected is not null)
        {
            Selected = _rows.FirstOrDefault(r => r.Id == _selected.Id);
        }

        Raise(nameof(TotalCount));
        Raise(nameof(HasResults));
        Raise(nameof(IsFiltering));
        Raise(nameof(ResultText));
    }
}
