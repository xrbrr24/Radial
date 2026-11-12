using Radial.Core.Catalog;
using Radial.Core.Editing;
using Radial.Core.Model;
using Radial.Core.Validation;
using Radial.Windows.Shortcuts;

namespace Radial.Windows.Presentation;

/// <summary>
/// El formulario de una acción de la biblioteca (nueva o en edición). Los campos cambian con el tipo; el formulario NUNCA guarda
/// una definición no válida: construye la definición, la valida con las mismas reglas del dominio que usan la carga y la
/// ejecución, y solo entonces la entrega al editor. Tampoco crea ni edita acciones de tipo desconocido.
///
/// Comportamiento fijado (decisión del bloque S1, R1): el formulario es un BORRADOR independiente de la sesión. Siempre escribe en el
/// editor vigente de la sesión, y si la sesión se descarta mientras está abierto, no se cierra ni se reconstruye: si el usuario lo
/// guarda después, vuelve a aplicar sobre la sesión vigente los valores que contiene, aunque el descarte los hubiera revertido. La
/// única excepción es una acción que ya no existe (p. ej. se creó en los cambios descartados): entonces <see cref="Save"/> falla con
/// <c>NotFound</c> y conserva lo escrito, en lugar de resucitarla o dar un éxito falso.
/// </summary>
public sealed class ActionFormViewModel : ObservableObject
{
    /// <summary>Los tipos que se pueden crear (los de <see cref="ActionKind"/> menos «no soportada»).</summary>
    public static IReadOnlyList<ActionKind> CreatableKinds { get; } =
        [ActionKind.Shortcut, ActionKind.Application, ActionKind.File, ActionKind.Folder, ActionKind.Url];

    // La sesión, no un editor: «Descartar» sustituye el editor de la sesión, y un formulario abierto debe seguir escribiendo en el vigente.
    private readonly ConfigurationSession _session;
    private readonly UiServices _services;
    private readonly string? _editingId;
    private readonly bool _builtIn;
    private ActionKind _kind;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private ActionCategory _category = ActionCategory.Custom;
    private string _tagsText = string.Empty;
    private string _iconName = "Apps";
    private string _path = string.Empty;
    private string _arguments = string.Empty;
    private string _workingDirectory = string.Empty;
    private string _url = string.Empty;
    private bool _touched;
    private bool _loading;
    private string _saveError = string.Empty;

    private ActionFormViewModel(ConfigurationSession session, UiServices services, string? editingId, bool builtIn)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _services = services;
        _editingId = editingId;
        _builtIn = builtIn;
        Key = new KeyCaptureViewModel(services.Labels);
        Key.ChordChanged += OnFieldChanged;
        Pick = new RelayCommand(PickTarget);
        PickWorkingDirectory = new RelayCommand(() =>
        {
            if (_services.Files.PickFolder() is { } folder)
            {
                WorkingDirectory = folder;
            }
        });
    }

    /// <summary>Un formulario vacío para crear una acción del tipo dado.</summary>
    public static ActionFormViewModel ForNew(ConfigurationSession session, UiServices services, ActionKind kind)
    {
        var form = new ActionFormViewModel(session, services, null, false);
        form._loading = true;
        form._kind = kind;
        form._category = DefaultCategory(kind);
        form._iconName = DefaultIcon(kind);
        form._loading = false;
        return form;
    }

    /// <summary>Un formulario con los datos de una definición existente. Una definición de tipo desconocido no se puede editar.</summary>
    public static ActionFormViewModel ForEdit(ConfigurationSession session, UiServices services, ActionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Action is UnknownAction)
        {
            throw new InvalidOperationException("Una acción de tipo desconocido se conserva tal cual y no se puede editar.");
        }

        var form = new ActionFormViewModel(session, services, definition.Id, definition.IsBuiltIn);
        form._loading = true;
        form._kind = ActionKinds.Of(definition.Action);
        form._name = definition.Name;
        form._description = definition.Description ?? string.Empty;
        form._category = definition.Category;
        form._tagsText = string.Join(", ", definition.TagList);
        form._iconName = definition.Icon.Source == IconSource.Glyph ? definition.Icon.Value : "Apps";
        switch (definition.Action)
        {
            case SendKeysAction keys:
                form.Key.Load(keys.Chord);
                break;
            case LaunchApplicationAction launch:
                form._path = launch.Path;
                form._arguments = launch.Arguments ?? string.Empty;
                form._workingDirectory = launch.WorkingDirectory ?? string.Empty;
                break;
            case OpenFileAction file:
                form._path = file.Path;
                break;
            case OpenFolderAction folder:
                form._path = folder.Path;
                break;
            case OpenUrlAction url:
                form._url = url.Url;
                break;
        }

        form._loading = false;
        return form;
    }

    /// <summary>El editor VIGENTE de la sesión (cambia cuando la sesión descarta); nunca se guarda una referencia a uno concreto.</summary>
    private ConfigurationEditor Editor => _session.Editor;

    public KeyCaptureViewModel Key { get; }

    public RelayCommand Pick { get; }

    public RelayCommand PickWorkingDirectory { get; }

    public bool IsNew => _editingId is null;

    public bool IsBuiltIn => _builtIn;

    public string? EditingId => _editingId;

    public string Title => IsNew ? "Nueva acción" : "Editar acción";

    public ActionKind Kind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value))
            {
                Raise(nameof(IsShortcut));
                Raise(nameof(IsApplication));
                Raise(nameof(IsFile));
                Raise(nameof(IsFolder));
                Raise(nameof(IsUrl));
                Raise(nameof(UsesPath));
                Raise(nameof(PathLabel));
                OnFieldChanged();
            }
        }
    }

    public bool IsShortcut => _kind == ActionKind.Shortcut;

    public bool IsApplication => _kind == ActionKind.Application;

    public bool IsFile => _kind == ActionKind.File;

    public bool IsFolder => _kind == ActionKind.Folder;

    public bool IsUrl => _kind == ActionKind.Url;

    public bool UsesPath => _kind is ActionKind.Application or ActionKind.File or ActionKind.Folder;

    public string PathLabel => _kind switch
    {
        ActionKind.Application => "PROGRAMA (.EXE)",
        ActionKind.File => "ARCHIVO",
        ActionKind.Folder => "CARPETA",
        _ => string.Empty,
    };

    public string Name { get => _name; set { if (Set(ref _name, value)) { OnFieldChanged(); } } }

    public string Description { get => _description; set { if (Set(ref _description, value)) { OnFieldChanged(); } } }

    public ActionCategory Category { get => _category; set { if (Set(ref _category, value)) { OnFieldChanged(); } } }

    /// <summary>Las etiquetas separadas por comas.</summary>
    public string TagsText { get => _tagsText; set { if (Set(ref _tagsText, value)) { OnFieldChanged(); } } }

    public string IconName { get => _iconName; set { if (Set(ref _iconName, value ?? "Apps")) { OnFieldChanged(); } } }

    public string Path { get => _path; set { if (Set(ref _path, value)) { OnFieldChanged(); } } }

    public string Arguments { get => _arguments; set { if (Set(ref _arguments, value)) { OnFieldChanged(); } } }

    public string WorkingDirectory { get => _workingDirectory; set { if (Set(ref _workingDirectory, value)) { OnFieldChanged(); } } }

    public string Url { get => _url; set { if (Set(ref _url, value)) { OnFieldChanged(); } } }

    /// <summary>Los tipos que se pueden elegir, con su nombre.</summary>
    public static IReadOnlyList<KindOption> KindOptions { get; } = CreatableKinds.Select(k => new KindOption(k, UiText.KindName(k))).ToList();

    /// <summary>Las categorías ofrecidas (las estándar y, si la definición usa otra, esa), con su nombre.</summary>
    public IReadOnlyList<CategoryOption> CategoryOptions => OfferedCategories().Select(c => new CategoryOption(c, UiText.CategoryName(c))).ToList();

    private List<ActionCategory> OfferedCategories()
    {
        var offered = ActionCategory.Standard.ToList();
        if (!string.IsNullOrEmpty(_category.Id) && !offered.Contains(_category))
        {
            offered.Add(_category);
        }

        return offered;
    }

    /// <summary>Lo que impide guardar, en palabras del usuario. Vacío hasta que el usuario toca algún campo.</summary>
    public IReadOnlyList<string> Problems => _touched ? AllProblems() : [];

    /// <summary>Se puede guardar: la definición construida es válida.</summary>
    public bool CanSave => AllProblems().Count == 0;

    /// <summary>Cambió algo respecto a lo abierto (para pedir confirmación al cerrar el formulario).</summary>
    public bool HasChanges => _touched;

    /// <summary>Se produce al guardar bien, con la definición guardada.</summary>
    public event Action<ActionDefinition>? Saved;

    /// <summary>
    /// Construye la definición con lo que hay en el formulario (aunque no sea válida: <see cref="AllProblems"/> la valida). El
    /// identificador de una acción nueva sale del nombre; el de una existente no cambia nunca.
    /// </summary>
    public ActionDefinition Build()
    {
        var id = _editingId ?? Editor.NewDefinitionId(string.IsNullOrWhiteSpace(_name) ? "accion" : _name);
        return new ActionDefinition(
            id,
            _name.Trim(),
            _category,
            _description.Trim(),
            new IconReference(IconSource.Glyph, string.IsNullOrWhiteSpace(_iconName) ? "Apps" : _iconName),
            BuildAction(),
            ParseTags(_tagsText),
            _builtIn);
    }

    /// <summary>Guarda en la biblioteca. Devuelve el resultado; si falla, el formulario sigue abierto con el motivo en <see cref="SaveError"/>.</summary>
    public EditResult Save()
    {
        _touched = true;
        RaiseProblems();
        var problems = AllProblems();
        if (problems.Count > 0)
        {
            SaveError = problems[0];
            return EditResult.Fail(EditCodes.Invalid, problems[0]);
        }

        // Si la sesión se descartó mientras tanto y la acción que se edita ya no existe (p. ej. se creó en los cambios descartados),
        // no se resucita en silencio ni se muestra un éxito: se explica y el formulario conserva lo escrito.
        if (!IsNew && !Editor.Current.TryGetDefinition(_editingId!, out _))
        {
            const string missing = "Esta acción ya no existe en la biblioteca (se descartaron los cambios que la creaban). Cierra el formulario y créala de nuevo.";
            SaveError = missing;
            return EditResult.Fail(EditCodes.NotFound, missing);
        }

        var definition = Build();
        var result = IsNew ? Editor.AddDefinition(definition) : Editor.UpdateDefinition(definition);
        SaveError = result.Success ? string.Empty : result.Message;
        if (result.Success)
        {
            _touched = false;
            Saved?.Invoke(definition);
        }

        return result;
    }

    public string SaveError
    {
        get => _saveError;
        private set => Set(ref _saveError, value);
    }

    private RadialAction BuildAction() => _kind switch
    {
        ActionKind.Shortcut => new SendKeysAction(Key.Chord ?? new KeyChord(Key.Modifiers, Key.KeyName ?? string.Empty)),
        ActionKind.Application => new LaunchApplicationAction(_path.Trim(), NullIfEmpty(_arguments), NullIfEmpty(_workingDirectory)),
        ActionKind.File => new OpenFileAction(_path.Trim()),
        ActionKind.Folder => new OpenFolderAction(_path.Trim()),
        _ => new OpenUrlAction(_url.Trim()),
    };

    private List<string> AllProblems()
    {
        var problems = new List<string>();
        var definition = Build();
        if (string.IsNullOrWhiteSpace(_name))
        {
            problems.Add("Ponle un nombre a la acción.");
        }
        else if (_name.Trim().Length > ConfigurationLimits.MaxNameLength)
        {
            problems.Add($"El nombre tiene como máximo {ConfigurationLimits.MaxNameLength} caracteres.");
        }

        switch (_kind)
        {
            case ActionKind.Shortcut when Key.Chord is null:
                problems.Add(string.IsNullOrEmpty(Key.Message)
                    ? "Graba o elige una combinación de teclas."
                    : Key.Message);
                break;
            case ActionKind.Application when string.IsNullOrWhiteSpace(_path):
                problems.Add("Elige el programa que se va a abrir.");
                break;
            case ActionKind.File when string.IsNullOrWhiteSpace(_path):
                problems.Add("Elige el archivo que se va a abrir.");
                break;
            case ActionKind.Folder when string.IsNullOrWhiteSpace(_path):
                problems.Add("Elige la carpeta que se va a abrir.");
                break;
            case ActionKind.Url when string.IsNullOrWhiteSpace(_url):
                problems.Add("Escribe la dirección web (http:// o https://).");
                break;
        }

        // Con todo relleno, mandan las reglas del dominio (rutas, esquema, longitudes, etiquetas): una sola fuente de lo válido.
        if (problems.Count > 0)
        {
            return problems;
        }

        problems.AddRange(definition.GetProblems().Where(p => !string.IsNullOrWhiteSpace(p)).Distinct());

        // Lo que el ejecutor rechazaría SIEMPRE al ejecutar (ruta relativa, de dispositivo, con protocolo) no se guarda: si no, la
        // acción parecería correcta en el editor y solo fallaría al usarla en el radial.
        if (problems.Count == 0)
        {
            problems.AddRange(ActionTargets.GetProblems(definition.Action));
        }

        return problems;
    }

    private void OnFieldChanged()
    {
        if (_loading)
        {
            return;
        }

        _touched = true;
        RaiseProblems();
    }

    private void RaiseProblems()
    {
        Raise(nameof(Problems));
        Raise(nameof(CanSave));
        Raise(nameof(HasChanges));
        Raise(nameof(CategoryOptions));
    }

    private void PickTarget()
    {
        var chosen = _kind switch
        {
            ActionKind.Application => _services.Files.PickApplication(),
            ActionKind.File => _services.Files.PickFile(),
            ActionKind.Folder => _services.Files.PickFolder(),
            _ => null,
        };

        if (chosen is not null)
        {
            Path = chosen;
            if (string.IsNullOrWhiteSpace(_name))
            {
                Name = SuggestName(chosen);
            }
        }
    }

    private static string SuggestName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = System.IO.Path.GetFileNameWithoutExtension(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private static IReadOnlyList<string>? ParseTags(string text)
    {
        var tags = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return tags.Count == 0 ? null : tags;
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static ActionCategory DefaultCategory(ActionKind kind) => kind switch
    {
        ActionKind.Application => ActionCategory.Applications,
        ActionKind.File or ActionKind.Folder => ActionCategory.Files,
        ActionKind.Url => ActionCategory.Browser,
        _ => ActionCategory.Custom,
    };

    private static string DefaultIcon(ActionKind kind) => kind switch
    {
        ActionKind.Shortcut => "Keyboard",
        ActionKind.Application => "Application",
        ActionKind.File => "Document",
        ActionKind.Folder => "Folder",
        ActionKind.Url => "Globe",
        _ => "Apps",
    };
}

/// <summary>Un tipo de acción elegible en el formulario.</summary>
public sealed record KindOption(ActionKind Kind, string Name);

/// <summary>Una categoría elegible en el formulario.</summary>
public sealed record CategoryOption(ActionCategory Category, string Name);

/// <summary>Lo que los modelos de vista necesitan del entorno, para poder probarlos con dobles.</summary>
public sealed record UiServices(IDialogService Dialogs, IFilePicker Files, IToastSink Toasts, IKeyLabelProvider Labels)
{
    /// <summary>Las definiciones de fábrica (para «restaurar las acciones de fábrica»); <c>null</c> si no se ofrece.</summary>
    public Func<IReadOnlyList<ActionDefinition>>? Factory { get; init; }
}
