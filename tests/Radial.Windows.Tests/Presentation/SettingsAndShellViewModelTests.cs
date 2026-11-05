using Radial.Core.Editing;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Windows.Presentation;

namespace Radial.Windows.Tests.Presentation;

/// <summary>Los ajustes de navegación y el centro de control: guardado, cambios pendientes, confirmación al cerrar y búsqueda de comandos.</summary>
public class SettingsAndShellViewModelTests
{
    // ───────────────────────── Ajustes ─────────────────────────

    [Fact]
    public void ChangingASetting_UpdatesTheConfiguration_AndMarksTheSessionDirty()
    {
        var app = new TestApp();
        var settings = app.NewSettings();

        settings.SubmenuDwellMs = 450;
        settings.BackDwellMs = 1000;
        settings.MaxDepth = 4;

        var navigation = app.Editor.Current.Navigation;
        Assert.Equal(TimeSpan.FromMilliseconds(450), navigation.SubmenuDwell);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), navigation.BackDwell);
        Assert.Equal(4, navigation.MaxDepth);
        Assert.True(app.Session.IsDirty);
        Assert.Equal("450 ms", settings.SubmenuDwellText);
    }

    [Fact]
    public void ValuesOutsideTheAllowedRange_AreClampedNeverStored()
    {
        var app = new TestApp();
        var settings = app.NewSettings();

        settings.SubmenuDwellMs = 1;
        Assert.Equal(settings.MinDwellMs, settings.SubmenuDwellMs);

        settings.MaxDepth = 99;
        Assert.Equal(settings.MaxDepthLimit, settings.MaxDepth);
        Assert.True(Radial.Core.Validation.ConfigurationValidator.Validate(app.Editor.Current).IsValid);
    }

    [Fact]
    public void LoweringTheDepthBelowWhatTheGroupsUse_IsRefused_WithAnExplanation_AndTheControlGoesBack()
    {
        var app = new TestApp(); // «Principal» › «Abrir»: dos niveles en uso
        var settings = app.NewSettings();

        settings.MaxDepth = 1;

        Assert.Equal(3, app.Editor.Current.Navigation.MaxDepth);
        Assert.Equal(3, settings.MaxDepth);
        Assert.NotEmpty(settings.Message);
    }

    [Fact]
    public void RestoringTheDefaults_PutsTheStandardValuesBack()
    {
        var app = new TestApp();
        var settings = app.NewSettings();
        settings.SubmenuDwellMs = 300;

        settings.RestoreDefaults.Execute(null);

        Assert.Equal((int)new NavigationSettings().SubmenuDwell.TotalMilliseconds, settings.SubmenuDwellMs);
    }

    [Fact]
    public void TheSliderRange_NeverTrimsAValueTheConfigurationAlreadyHas()
    {
        var app = new TestApp();
        var settings = app.NewSettings();

        settings.SubmenuDwellMs = 5000;

        Assert.True(settings.MaxDwellMs >= 5000);
    }

    // ───────────────────────── Centro de control ─────────────────────────

    [Fact]
    public void InitialState_IsSaved_OnTheEditor()
    {
        var shell = new TestApp().NewShell();

        Assert.Equal(AppSection.Editor, shell.Section);
        Assert.Equal(SaveState.Saved, shell.SaveState);
        Assert.False(shell.Save.CanExecute(null));
        Assert.Equal(new[] { "Radial", "Editor radial", "Principal" }, shell.Breadcrumb.ToArray());
    }

    [Fact]
    public void AnEdit_MarksUnsavedChanges_AndSavingWritesOnce_AndCleansTheState()
    {
        var app = new TestApp();
        var shell = app.NewShell();
        shell.Editor.PlaceDefinition("edit.cut");

        Assert.Equal(SaveState.Unsaved, shell.SaveState);
        Assert.True(shell.Save.CanExecute(null));

        shell.Save.Execute(null);

        Assert.Equal(1, app.Store.Saves);
        Assert.Equal(SaveState.Saved, shell.SaveState);
        Assert.False(shell.IsDirty);
    }

    [Fact]
    public void SavingWithoutChanges_DoesNotWriteAnything()
    {
        var app = new TestApp();
        var shell = app.NewShell();

        shell.SaveNow();

        Assert.Equal(0, app.Store.Saves);
    }

    [Fact]
    public void UndoingAChangeByHand_LeavesTheSessionClean_SoNothingIsWrittenNeedlessly()
    {
        var app = new TestApp();
        var shell = app.NewShell();
        shell.Editor.PlaceDefinition("edit.cut");
        Assert.True(shell.IsDirty);

        shell.Editor.SelectDirection(RadialDirection.SouthWest);
        shell.Editor.Remove.Execute(null);

        Assert.False(shell.IsDirty);
        Assert.Equal(SaveState.Saved, shell.SaveState);
    }

    [Fact]
    public void AFailedSave_KeepsTheChangesInMemory_ShowsTheFailure_AndCanBeRetried()
    {
        var app = new TestApp();
        var shell = app.NewShell();
        shell.Editor.PlaceDefinition("edit.cut");
        app.Store.FailNext = true;

        shell.Save.Execute(null);

        Assert.Equal(SaveState.Failed, shell.SaveState);
        Assert.True(shell.IsDirty);
        Assert.Contains("Disco lleno", shell.SaveDetail);
        Assert.Equal("Cortar", shell.Editor.CurrentMenu.GetItem(RadialDirection.SouthWest)?.Name); // nada se perdió

        shell.Save.Execute(null);

        Assert.Equal(SaveState.Saved, shell.SaveState);
        Assert.Equal(1, app.Store.Saves);
    }

    [Fact]
    public void AReadOnlyConfiguration_CanBeEdited_ButNotSaved()
    {
        var app = new TestApp(canSave: false);
        var shell = app.NewShell();

        Assert.Equal(SaveState.ReadOnly, shell.SaveState);
        shell.Editor.PlaceDefinition("edit.cut");

        Assert.False(shell.Save.CanExecute(null));
        shell.SaveNow();
        Assert.Equal(0, app.Store.Saves);
    }

    [Fact]
    public async Task ClosingWithoutChanges_NeedsNoQuestion()
    {
        var dialogs = new InAppDialogs();
        var shell = new TestApp().NewShell(dialogs);

        Assert.True(await shell.ConfirmCloseAsync());
        Assert.False(dialogs.HasDialog);
    }

    [Theory]
    [InlineData(2, true, 1)] // guardar y salir
    [InlineData(1, true, 0)] // descartar y salir
    [InlineData(0, false, 0)] // seguir editando
    [InlineData(-1, false, 0)] // Esc: seguir editando
    public async Task ClosingWithChanges_AsksWhatToDo(int choice, bool expectedClose, int expectedSaves)
    {
        var app = new TestApp();
        var dialogs = new InAppDialogs();
        var shell = app.NewShell(dialogs);
        shell.Editor.PlaceDefinition("edit.cut");

        var closing = shell.ConfirmCloseAsync();
        Assert.True(dialogs.HasDialog); // se muestra el diálogo con tres opciones
        Assert.Equal(3, dialogs.CurrentDialog!.Options.Count);
        dialogs.CurrentDialog.Complete(choice);

        Assert.Equal(expectedClose, await closing);
        Assert.Equal(expectedSaves, app.Store.Saves);
    }

    [Fact]
    public async Task ClosingAndChoosingToSave_WhenSavingFails_DoesNotClose()
    {
        var app = new TestApp();
        var dialogs = new InAppDialogs();
        var shell = app.NewShell(dialogs);
        shell.Editor.PlaceDefinition("edit.cut");
        app.Store.FailNext = true;

        var closing = shell.ConfirmCloseAsync();
        dialogs.CurrentDialog!.Complete(2);

        Assert.False(await closing);
        Assert.True(shell.IsDirty);
    }

    [Fact]
    public async Task Discarding_AsksFirst_AndRestoresTheLastSavedState()
    {
        var app = new TestApp();
        var dialogs = new InAppDialogs();
        var shell = app.NewShell(dialogs);
        shell.Editor.PlaceDefinition("edit.cut");

        var discarding = shell.Discard.RunAsync();
        Assert.True(dialogs.HasDialog);
        dialogs.CurrentDialog!.Complete(1);
        await discarding;

        Assert.False(shell.IsDirty);
        Assert.Null(shell.Editor.CurrentMenu.GetItem(RadialDirection.SouthWest));
    }

    [Fact]
    public void Navigating_ChangesTheSection_TheBreadcrumb_AndThePage()
    {
        var shell = new TestApp().NewShell();

        shell.Navigate.Execute(AppSection.Library);

        Assert.Equal(AppSection.Library, shell.Section);
        Assert.Same(shell.Library, shell.CurrentPage);
        Assert.Equal(new[] { "Radial", "Biblioteca de acciones" }, shell.Breadcrumb.ToArray());

        shell.Go(AppSection.Diagnostics);
        Assert.Same(shell.Diagnostics, shell.CurrentPage);
    }

    [Fact]
    public void TheCommandSearch_FindsSectionsMenusAndCommands_AndRunningOneNavigates()
    {
        var shell = new TestApp().NewShell();

        shell.CommandText = "bibliot";
        Assert.Contains(shell.CommandResults, c => c.Title.Contains("Biblioteca", StringComparison.Ordinal));
        Assert.True(shell.IsCommandOpen);

        shell.RunCommand.Execute(shell.CommandResults.First(c => c.Title.Contains("Biblioteca", StringComparison.Ordinal)));
        Assert.Equal(AppSection.Library, shell.Section);
        Assert.False(shell.IsCommandOpen);
        Assert.Equal(string.Empty, shell.CommandText);

        shell.CommandText = "abrir el menú";
        var open = shell.CommandResults.First(c => c.Title == "Abrir el menú «Abrir»");
        shell.RunCommand.Execute(open);
        Assert.Equal(AppSection.Editor, shell.Section);
        Assert.Equal(2, shell.Editor.Depth);
    }

    [Fact]
    public void TheCommandSearch_OffersSaveOnlyWhenThereIsSomethingToSave()
    {
        var shell = new TestApp().NewShell();

        shell.CommandText = "guardar";
        Assert.DoesNotContain(shell.CommandResults, c => c.Title == "Guardar cambios");

        shell.Editor.PlaceDefinition("edit.cut");
        shell.CommandText = "guardar c";
        Assert.Contains(shell.CommandResults, c => c.Title == "Guardar cambios");
    }

    [Fact]
    public void AskingTheLibraryToPlaceAnAction_GoesToTheEditorAndPlacesIt()
    {
        var shell = new TestApp().NewShell();
        shell.Go(AppSection.Library);

        shell.Library.RequestPlace("edit.cut");

        Assert.Equal(AppSection.Editor, shell.Section);
        Assert.Equal("Cortar", shell.Editor.CurrentMenu.GetItem(RadialDirection.SouthWest)?.Name);
    }

    [Fact]
    public void TheNewActionCommand_OpensTheLibraryForm()
    {
        var shell = new TestApp().NewShell();

        shell.CommandText = "nueva acción: atajo";
        shell.RunCommand.Execute(shell.CommandResults[0]);

        Assert.Equal(AppSection.Library, shell.Section);
        Assert.True(shell.Library.IsFormOpen);
        Assert.Equal(ActionKind.Shortcut, shell.Library.Form!.Kind);
    }

    [Fact]
    public void ARecoveredConfiguration_ShowsABanner_ThatCanBeDismissed()
    {
        var configuration = Radial.Windows.Tests.Storage.StorageSamples.Defaults();
        var store = new MemoryStore(configuration);
        var loaded = new SettingsLoadResult(configuration, SettingsSource.RecoveredFromBackup, [new SettingsNotice(SettingsNoticeSeverity.Warning, "Se recuperó una copia.")], CanSave: true);
        var session = new ConfigurationSession(store, loaded);
        var shell = new ShellViewModel(session, new TestApp().Services, new InAppDialogs(), new object());

        Assert.True(shell.HasBanner);
        Assert.Contains("copia", shell.Banner);

        shell.DismissBanner.Execute(null);
        Assert.False(shell.HasBanner);
    }

    [Fact]
    public async Task TheDialogService_ReturnsTheChosenOption_AndQueuesASecondOneUntilTheFirstCloses()
    {
        var dialogs = new InAppDialogs();

        var first = dialogs.ConfirmAsync("Primero", "¿Seguro?", "Sí");
        Assert.Equal("Primero", dialogs.CurrentDialog!.Title);
        var second = dialogs.ConfirmAsync("Segundo", "¿Y ahora?", "Sí");
        Assert.Equal("Primero", dialogs.CurrentDialog!.Title); // el segundo espera

        dialogs.CurrentDialog.Complete(1);
        Assert.True(await first);
        for (var attempt = 0; attempt < 200 && dialogs.CurrentDialog?.Title != "Segundo"; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal("Segundo", dialogs.CurrentDialog!.Title);

        dialogs.CurrentDialog.Complete(0);
        Assert.False(await second);
        Assert.False(dialogs.HasDialog);
    }

    [Fact]
    public void ATimedToast_ShowsTheMessageAndCanBeDismissed()
    {
        var dialogs = new InAppDialogs();

        dialogs.Show("Hecho", isError: true);
        Assert.True(dialogs.HasToast);
        Assert.True(dialogs.ToastIsError);

        dialogs.DismissToast();
        Assert.False(dialogs.HasToast);
    }
}
