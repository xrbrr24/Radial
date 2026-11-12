using Radial.Core.Editing;
using Radial.Core.Model;
using Radial.Core.Storage;

namespace Radial.Windows.Presentation;

/// <summary>Las secciones del centro de control.</summary>
public enum AppSection
{
    Editor,
    Library,
    Settings,
    Diagnostics,
}

/// <summary>Una entrada de la navegación lateral.</summary>
public sealed record NavItem(AppSection Section, string Title, string IconName, string Hint);

/// <summary>Un resultado de la búsqueda global de comandos y navegación.</summary>
public sealed record CommandEntry(string Title, string Subtitle, string IconName, Action Run);

/// <summary>Cómo está la configuración respecto al disco.</summary>
public enum SaveState
{
    Saved,
    Unsaved,
    Failed,

    /// <summary>El archivo del disco es de una versión futura o no se pudo leer: no se sobrescribe.</summary>
    ReadOnly,
}

/// <summary>
/// El centro de control: navegación lateral, barra superior (recorrido, búsqueda global de comandos, estado de guardado) y las
/// secciones. Es dueño de la sesión de edición, así que el guardado, el descarte y la confirmación de cambios pendientes viven aquí
/// y valen para todas las secciones. La búsqueda de la barra superior es de COMANDOS y NAVEGACIÓN (ir a una sección, a un grupo,
/// guardar, crear una acción); la de acciones es la de la biblioteca.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly ConfigurationSession _session;
    private AppSection _section = AppSection.Editor;
    private SaveState _saveState;
    private string _commandText = string.Empty;
    private IReadOnlyList<CommandEntry> _commandResults = [];
    private bool _isCommandOpen;
    private string _banner = string.Empty;

    /// <param name="diagnostics">El contenido de la sección de diagnóstico (lo aporta la aplicación: depende de la captura global).</param>
    public ShellViewModel(ConfigurationSession session, UiServices services, InAppDialogs dialogs, object diagnostics)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _session = session;
        Dialogs = dialogs;
        Diagnostics = diagnostics;

        Editor = new EditorViewModel(session, services);
        Library = new LibraryViewModel(session, services);
        Settings = new SettingsViewModel(session);

        NavItems =
        [
            new NavItem(AppSection.Editor, "Editor radial", "Apps", "Organiza los ocho sectores y los grupos"),
            new NavItem(AppSection.Library, "Biblioteca de acciones", "Tag", "Crea y busca tus acciones"),
            new NavItem(AppSection.Settings, "Ajustes", "Settings", "Tiempos y profundidad del radial"),
            new NavItem(AppSection.Diagnostics, "Diagnóstico", "Info", "Estado de la captura global"),
        ];

        Navigate = new RelayCommand(parameter =>
        {
            if (parameter is AppSection section)
            {
                Go(section);
            }
        });
        Save = new RelayCommand(SaveNow, () => IsDirty && _session.CanSave);
        Discard = new AsyncCommand(DiscardAsync, () => IsDirty);
        ToggleCommands = new RelayCommand(() => IsCommandOpen = !IsCommandOpen);
        RunCommand = new RelayCommand(parameter =>
        {
            if (parameter is CommandEntry entry)
            {
                CloseCommands();
                entry.Run();
            }
        });
        DismissBanner = new RelayCommand(() => Banner = string.Empty);

        // Pedir a la biblioteca «colocar» una acción lleva al editor y la coloca allí.
        Library.PlaceRequested += id =>
        {
            Go(AppSection.Editor);
            Editor.PlaceDefinition(id);
        };

        _session.Changed += OnSessionChanged;
        Editor.PendingChanged += OnSessionChanged;
        _saveState = ComputeState();
        Banner = LoadBanner(session.Loaded);
    }

    public InAppDialogs Dialogs { get; }

    public EditorViewModel Editor { get; }

    public LibraryViewModel Library { get; }

    public SettingsViewModel Settings { get; }

    public object Diagnostics { get; }

    public IReadOnlyList<NavItem> NavItems { get; }

    public RelayCommand Navigate { get; }

    public RelayCommand Save { get; }

    public AsyncCommand Discard { get; }

    public RelayCommand ToggleCommands { get; }

    public RelayCommand RunCommand { get; }

    public RelayCommand DismissBanner { get; }

    public ConfigurationSession Session => _session;

    public AppSection Section
    {
        get => _section;
        private set
        {
            if (Set(ref _section, value))
            {
                Raise(nameof(CurrentPage));
                Raise(nameof(Breadcrumb));
                Raise(nameof(Title));
            }
        }
    }

    /// <summary>El modelo de la sección abierta (la vista elige su plantilla por el tipo).</summary>
    public object CurrentPage => _section switch
    {
        AppSection.Editor => Editor,
        AppSection.Library => Library,
        AppSection.Settings => Settings,
        _ => Diagnostics,
    };

    public string Title => NavItems.First(n => n.Section == _section).Title;

    /// <summary>El recorrido de la barra superior: Radial › sección › (en el editor) menú abierto.</summary>
    public IReadOnlyList<string> Breadcrumb
    {
        get
        {
            var parts = new List<string> { "Radial", Title };
            if (_section == AppSection.Editor)
            {
                parts.AddRange(Editor.Path.Select(p => p.Name));
            }

            return parts;
        }
    }

    public SaveState SaveState
    {
        get => _saveState;
        private set
        {
            if (Set(ref _saveState, value))
            {
                Raise(nameof(SaveText));
                Raise(nameof(IsDirty));
            }
        }
    }

    /// <summary>
    /// Hay cambios sin guardar: en la sesión, o un nombre escrito en el editor que todavía no se ha confirmado (no depende de que el
    /// cuadro de texto haya perdido el foco). Es lo que muestran el estado de guardado, el botón Guardar y la pregunta al cerrar.
    /// </summary>
    public bool IsDirty => _session.IsDirty || Editor.HasPendingEdits;

    /// <summary>
    /// Confirma lo que esté escrito y pendiente en el editor para que entre en la configuración. Se llama antes de guardar, de ocultar
    /// la ventana y de decidir si hay cambios al cerrar; la vista no necesita esperar a que ningún control pierda el foco.
    /// Devuelve <c>false</c> si había una edición pendiente y la configuración la rechazó: quien llama debe cancelar lo que iba a hacer
    /// (guardar, cerrar u ocultar la ventana) para que el usuario vea el aviso. El texto rechazado vuelve al nombre que había.
    /// </summary>
    public bool CommitPending() => Editor.CommitPending();

    /// <summary>Un atajo global de la ventana (Ctrl+S, Ctrl+K).</summary>
    public enum GlobalShortcut
    {
        None,
        Save,
        FocusCommands,
    }

    /// <summary>Hay un atajo de teclado grabándose en el formulario de la biblioteca: la pulsación es para el grabador, no para la ventana.</summary>
    public bool IsRecordingShortcut => Library.Form?.Key.IsRecording == true;

    /// <summary>
    /// Qué atajo global (si alguno) corresponde a una pulsación. Mientras se graba un atajo no hay ninguno: Ctrl+S y Ctrl+K deben poder
    /// grabarse como cualquier otra combinación. <paramref name="modifiers"/> son los modificadores pulsados y <paramref name="virtualKey"/>
    /// el código de tecla virtual.
    /// </summary>
    public GlobalShortcut ResolveGlobalShortcut(KeyModifiers modifiers, int virtualKey)
    {
        if (IsRecordingShortcut || modifiers != KeyModifiers.Control)
        {
            return GlobalShortcut.None;
        }

        return virtualKey switch
        {
            0x53 => GlobalShortcut.Save, // S
            0x4B => GlobalShortcut.FocusCommands, // K
            _ => GlobalShortcut.None,
        };
    }

    public string SaveText => _saveState switch
    {
        SaveState.Saved => "Todo guardado",
        SaveState.Unsaved => "Cambios sin guardar",
        SaveState.Failed => "No se pudo guardar",
        _ => "Solo lectura",
    };

    /// <summary>Detalle del último fallo de guardado, o por qué es de solo lectura.</summary>
    public string SaveDetail => _saveState switch
    {
        SaveState.Failed => _session.LastSaveError ?? string.Empty,
        SaveState.ReadOnly => "El archivo de configuración es de una versión más nueva de Radial o no se pudo leer: no se sobrescribe. Puedes editar, pero no guardar.",
        _ => string.Empty,
    };

    /// <summary>Un aviso destacado bajo la barra (p. ej. la configuración se recuperó de una copia); se puede cerrar.</summary>
    public string Banner
    {
        get => _banner;
        private set
        {
            if (Set(ref _banner, value))
            {
                Raise(nameof(HasBanner));
            }
        }
    }

    public bool HasBanner => _banner.Length > 0;

    /// <summary>
    /// Muestra en el banner de la ventana un problema que compromete el funcionamiento de Radial (lo usa el sistema de avisos de errores,
    /// <see cref="Radial.Windows.Diagnostics.NotificationRouter"/>). Reutiliza el banner existente: queda visible hasta que el usuario
    /// lo cierra, así que se ve al abrir la ventana aunque el fallo ocurriera con Radial oculto en la bandeja. Un mensaje ya mostrado no
    /// se repite; si se acumulan demasiados, manda el último. Solo desde el hilo de la interfaz.
    /// </summary>
    public void ShowProblem(string message)
    {
        const int MaxBannerLength = 600;
        if (string.IsNullOrWhiteSpace(message) || _banner.Contains(message, StringComparison.Ordinal))
        {
            return;
        }

        var combined = _banner.Length == 0 ? message : $"{_banner} {message}";
        Banner = combined.Length > MaxBannerLength ? message : combined;
    }

    // ───────────────────────── Búsqueda global ─────────────────────────

    public string CommandText
    {
        get => _commandText;
        set
        {
            if (Set(ref _commandText, value ?? string.Empty))
            {
                IsCommandOpen = _commandText.Length > 0;
                RefreshCommands();
            }
        }
    }

    public bool IsCommandOpen
    {
        get => _isCommandOpen;
        set
        {
            if (Set(ref _isCommandOpen, value) && value)
            {
                RefreshCommands();
            }
        }
    }

    public IReadOnlyList<CommandEntry> CommandResults
    {
        get => _commandResults;
        private set => Set(ref _commandResults, value);
    }

    public void CloseCommands()
    {
        _commandText = string.Empty;
        Raise(nameof(CommandText));
        IsCommandOpen = false;
    }

    // ───────────────────────── Navegación y guardado ─────────────────────────

    public void Go(AppSection section) => Section = section;

    public void SaveNow()
    {
        // Lo escrito y no confirmado entra en la sesión ANTES de guardar; si no, se guardaría sin ello y el estado diría «guardado».
        // Si la configuración rechaza esa edición (ya se avisó del motivo), no se guarda nada: el usuario debe ver el error antes.
        if (!CommitPending())
        {
            return;
        }

        if (!_session.CanSave)
        {
            Dialogs.Show(SaveDetail.Length > 0 ? SaveDetail : "No se puede guardar.", isError: true);
            return;
        }

        var result = _session.Save();
        if (!result.Success)
        {
            Dialogs.Show("No se pudo guardar: " + result.Message, isError: true);
        }
        else if (result.Changed)
        {
            Dialogs.Show("Configuración guardada. El radial ya usa los cambios.");
        }
    }

    private async Task DiscardAsync()
    {
        if (!IsDirty)
        {
            return;
        }

        if (await Dialogs.ConfirmAsync("¿Descartar los cambios?", "Se perderán todas las modificaciones desde el último guardado.", "Descartar cambios", danger: true))
        {
            _session.Discard();
            Dialogs.Show("Cambios descartados.");
        }
    }

    /// <summary>
    /// Antes de cerrar la aplicación: si hay cambios sin guardar pregunta si se guardan, se descartan o se cancela el cierre.
    /// Devuelve <c>true</c> si se puede cerrar. Si se elige guardar y falla, no se cierra (los cambios siguen en memoria).
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        // Antes de decidir si hay algo que guardar, lo escrito y pendiente se incorpora a la sesión. Si se rechaza (ya se avisó del
        // motivo), el cierre se cancela: la ventana debe seguir disponible para que el usuario vea el error.
        if (!CommitPending())
        {
            return false;
        }

        if (!_session.IsDirty)
        {
            return true;
        }

        if (!_session.CanSave)
        {
            return await Dialogs.ConfirmAsync("Cambios sin guardar", "Hay cambios que no se pueden guardar (el archivo es de solo lectura). Si cierras, se perderán.", "Cerrar y perder los cambios", danger: true);
        }

        var choice = await Dialogs.ChooseAsync(
            "Cambios sin guardar",
            "Has modificado la configuración y todavía no está guardada.",
            ["Seguir editando", "Descartar y salir", "Guardar y salir"],
            dangerIndex: 1);

        switch (choice)
        {
            case 2:
                var result = _session.Save();
                if (result.Success)
                {
                    return true;
                }

                Dialogs.Show("No se pudo guardar: " + result.Message, isError: true);
                return false;
            case 1:
                return true;
            default:
                return false;
        }
    }

    private void OnSessionChanged()
    {
        SaveState = ComputeState();
        Raise(nameof(IsDirty));
        Raise(nameof(SaveDetail));
        Raise(nameof(Breadcrumb));
        Save.RaiseCanExecuteChanged();
        Discard.RaiseCanExecuteChanged();
    }

    private SaveState ComputeState()
    {
        if (!_session.CanSave)
        {
            return SaveState.ReadOnly;
        }

        if (_session.LastSaveError is not null && IsDirty)
        {
            return SaveState.Failed;
        }

        return IsDirty ? SaveState.Unsaved : SaveState.Saved;
    }

    private static string LoadBanner(SettingsLoadResult loaded)
    {
        var important = loaded.Notices.Where(n => n.Severity != SettingsNoticeSeverity.Info).Select(n => n.Message).ToList();
        if (important.Count > 0)
        {
            return string.Join(" ", important.Take(2));
        }

        return loaded.Source switch
        {
            SettingsSource.RecoveredFromBackup => "La configuración estaba dañada: se recuperó la copia válida más reciente.",
            SettingsSource.DefaultsAfterProblem => "La configuración estaba dañada y no había copia válida: se usan los valores de fábrica.",
            _ => string.Empty,
        };
    }

    // ───────────────────────── Comandos ─────────────────────────

    private void RefreshCommands()
    {
        var query = ActionSearch.Normalize(_commandText).Trim();
        var all = new List<CommandEntry>();

        foreach (var nav in NavItems)
        {
            all.Add(new CommandEntry($"Ir a {nav.Title}", nav.Hint, nav.IconName, () => Go(nav.Section)));
        }

        foreach (var menu in _session.Current.Menus)
        {
            var target = menu.Id;
            all.Add(new CommandEntry($"Abrir el menú «{menu.Name}»", "Editor radial", "Apps", () =>
            {
                Go(AppSection.Editor);
                Editor.OpenMenu(target);
            }));
        }

        if (IsDirty && _session.CanSave)
        {
            all.Add(new CommandEntry("Guardar cambios", "Escribe la configuración y la aplica al radial", "Save", SaveNow));
        }

        if (IsDirty)
        {
            all.Add(new CommandEntry("Descartar cambios", "Vuelve a lo último guardado", "Undo", () => Discard.Execute(null)));
        }

        all.Add(NewAction("Nueva acción: atajo de teclado", ActionKind.Shortcut, "Keyboard"));
        all.Add(NewAction("Nueva acción: aplicación", ActionKind.Application, "Application"));
        all.Add(NewAction("Nueva acción: archivo", ActionKind.File, "Document"));
        all.Add(NewAction("Nueva acción: carpeta", ActionKind.Folder, "Folder"));
        all.Add(NewAction("Nueva acción: dirección web", ActionKind.Url, "Globe"));
        all.Add(new CommandEntry("Crear un grupo en el menú abierto", "Editor radial", "New", () =>
        {
            Go(AppSection.Editor);
            Editor.CreateGroupAt();
        }));

        var tokens = query.Split([' ', '+'], StringSplitOptions.RemoveEmptyEntries);
        CommandResults = tokens.Length == 0
            ? all.Take(8).ToList()
            : all.Where(e => Matches(e, tokens)).Take(10).ToList();
    }

    private CommandEntry NewAction(string title, ActionKind kind, string icon) =>
        new(title, "Biblioteca de acciones", icon, () =>
        {
            Go(AppSection.Library);
            Library.New.Execute(kind);
        });

    private static bool Matches(CommandEntry entry, string[] tokens)
    {
        var text = ActionSearch.Normalize(entry.Title + " " + entry.Subtitle);
        return tokens.All(token => text.Contains(token, StringComparison.Ordinal));
    }
}
