using Radial.Core.Engine;
using Radial.Core.Execution;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Windows.Actions;
using Radial.Windows.Overlay;
using Radial.Windows.Storage;
using Radial.Windows.Tests.Actions;
using Radial.Windows.Tests.Overlay;
using Radial.Windows.Tests.Storage;

namespace Radial.Windows.Tests.Integration;

/// <summary>
/// De extremo a extremo, sin Windows real: el almacén en disco (carpeta temporal) → la configuración → el coordinador del overlay
/// con sus menús → los ejecutores reales de apertura con un lanzador falso. Comprueba que los subsistemas están conectados: primer
/// inicio, carga, entrada en grupos, tres niveles, retroceso, ejecución desde un submenú, reinicio con la misma configuración,
/// recuperación de un archivo corrupto, acciones desconocidas y versión futura. No inicia procesos ni envía teclas.
/// </summary>
public class PhaseFiveEndToEndTests : IDisposable
{
    private const double EnterDwell = 700; // más que los 600 ms predeterminados
    private const double BackDwell = 850; // más que los 800 ms predeterminados

    private readonly StorageTempDirectory _directory = new();
    private readonly TempFileSystem _files = new();
    private readonly FakeProcessLauncher _launcher = new();
    private readonly KeysExecutor _keys = new();
    private readonly TickingClock _storageClock = new();

    public void Dispose()
    {
        _files.Dispose();
        _directory.Dispose();
    }

    /// <summary>Atajos: se anotan, no se envía ninguna tecla.</summary>
    private sealed class KeysExecutor : IActionExecutor
    {
        public List<SendKeysAction> Sent { get; } = [];

        public bool CanExecute(RadialAction action) => action is SendKeysAction;

        public Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
        {
            Sent.Add((SendKeysAction)action);
            return Task.FromResult(ActionExecutionResult.Success);
        }
    }

    private CompositeActionExecutor Executors() => new(
    [
        new LaunchApplicationActionExecutor(_launcher),
        new OpenFileActionExecutor(_launcher),
        new OpenFolderActionExecutor(_launcher),
        new OpenUrlActionExecutor(_launcher),
        _keys,
    ]);

    private FileSettingsStore Store() => new(
        _directory.Root,
        _storageClock.Next,
        () => DefaultConfiguration.Create(@"C:\Apps\Edge\msedge.exe", _files.ExistingFolder, "https://www.wikipedia.org"));

    private CoordinatorHarness Harness(RadialConfiguration configuration) => new(configuration, executor: Executors());

    private static ScreenPoint Up(ScreenPoint p, int d = 120) => new(p.X, p.Y - d);

    private static ScreenPoint Down(ScreenPoint p, int d = 120) => new(p.X, p.Y + d);

    private static ScreenPoint Left(ScreenPoint p, int d = 120) => new(p.X - d, p.Y);

    private static ScreenPoint Right(ScreenPoint p, int d = 120) => new(p.X + d, p.Y);

    /// <summary>Activa el gesto y entra en el grupo «Abrir» (oeste) del menú predeterminado; devuelve el centro del submenú.</summary>
    private static ScreenPoint EnterOpenGroup(CoordinatorHarness h)
    {
        h.Activate();
        var group = Left(CoordinatorHarness.Start);
        h.Move(group);
        h.Dwell(EnterDwell);
        return group;
    }

    // El recorrido completo

    [Fact]
    public async Task FirstRun_Load_NavigateIntoAGroup_ExecuteFromTheSubmenu_Restart_AndDoItAgain()
    {
        // 1. Primer inicio: se crean las carpetas y se guarda la configuración predeterminada.
        var first = Store().LoadOrCreate();
        Assert.Equal(SettingsSource.CreatedDefaults, first.Source);
        Assert.True(File.Exists(Path.Combine(_directory.Root, "config.json")));

        // 2. Mostrar el menú principal y entrar en el grupo «Abrir».
        var h = Harness(first.Configuration);
        var center = EnterOpenGroup(h);
        Assert.Equal("Principal", h.Presenter.Menus[0].Menu.Name);
        Assert.Equal("Abrir", h.Presenter.Menus[^1].Menu.Name);
        Assert.Equal(2, h.Presenter.Menus[^1].Depth);
        Assert.Equal(1, h.Presenter.Shows);
        Assert.Equal(0, h.Presenter.Hides);

        // 3. Ejecutar una acción del submenú (Carpeta, al norte): abre la carpeta configurada con el ejecutor real.
        h.Release(Up(center));
        var (item, result) = await h.NextFinishedAsync();
        Assert.Equal("Carpeta", item.Name);
        Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
        Assert.Equal(_files.ExistingFolder, Assert.Single(_launcher.Requests).Target);
        Assert.False(h.Presenter.Visible);

        // 4. Una acción del menú principal en un gesto nuevo (Copiar, al norte).
        h.Activate();
        h.Release(Up(CoordinatorHarness.Start));
        var (copy, copyResult) = await h.NextFinishedAsync();
        Assert.Equal("Copiar", copy.Name);
        Assert.Equal(ActionExecutionStatus.Succeeded, copyResult.Status);
        Assert.Equal(new SendKeysAction(new KeyChord(KeyModifiers.Control, "C")), Assert.Single(_keys.Sent));

        // 5. «Cerrar» y reiniciar: la misma configuración, desde disco, y las mismas acciones.
        var second = Store().LoadOrCreate();
        Assert.Equal(SettingsSource.Loaded, second.Source);
        Assert.Equal(ConfigurationFile.Serialize(first.Configuration), ConfigurationFile.Serialize(second.Configuration));

        var again = Harness(second.Configuration);
        var centerAgain = EnterOpenGroup(again);
        again.Release(Right(centerAgain)); // Web, al este
        var (web, webResult) = await again.NextFinishedAsync();
        Assert.Equal("Web", web.Name);
        Assert.Equal(ActionExecutionStatus.Succeeded, webResult.Status);
        Assert.StartsWith("https://www.wikipedia.org", _launcher.Requests[^1].Target);
    }

    [Fact]
    public async Task ASavedThreeLevelConfiguration_IsRestoredAfterARestart_AndNavigatedToTheThirdLevel()
    {
        var main = new MenuId("main");
        var level2 = new MenuId("level2");
        var level3 = new MenuId("level3");
        var deepAction = new OpenFolderAction(_files.ExistingFolder);
        var custom = new RadialConfiguration
        {
            RootMenuId = main,
            Menus =
            [
                new RadialMenu(main, "Uno", [new RadialItem(new ItemId("main.next"), "Siguiente", RadialDirection.North, new OpenMenuAction(level2))]),
                new RadialMenu(level2, "Dos", [new RadialItem(new ItemId("two.next"), "Siguiente", RadialDirection.North, new OpenMenuAction(level3))]),
                new RadialMenu(level3, "Tres", [new RadialItem(new ItemId("three.open"), "Abrir", RadialDirection.North, deepAction, null, "files.folder")]),
            ],
        };

        var store = Store();
        Assert.True(store.Save(custom).Success);

        // Reinicio.
        var loaded = Store().LoadOrCreate();
        Assert.Equal(SettingsSource.Loaded, loaded.Source);
        var h = Harness(loaded.Configuration);

        h.Activate();
        var centerTwo = Up(CoordinatorHarness.Start);
        h.Move(centerTwo);
        h.Dwell(EnterDwell);
        var centerThree = Up(centerTwo);
        h.Move(centerThree);
        h.Dwell(EnterDwell);

        Assert.Equal(new[] { "Dos", "Tres" }, h.Presenter.Menus.Skip(1).Select(m => m.Menu.Name).ToArray());
        Assert.Equal(3, h.Presenter.Menus[^1].Depth);

        h.Release(Up(centerThree));
        var (item, result) = await h.NextFinishedAsync();
        Assert.Equal("files.folder", item.DefinitionId);
        Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
        Assert.Equal(_files.ExistingFolder, Assert.Single(_launcher.Requests).Target);
    }

    [Fact]
    public void GoingBackOutOfTheSubmenu_WorksWithTheLoadedConfiguration()
    {
        var h = Harness(Store().LoadOrCreate().Configuration);
        var center = EnterOpenGroup(h);

        h.Move(Down(center, 80)); // fuera del centro (sobre Navegador): arma el retroceso
        h.Move(center);
        h.Dwell(BackDwell);

        Assert.Equal("Principal", h.Presenter.Menus[^1].Menu.Name);
        Assert.Equal(1, h.Presenter.Menus[^1].Depth);
        Assert.Equal(center, h.Presenter.Menus[^1].Center);
        Assert.Equal(0, h.Presenter.Hides);
        Assert.Empty(_launcher.Requests);
    }

    // Archivos problemáticos

    [Fact]
    public async Task ACorruptConfigurationFile_DoesNotBringTheAppDown_AndTheRecoveredMenuWorks()
    {
        Directory.CreateDirectory(_directory.Root);
        File.WriteAllText(Path.Combine(_directory.Root, "config.json"), "{ esto no es una configuración");

        var loaded = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.DefaultsAfterProblem, loaded.Source);
        Assert.Single(Directory.GetFiles(_directory.Root, "config.corrupt-*.json"));
        Assert.Contains(loaded.Notices, n => n.Severity == SettingsNoticeSeverity.Warning);

        var h = Harness(loaded.Configuration);
        var center = EnterOpenGroup(h);
        h.Release(Up(center));
        var (item, result) = await h.NextFinishedAsync();
        Assert.Equal("Carpeta", item.Name);
        Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);

        // Y la configuración recuperada es la que quedó en disco para el siguiente inicio.
        Assert.Equal(SettingsSource.Loaded, Store().LoadOrCreate().Source);
    }

    [Fact]
    public async Task AnUnknownActionInTheConfiguration_IsKept_ButNeverExecuted()
    {
        var unknown = "{\"type\":\"futureAction\",\"nested\":{\"a\":[1,2,3]}}";
        Directory.CreateDirectory(_directory.Root);
        File.WriteAllText(
            Path.Combine(_directory.Root, "config.json"),
            "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":["
            + "{\"id\":\"future\",\"name\":\"Futura\",\"position\":\"North\",\"action\":" + unknown + "},"
            + "{\"id\":\"known\",\"name\":\"Conocida\",\"position\":\"East\",\"action\":{\"type\":\"openUrl\",\"url\":\"https://example.com\"}}]}]}");

        var loaded = Store().LoadOrCreate();
        var h = Harness(loaded.Configuration);

        Assert.Equal(SettingsSource.Loaded, loaded.Source);
        Assert.Contains(loaded.Notices, n => n.Severity == SettingsNoticeSeverity.Warning);
        Assert.False(Executors().CanExecute(Assert.Single(loaded.Configuration.Menus[0].Items, i => i.Id == new ItemId("future")).Action));

        h.Activate();
        h.Release(Up(CoordinatorHarness.Start)); // la acción desconocida

        Assert.Empty(_launcher.Requests);
        Assert.Empty(_keys.Sent);
        Assert.Equal(CancelReason.UnsupportedAction, Assert.Single(h.Completed).Reason);

        // La conocida de al lado sí funciona.
        h.Activate();
        h.Release(Right(CoordinatorHarness.Start));
        var (item, result) = await h.NextFinishedAsync();
        Assert.Equal("Conocida", item.Name);
        Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
        Assert.Single(_launcher.Requests);
    }

    [Fact]
    public void AFutureConfiguration_StartsWithTheDefaults_AndTheFileIsLeftIntact()
    {
        var future = "{\"schemaVersion\":42,\"datos\":{\"nuevos\":[1,2,3]}}";
        Directory.CreateDirectory(_directory.Root);
        var path = Path.Combine(_directory.Root, "config.json");
        File.WriteAllText(path, future);
        var store = Store();

        var loaded = store.LoadOrCreate();
        var h = Harness(loaded.Configuration);
        EnterOpenGroup(h); // el menú predeterminado funciona

        Assert.Equal(SettingsSource.DefaultsNotPersisted, loaded.Source);
        Assert.Equal("Abrir", h.Presenter.Menus[^1].Menu.Name);
        Assert.False(store.Save(loaded.Configuration).Success);
        Assert.Equal(future, File.ReadAllText(path));
    }

    [Fact]
    public void TheConfigurationOnDisk_IsTheSourceOfTruth_NotTheCode()
    {
        // Se edita el archivo a mano antes de arrancar: el menú que se muestra es el del archivo, no el predeterminado.
        Directory.CreateDirectory(_directory.Root);
        File.WriteAllText(Path.Combine(_directory.Root, "config.json"), StorageSamples.CustomJson("Editado a mano"));

        var loaded = Store().LoadOrCreate();
        var h = Harness(loaded.Configuration);
        h.Activate();

        Assert.Equal("Editado a mano", h.Presenter.Menus[0].Menu.Name);
        Assert.Single(h.Presenter.Menus[0].Menu.Items);
    }
}
