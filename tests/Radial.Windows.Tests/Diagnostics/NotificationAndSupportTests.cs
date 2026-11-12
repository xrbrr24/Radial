using Radial.Core.Diagnostics;
using Radial.Core.Editing;
using Radial.Core.Storage;
using Radial.Windows.Diagnostics;
using Radial.Windows.Storage;
using Radial.Windows.Tests.Presentation;
using Radial.Windows.Tests.Storage;

namespace Radial.Windows.Tests.Diagnostics;

/// <summary>
/// El enrutado de los avisos entre la ventana y la bandeja, con canales falsos: lo que se prueba es QUÉ canal se usa según el usuario
/// esté mirando la ventana o Radial esté oculto, y que ningún fallo de un canal se propague.
/// </summary>
public class NotificationRouterTests
{
    private static readonly UserNotice Notice = new(UserNoticeKind.Notice, "Radial", "una acción falló", "k");
    private static readonly UserNotice Problem = new(UserNoticeKind.Problem, "Radial: problema", "la captura se detuvo", "p");

    private sealed class Rig
    {
        public Rig()
        {
            Router = new NotificationRouter(Log, Presence, InApp, Tray);
        }

        public ListLog Log { get; } = new();

        public FakePresence Presence { get; } = new();

        public FakeSink InApp { get; } = new();

        public FakeSink Tray { get; } = new();

        public NotificationRouter Router { get; }
    }

    [Fact]
    public void WithTheWindowHidden_ANoticeGoesToTheTray_AndOnlyThere()
    {
        var rig = new Rig();
        rig.Presence.Looking = false;

        rig.Router.Notify(Notice);

        Assert.Equal(new[] { Notice }, rig.Tray.Shown);
        Assert.Empty(rig.InApp.Shown);
    }

    [Fact]
    public void WhileTheUserLooksAtTheWindow_ANoticeStaysInsideIt()
    {
        var rig = new Rig();
        rig.Presence.Looking = true;

        rig.Router.Notify(Notice);

        Assert.Equal(new[] { Notice }, rig.InApp.Shown);
        Assert.Empty(rig.Tray.Shown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AProblem_AlwaysGoesToBothChannels(bool looking)
    {
        var rig = new Rig();
        rig.Presence.Looking = looking;

        rig.Router.Notify(Problem);

        Assert.Equal(new[] { Problem }, rig.Tray.Shown);
        Assert.Equal(new[] { Problem }, rig.InApp.Shown);
    }

    [Fact]
    public void WhenTheTrayIsNotAvailable_ANoticeWithTheWindowHiddenUsesTheWindow()
    {
        var rig = new Rig();
        rig.Tray.Available = false;

        rig.Router.Notify(Notice);

        Assert.Equal(new[] { Notice }, rig.InApp.Shown);
        Assert.Equal(0, rig.Router.PendingCount);
    }

    [Fact]
    public void WhenTheChosenChannelFails_TheOtherOneIsUsed_AndTheFailureIsLogged()
    {
        var rig = new Rig();
        rig.Presence.Looking = false;
        rig.Tray.ThrowOnShow = true;

        var thrown = Record.Exception(() => rig.Router.Notify(Notice));

        Assert.Null(thrown);
        Assert.Equal(new[] { Notice }, rig.InApp.Shown);
        Assert.Contains(rig.Log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("bandeja", StringComparison.Ordinal) && entry.Exception is not null);
    }

    [Fact]
    public void AProblem_ReachesTheWindowEvenIfTheTrayFails()
    {
        var rig = new Rig();
        rig.Tray.ThrowOnShow = true;

        rig.Router.Notify(Problem);

        Assert.Equal(new[] { Problem }, rig.InApp.Shown);
        Assert.Equal(0, rig.Router.PendingCount);
    }

    [Fact]
    public void WhenNoChannelExists_TheNoticeWaits_AndIsDeliveredWhenOneAppears()
    {
        var rig = new Rig();
        rig.InApp.Available = false;
        rig.Tray.Available = false;

        rig.Router.Notify(Problem);

        Assert.Equal(1, rig.Router.PendingCount);
        Assert.Empty(rig.Tray.Shown);

        rig.Tray.Available = true; // la bandeja se crea al cargar la ventana
        rig.Router.DeliverPending();

        Assert.Equal(0, rig.Router.PendingCount);
        Assert.Equal(new[] { Problem }, rig.Tray.Shown);
    }

    [Fact]
    public void IfBothChannelsFail_NothingPropagates_AndTheNoticeWaits()
    {
        var rig = new Rig();
        rig.InApp.ThrowOnShow = true;
        rig.Tray.ThrowOnShow = true;

        var thrown = Record.Exception(() => rig.Router.Notify(Problem));

        Assert.Null(thrown);
        Assert.Equal(1, rig.Router.PendingCount);
        Assert.Equal(2, rig.Log.Entries.Count(entry => entry.Level == LogLevel.Warning));
    }

    [Fact]
    public void ABrokenPresence_IsTreatedAsNotLooking_WithoutFailing()
    {
        var rig = new Rig();
        rig.Presence.Throws = true;

        var thrown = Record.Exception(() => rig.Router.Notify(Notice));

        Assert.Null(thrown);
        Assert.Equal(new[] { Notice }, rig.Tray.Shown);
    }

    [Fact]
    public void TheWaitingNotices_AreBounded_KeepingTheNewest()
    {
        var rig = new Rig();
        rig.InApp.Available = false;
        rig.Tray.Available = false;

        for (var index = 0; index < NotificationRouter.MaxPending + 5; index++)
        {
            rig.Router.Notify(new UserNotice(UserNoticeKind.Notice, "Radial", "aviso " + index, "k" + index));
        }

        Assert.Equal(NotificationRouter.MaxPending, rig.Router.PendingCount);
        rig.Tray.Available = true;
        rig.Router.DeliverPending();

        Assert.Equal(NotificationRouter.MaxPending, rig.Tray.Shown.Count);
        Assert.Equal("aviso " + (NotificationRouter.MaxPending + 4), rig.Tray.Shown[^1].Message);
        Assert.DoesNotContain(rig.Tray.Shown, shown => shown.Message == "aviso 0");
    }

    [Fact]
    public void ANullNotice_IsIgnored()
    {
        var rig = new Rig();

        rig.Router.Notify(null!);

        Assert.Empty(rig.Tray.Shown);
        Assert.Empty(rig.InApp.Shown);
        Assert.Equal(0, rig.Router.PendingCount);
    }

    [Fact]
    public void AReporterWiredToTheRouter_TellsTheUserWithTheWindowHidden()
    {
        // De extremo a extremo con falsos: un fallo de una acción con Radial oculto acaba en un globo de la bandeja.
        var rig = new Rig();
        rig.Presence.Looking = false;
        var reporter = new ErrorReporter(rig.Log, rig.Router, new DiagClock());

        reporter.ReportProblem(ErrorImpact.Recoverable, "CaptureHost", "ejecutar una acción", "La acción x falló", "«X» no se pudo ejecutar: no se encontró la carpeta.", "action|TargetNotFound|x");

        var shown = Assert.Single(rig.Tray.Shown);
        Assert.Equal("«X» no se pudo ejecutar: no se encontró la carpeta.", shown.Message);
        Assert.Empty(rig.InApp.Shown);
    }
}

/// <summary>El marcador de «Radial terminó por un error grave» que se avisa en el siguiente arranque.</summary>
public sealed class FatalMarkerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "radial-marker-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // La limpieza temporal no puede hacer fallar una prueba.
        }
    }

    [Fact]
    public void ARecordedFatalError_IsReportedOnce_ThenCleared()
    {
        var marker = new FileFatalMarker(Path.Combine(_root, "logs"));

        marker.Record(new DateTimeOffset(2026, 11, 10, 14, 3, 0, TimeSpan.Zero), "UiThread, System.OutOfMemoryException, durante: pintar");

        var first = marker.TakePending();
        Assert.NotNull(first);
        Assert.Contains("OutOfMemoryException", first);
        Assert.Contains("pintar", first);
        Assert.Null(marker.TakePending());
        Assert.False(File.Exists(marker.MarkerPath));
    }

    [Fact]
    public void WithoutAMarker_ThereIsNothingToReport()
    {
        Assert.Null(new FileFatalMarker(Path.Combine(_root, "nada")).TakePending());
    }

    [Fact]
    public void LineBreaksInTheSummary_AreFlattened()
    {
        var marker = new FileFatalMarker(_root);

        marker.Record(DateTimeOffset.Now, "una\r\ndos\ntres");

        Assert.Equal(2, File.ReadAllLines(marker.MarkerPath).Length); // fecha y resumen
        Assert.Contains("una dos tres", marker.TakePending());
    }

    [Fact]
    public void AnEmptyMarkerFile_DoesNotFail()
    {
        Directory.CreateDirectory(_root);
        var marker = new FileFatalMarker(_root);
        File.WriteAllText(marker.MarkerPath, string.Empty);

        Assert.Equal("(sin detalles)", marker.TakePending());
    }

    [Fact]
    public void AFolderThatCannotBeWritten_NeverThrows_AndIsLogged()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "es-un-archivo");
        File.WriteAllText(blocker, "x");
        var log = new ListLog();
        var marker = new FileFatalMarker(Path.Combine(blocker, "logs"), log);

        var thrown = Record.Exception(() =>
        {
            marker.Record(DateTimeOffset.Now, "resumen");
            Assert.Null(marker.TakePending());
        });

        Assert.Null(thrown);
        Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Warning && entry.Exception is not null);
    }
}

/// <summary>El rastro de operaciones: qué se estaba haciendo cuando algo falló.</summary>
public class OperationTrailTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 10, 9, 8, 7, 6, TimeSpan.Zero);

    [Fact]
    public void BeginningAnOperation_SetsTheCurrentOne_AndDisposingRestoresThePrevious()
    {
        var trail = new OperationTrail(() => At);
        Assert.Null(trail.Current);

        using (trail.Begin("externa"))
        {
            Assert.Equal("externa", trail.Current);
            using (trail.Begin("interna"))
            {
                Assert.Equal("interna", trail.Current);
            }

            Assert.Equal("externa", trail.Current);
        }

        Assert.Null(trail.Current);
    }

    [Fact]
    public void DisposingTwice_DoesNotRestoreAStaleValue()
    {
        var trail = new OperationTrail(() => At);
        var outer = trail.Begin("externa");
        var inner = trail.Begin("interna");

        inner.Dispose();
        inner.Dispose();

        Assert.Equal("externa", trail.Current);
        outer.Dispose();
        Assert.Null(trail.Current);
    }

    [Fact]
    public void TheSnapshot_ListsTheLatestOperationsInOrder_WithTheirTime()
    {
        var trail = new OperationTrail(() => At, capacity: 3);

        trail.Note("uno");
        trail.Note("dos");
        trail.Note("tres");
        trail.Note("cuatro");

        var lines = trail.Snapshot()!.Split('\n', StringSplitOptions.TrimEntries);
        Assert.Equal(new[] { "09:08:07.006 dos", "09:08:07.006 tres", "09:08:07.006 cuatro" }, lines);
    }

    [Fact]
    public void WithoutOperations_TheSnapshotIsNull()
    {
        Assert.Null(new OperationTrail().Snapshot());
    }

    [Fact]
    public void BlankNotes_AreIgnored_AndBlankOperationsRejected()
    {
        var trail = new OperationTrail(() => At);

        trail.Note("  ");

        Assert.Null(trail.Snapshot());
        Assert.Throws<ArgumentException>(() => trail.Begin(" "));
    }

    [Fact]
    public async Task TheCurrentOperation_FollowsTheFlowAcrossAwaits_AndDoesNotLeakToOtherFlows()
    {
        var trail = new OperationTrail(() => At);

        async Task<string> Run(string name)
        {
            using (trail.Begin(name))
            {
                await Task.Delay(20);
                await Task.Yield();
                return trail.Current ?? "(nada)";
            }
        }

        var results = await Task.WhenAll(Run("uno"), Run("dos"), Run("tres"));

        Assert.Equal(new[] { "uno", "dos", "tres" }, results);
        Assert.Null(trail.Current);
    }
}

/// <summary>El banner de la ventana como canal de los problemas que comprometen el funcionamiento.</summary>
public class ShellProblemBannerTests
{
    [Fact]
    public void AProblem_IsShownInTheBanner_UntilItIsDismissed()
    {
        var shell = new TestApp().NewShell();
        Assert.False(shell.HasBanner);

        shell.ShowProblem("La captura se detuvo.");

        Assert.True(shell.HasBanner);
        Assert.Equal("La captura se detuvo.", shell.Banner);

        shell.DismissBanner.Execute(null);
        Assert.False(shell.HasBanner);
    }

    [Fact]
    public void TheSameProblemTwice_IsShownOnce()
    {
        var shell = new TestApp().NewShell();

        shell.ShowProblem("Mismo problema.");
        shell.ShowProblem("Mismo problema.");

        Assert.Equal("Mismo problema.", shell.Banner);
    }

    [Fact]
    public void DifferentProblems_AreAccumulated_UntilTheyWouldNotFit()
    {
        var shell = new TestApp().NewShell();

        shell.ShowProblem("Primero.");
        shell.ShowProblem("Segundo.");
        Assert.Equal("Primero. Segundo.", shell.Banner);

        var last = new string('x', 590) + ".";
        shell.ShowProblem(last);
        Assert.Equal(last, shell.Banner); // demasiado largo para acumular: manda el último
    }

    [Fact]
    public void ABlankProblem_IsIgnored()
    {
        var shell = new TestApp().NewShell();

        shell.ShowProblem("   ");

        Assert.False(shell.HasBanner);
    }

    [Fact]
    public void TheBanner_KeepsTheConfigurationNoticeAndAddsTheProblemAfterIt()
    {
        var recovered = new SettingsLoadResult(
            StorageSamples.Defaults(),
            SettingsSource.RecoveredFromBackup,
            [new SettingsNotice(SettingsNoticeSeverity.Warning, "Se recuperó una copia.")],
            CanSave: true);
        var store = new MemoryStore(recovered.Configuration);
        var shell = new Radial.Windows.Presentation.ShellViewModel(
            new ConfigurationSession(store, recovered),
            new TestApp().Services,
            new Radial.Windows.Presentation.InAppDialogs(),
            new object());

        shell.ShowProblem("Y además esto.");

        Assert.StartsWith("Se recuperó una copia.", shell.Banner);
        Assert.EndsWith("Y además esto.", shell.Banner);
    }
}

/// <summary>Los fallos de disco del almacén de configuración llegan al registro persistente, sin rutas ni contenido.</summary>
public class FileSettingsStoreLoggingTests : IDisposable
{
    private readonly StorageTempDirectory _directory = new();
    private readonly TickingClock _clock = new();
    private readonly ListLog _log = new();

    public void Dispose() => _directory.Dispose();

    private FileSettingsStore Store(string? root = null) =>
        new(root ?? _directory.Root, _clock.Next, StorageSamples.Defaults, _log);

    private string ConfigPath => Path.Combine(_directory.Root, "config.json");

    private void AssertNoPathsInTheLog() =>
        Assert.DoesNotContain(_log.Entries, entry => entry.Message.Contains(_directory.Path, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void ACorruptConfiguration_IsLoggedAsAWarning_WithoutItsPathOrContent()
    {
        Directory.CreateDirectory(_directory.Root);
        File.WriteAllText(ConfigPath, "{ esto no es json y contiene datos-del-usuario");

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.DefaultsAfterProblem, result.Source);
        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Warning && entry.Source == nameof(FileSettingsStore) && entry.Operation == "cargar configuración" && entry.Message.Contains("cuarentena", StringComparison.Ordinal));
        AssertNoPathsInTheLog();
        Assert.DoesNotContain(_log.Entries, entry => entry.Message.Contains("datos-del-usuario", StringComparison.Ordinal));
    }

    [Fact]
    public void ADataFolderThatCannotBeCreated_IsLoggedAsAnError_WithTheException()
    {
        var blocker = Path.Combine(_directory.Path, "es-un-archivo");
        File.WriteAllText(blocker, "x");

        var result = Store(Path.Combine(blocker, "Radial")).LoadOrCreate();

        Assert.False(result.CanSave);
        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Error && entry.Operation == "preparar carpeta de datos" && entry.Exception is not null);
    }

    [Fact]
    public void AConfigurationThatCannotBeRead_IsLoggedAsAnError()
    {
        Directory.CreateDirectory(_directory.Root);
        File.WriteAllText(ConfigPath, StorageSamples.CustomJson("Bloqueada"));
        SettingsLoadResult result;

        using (new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = Store().LoadOrCreate();
        }

        Assert.False(result.CanSave);
        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Error && entry.Operation == "cargar configuración" && entry.Exception is IOException);
    }

    [Fact]
    public void AFailedSave_IsLoggedAsAnError_AndStillReturnsTheFailure()
    {
        var store = Store();
        var loaded = store.LoadOrCreate();
        Assert.True(loaded.CanSave);
        var editor = new ConfigurationEditor(loaded.Configuration);
        Assert.True(editor.RenameRootMenu("Nombre nuevo").Success);
        SettingsSaveResult result;

        using (new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = store.Save(editor.Current);
        }

        Assert.False(result.Success);
        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Error && entry.Operation == "guardar configuración" && entry.Exception is not null);
        AssertNoPathsInTheLog();
    }

    [Fact]
    public void WithoutALog_TheStoreWorksAsBefore()
    {
        var store = new FileSettingsStore(_directory.Root, _clock.Next, StorageSamples.Defaults);

        var result = store.LoadOrCreate();

        Assert.Equal(SettingsSource.CreatedDefaults, result.Source);
    }
}
