using Radial.Core.Catalog;
using Radial.Core.Editing;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Windows.Presentation;
using Radial.Windows.Shortcuts;
using Radial.Windows.Storage;
using Radial.Windows.Tests.Storage;

namespace Radial.Windows.Tests.Presentation;

/// <summary>Un almacén en memoria que cuenta los guardados y puede fallar a voluntad. No toca el disco.</summary>
internal sealed class MemoryStore : ISettingsStore
{
    private readonly RadialConfiguration _initial;
    private readonly bool _canSave;

    public MemoryStore(RadialConfiguration initial, bool canSave = true)
    {
        _initial = initial;
        _canSave = canSave;
    }

    public int Saves { get; private set; }

    public bool FailNext { get; set; }

    public RadialConfiguration? LastSaved { get; private set; }

    public SettingsLoadResult LoadOrCreate() => new(_initial, SettingsSource.Loaded, [], _canSave);

    public SettingsSaveResult Save(RadialConfiguration configuration)
    {
        if (FailNext)
        {
            FailNext = false;
            return SettingsSaveResult.Failed("Disco lleno (prueba).");
        }

        Saves++;
        LastSaved = configuration;
        return SettingsSaveResult.Saved;
    }
}

internal sealed class FakeFilePicker : IFilePicker
{
    public string? Application { get; set; }

    public string? File { get; set; }

    public string? Folder { get; set; }

    public string? PickApplication() => Application;

    public string? PickFile() => File;

    public string? PickFolder() => Folder;
}

/// <summary>Diálogos y avisos con respuestas preparadas: no muestran nada y registran lo que se les pidió.</summary>
internal sealed class FakeDialogs : IDialogService, IToastSink
{
    public bool ConfirmAnswer { get; set; } = true;

    public int ChooseAnswer { get; set; } = -1;

    public List<string> Confirmations { get; } = [];

    public List<(string Message, bool IsError)> Toasts { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false)
    {
        Confirmations.Add(title + " | " + message);
        return Task.FromResult(ConfirmAnswer);
    }

    public Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> options, int dangerIndex = -1) => Task.FromResult(ChooseAnswer);

    public void Show(string message, bool isError = false) => Toasts.Add((message, isError));
}

/// <summary>Un teclado español de prueba: la tecla <c>Oem3</c> se llama «Ñ».</summary>
internal sealed class SpanishLabels : IKeyLabelProvider
{
    public string LayoutName => "español (prueba)";

    public string? GetLabel(string keyName) => keyName == "Oem3" ? "Ñ" : null;
}

/// <summary>Todo lo que necesitan los modelos de vista, montado sobre la configuración predeterminada de prueba y un almacén en memoria.</summary>
internal sealed class TestApp
{
    public TestApp(RadialConfiguration? configuration = null, bool canSave = true)
    {
        Store = new MemoryStore(configuration ?? StorageSamples.Defaults(), canSave);
        Session = new ConfigurationSession(Store, Store.LoadOrCreate());
        Services = new UiServices(Dialogs, Picker, Dialogs, new SpanishLabels())
        {
            Factory = () => FactoryLibrary.Create(new FactoryPaths(@"C:\Apps\Edge\msedge.exe", @"C:\Users\prueba\Documents", @"C:\Users\prueba\Downloads", @"C:\Users\prueba\Desktop", @"C:\Users\prueba\Pictures", "https://www.wikipedia.org")),
        };
    }

    public MemoryStore Store { get; }

    public ConfigurationSession Session { get; }

    public FakeDialogs Dialogs { get; } = new();

    public FakeFilePicker Picker { get; } = new();

    public UiServices Services { get; }

    public ConfigurationEditor Editor => Session.Editor;

    public LibraryViewModel NewLibrary() => new(Session, Services);

    public EditorViewModel NewEditor() => new(Session, Services);

    public SettingsViewModel NewSettings() => new(Session);

    public ShellViewModel NewShell(InAppDialogs? dialogs = null) => new(Session, Services, dialogs ?? new InAppDialogs(), new object());
}
