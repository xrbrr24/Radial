using Radial.Core.Catalog;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Windows.Presentation;
using Radial.Windows.Tests.Storage;

namespace Radial.Windows.Tests.Presentation;

/// <summary>
/// Bloque S1 (integridad del editor): lo escrito y no confirmado entra en el guardado y en «cambios sin guardar» sin depender del foco,
/// el formulario de una acción escribe siempre en la sesión vigente y las teclas que el grabador necesita no las consume la ventana.
/// Todo se comprueba sobre los modelos de vista (sin WPF). Lo que NO cubren —que la vista traduzca bien la tecla de WPF y que el evento
/// túnel llegue de verdad al grabador— queda para la validación manual o para pruebas con hilo STA (ver el informe del bloque).
/// </summary>
public class EditorIntegrityTests
{
    private static RadialDirection FirstActionSector(ShellViewModel shell) =>
        shell.Editor.Sectors.First(s => !s.IsEmpty && !s.IsGroup).Direction;

    private static RadialItem RootItem(TestApp app, RadialDirection direction)
    {
        Assert.True(app.Session.Current.TryGetMenu(app.Session.Current.RootMenuId, out var root));
        return root!.GetItem(direction)!;
    }

    // ───────────────────────── A1: etiquetas pendientes ─────────────────────────

    [Fact]
    public void ALabelTypedButNotCommitted_CountsAsUnsaved_AndEnablesSave()
    {
        var app = new TestApp();
        var shell = app.NewShell();
        shell.Editor.SelectDirection(FirstActionSector(shell));

        shell.Editor.LabelText = "Mi nombre";

        Assert.False(app.Session.IsDirty); // todavía no está en la configuración...
        Assert.True(shell.IsDirty); // ...pero cuenta como cambio sin guardar
        Assert.Equal(SaveState.Unsaved, shell.SaveState);
        Assert.True(shell.Save.CanExecute(null));
    }

    [Fact]
    public void ALabelTypedButNotCommitted_IsSaved_WithoutTheControlLosingFocus()
    {
        var app = new TestApp();
        var shell = app.NewShell();
        var direction = FirstActionSector(shell);
        shell.Editor.SelectDirection(direction);
        shell.Editor.LabelText = "Mi nombre";

        shell.Save.Execute(null); // Ctrl+S y el botón Guardar ejecutan este comando; nadie llama a CommitLabelText

        Assert.Equal(1, app.Store.Saves);
        var saved = app.Store.LastSaved!;
        Assert.True(saved.TryGetMenu(saved.RootMenuId, out var root));
        Assert.Equal("Mi nombre", root!.GetItem(direction)!.Name);
        Assert.Equal(SaveState.Saved, shell.SaveState);
        Assert.False(shell.IsDirty);
        Assert.False(shell.Editor.HasPendingEdits);
    }

    [Fact]
    public void ALabelTypedButNotCommitted_SurvivesHidingTheWindow_AndStillCountsAsUnsaved()
    {
        var app = new TestApp();
        var shell = app.NewShell();
        var direction = FirstActionSector(shell);
        shell.Editor.SelectDirection(direction);
        shell.Editor.LabelText = "Mi nombre";

        shell.CommitPending(); // lo que hace MainWindow antes de ocultar la ventana

        Assert.Equal("Mi nombre", RootItem(app, direction).Name); // el texto está en la sesión, no se pierde
        Assert.True(shell.IsDirty);
        Assert.Equal(SaveState.Unsaved, shell.SaveState);
        Assert.Equal(0, app.Store.Saves); // ocultar no guarda
    }

    [Theory]
    [InlineData(2, true, 1, "Mi nombre")] // guardar y salir: se guarda CON el nombre
    [InlineData(1, true, 0, null)] // descartar y salir
    [InlineData(0, false, 0, null)] // seguir editando
    public async Task ClosingWithALabelPending_AsksAboutUnsavedChanges(int choice, bool expectedClose, int expectedSaves, string? savedName)
    {
        var app = new TestApp();
        var dialogs = new InAppDialogs();
        var shell = app.NewShell(dialogs);
        var direction = FirstActionSector(shell);
        shell.Editor.SelectDirection(direction);
        shell.Editor.LabelText = "Mi nombre";

        var closing = shell.ConfirmCloseAsync();
        Assert.True(dialogs.HasDialog); // sin la corrección se cerraba sin preguntar y se perdía el nombre
        dialogs.CurrentDialog!.Complete(choice);

        Assert.Equal(expectedClose, await closing);
        Assert.Equal(expectedSaves, app.Store.Saves);
        if (savedName is not null)
        {
            Assert.Equal(savedName, RootItem(app, direction).Name);
        }
    }

    [Fact]
    public async Task ARevertedLabel_IsNotAChange_AndClosingNeedsNoQuestion()
    {
        var app = new TestApp();
        var dialogs = new InAppDialogs();
        var shell = app.NewShell(dialogs);
        var direction = FirstActionSector(shell);
        shell.Editor.SelectDirection(direction);
        var original = shell.Editor.LabelText;

        shell.Editor.LabelText = "Otro nombre";
        Assert.True(shell.IsDirty);
        shell.Editor.LabelText = original;

        Assert.False(shell.IsDirty);
        Assert.Equal(SaveState.Saved, shell.SaveState);
        Assert.True(await shell.ConfirmCloseAsync());
        Assert.False(dialogs.HasDialog);
    }

    [Fact]
    public void ARejectedPendingLabel_RestoresTheText_WritesNothing_AndKeepsTheStateConsistent()
    {
        var app = new TestApp();
        var shell = app.NewShell();
        shell.Editor.SelectDirection(RadialDirection.West); // el grupo «Abrir»: su nombre no puede quedar vacío
        var original = shell.Editor.LabelText;
        shell.Editor.LabelText = "   ";
        Assert.True(shell.IsDirty);

        shell.SaveNow();

        Assert.Equal(original, shell.Editor.LabelText);
        Assert.Equal(0, app.Store.Saves);
        Assert.False(shell.IsDirty);
        Assert.Equal(SaveState.Saved, shell.SaveState);
        Assert.Contains(app.Dialogs.Toasts, t => t.IsError); // se avisa del rechazo
    }

    // Una edición pendiente RECHAZADA (el nombre de un grupo no puede quedar vacío) no puede dejar pasar guardar, cerrar ni ocultar:
    // el usuario tiene que ver el aviso. Para que se vea que el bloqueo es por el rechazo y no por falta de cambios, la sesión tiene además
    // otro cambio sin guardar.

    private static (TestApp App, InAppDialogs Dialogs, ShellViewModel Shell) ShellWithAnotherChangeAndARejectedLabel()
    {
        var app = new TestApp();
        var dialogs = new InAppDialogs();
        var shell = app.NewShell(dialogs);
        Assert.True(shell.Editor.PlaceDefinition("edit.cut").Success); // otro cambio sin guardar
        shell.Editor.SelectDirection(RadialDirection.West); // el grupo «Abrir»
        shell.Editor.LabelText = "   ";
        Assert.True(shell.Editor.HasPendingEdits);
        return (app, dialogs, shell);
    }

    [Fact]
    public void ARejectedPendingLabel_DoesNotWriteToDisk_EvenWithOtherUnsavedChanges_AndTheNextSaveWorks()
    {
        var (app, dialogs, shell) = ShellWithAnotherChangeAndARejectedLabel();

        shell.Save.Execute(null); // Ctrl+S y el botón Guardar

        Assert.Equal(0, app.Store.Saves); // no se escribió nada
        Assert.False(dialogs.HasToast); // y tampoco se anunció «guardado»
        Assert.True(shell.IsDirty); // el otro cambio sigue pendiente
        Assert.Contains(app.Dialogs.Toasts, t => t.IsError); // el motivo se mostró
        Assert.False(shell.Editor.HasPendingEdits); // el texto rechazado volvió al nombre que había

        shell.Save.Execute(null); // con la edición ya resuelta, guardar funciona

        Assert.Equal(1, app.Store.Saves);
        Assert.False(shell.IsDirty);
    }

    [Fact]
    public async Task ARejectedPendingLabel_CancelsTheClose_AsksNothing_AndSavesNothing()
    {
        var (app, dialogs, shell) = ShellWithAnotherChangeAndARejectedLabel();

        var closed = await shell.ConfirmCloseAsync();

        Assert.False(closed); // la ventana sigue disponible
        Assert.False(dialogs.HasDialog); // no se pregunta por guardar mientras el error está sin resolver
        Assert.Equal(0, app.Store.Saves);
        Assert.True(shell.IsDirty); // no se perdió nada
        Assert.Contains(app.Dialogs.Toasts, t => t.IsError);
    }

    [Fact]
    public async Task ARejectedPendingLabel_CancelsTheClose_EvenWhenItIsTheOnlyChange_AndTheNextCloseWorks()
    {
        var app = new TestApp();
        var dialogs = new InAppDialogs();
        var shell = app.NewShell(dialogs);
        shell.Editor.SelectDirection(RadialDirection.West);
        shell.Editor.LabelText = "   "; // lo único pendiente, y se va a rechazar

        Assert.False(await shell.ConfirmCloseAsync()); // sin la corrección la sesión estaba limpia y se cerraba sin que nadie viera el error
        Assert.Contains(app.Dialogs.Toasts, t => t.IsError);

        Assert.True(await shell.ConfirmCloseAsync()); // resuelto el rechazo (el texto volvió al nombre), ya no hay nada que impida cerrar
        Assert.False(dialogs.HasDialog);
        Assert.Equal(0, app.Store.Saves);
    }

    [Fact]
    public void CommitPending_ReportsWhetherItIsSafeToHideTheWindow()
    {
        // MainWindow oculta la ventana solo si CommitPending() devuelve true. Esta prueba fija ese contrato del modelo de vista;
        // que MainWindow lo respete (no llamar a Hide() si devuelve false) es código de WPF y se valida a mano.
        var app = new TestApp();
        var shell = app.NewShell();
        Assert.True(shell.CommitPending()); // nada pendiente

        shell.Editor.SelectDirection(FirstActionSector(shell));
        shell.Editor.LabelText = "Mi nombre";
        Assert.True(shell.CommitPending()); // pendiente y aceptado
        Assert.Equal("Mi nombre", shell.Editor.CurrentMenu.GetItem(shell.Editor.SelectedDirection!.Value)!.Name);

        shell.Editor.SelectDirection(RadialDirection.West);
        var groupName = shell.Editor.LabelText;
        shell.Editor.LabelText = "   ";
        Assert.False(shell.CommitPending()); // pendiente y rechazado: NO se debe ocultar
        Assert.Equal(groupName, shell.Editor.LabelText);
        Assert.Contains(app.Dialogs.Toasts, t => t.IsError);
    }

    [Fact]
    public async Task DiscardingWithALabelPending_ThrowsItAway_AndLeavesNothingDirty()
    {
        var app = new TestApp();
        var dialogs = new InAppDialogs();
        var shell = app.NewShell(dialogs);
        var direction = FirstActionSector(shell);
        shell.Editor.SelectDirection(direction);
        var original = shell.Editor.LabelText;
        shell.Editor.LabelText = "Mi nombre";
        Assert.True(shell.Discard.CanExecute(null));

        var discarding = shell.Discard.RunAsync();
        dialogs.CurrentDialog!.Complete(1);
        await discarding;

        Assert.Equal(original, shell.Editor.LabelText);
        Assert.False(shell.IsDirty);
        Assert.Equal(SaveState.Saved, shell.SaveState);
        Assert.Equal(0, app.Store.Saves);
    }

    [Fact]
    public async Task AnOriginalNameWithLeadingAndTrailingSpaces_IsNotAPendingEdit_UntilTheUserChangesIt()
    {
        // Un config.json editado a mano puede tener «" Copiar "»: la validación no recorta los nombres.
        const string spaced = " Copiar ";
        var defaults = StorageSamples.Defaults();
        var configuration = LibraryResolver.Resolve(defaults with
        {
            Actions = defaults.Actions.Select(a => a.Id == "edit.copy" ? a with { Name = spaced } : a).ToList(),
        });
        var app = new TestApp(configuration);
        var dialogs = new InAppDialogs();
        var shell = app.NewShell(dialogs);

        shell.Editor.SelectDirection(RadialDirection.North); // «Copiar» (edit.copy)
        Assert.Equal(spaced, shell.Editor.LabelText); // el usuario no ha tocado nada

        Assert.False(shell.Editor.HasPendingEdits);
        Assert.False(shell.IsDirty);
        Assert.Equal(SaveState.Saved, shell.SaveState);
        Assert.False(shell.Save.CanExecute(null));
        shell.SaveNow();
        Assert.Equal(0, app.Store.Saves);
        Assert.True(await shell.ConfirmCloseAsync());
        Assert.False(dialogs.HasDialog);
        Assert.False(app.Session.IsDirty);
        Assert.Equal(spaced, RootItem(app, RadialDirection.North).Name); // el nombre original no se reescribió recortado

        // Una edición de verdad (aunque solo cambie los espacios) sigue contando como pendiente.
        shell.Editor.LabelText = spaced.Trim();
        Assert.True(shell.Editor.HasPendingEdits);
        Assert.True(shell.IsDirty);
        shell.Editor.LabelText = spaced; // volver al original exacto la deshace
        Assert.False(shell.IsDirty);
    }

    // ───────────────────────── A2: el formulario escribe en la sesión vigente ─────────────────────────

    private static void FillNewShortcut(ActionFormViewModel form, string name)
    {
        form.Name = name;
        form.Key.Control = true;
        form.Key.KeyName = "Q";
    }

    [Fact]
    public void DiscardingWhileANewFormIsOpen_ThenSaving_WritesToTheCurrentSession_AndTheSuccessIsTrue()
    {
        var app = new TestApp();
        var library = app.NewLibrary();
        app.Editor.RenameRootMenu("Cambio que se descarta"); // sesión con cambios
        library.New.Execute(ActionKind.Shortcut);
        var form = library.Form!;
        FillNewShortcut(form, "Tras descartar");

        app.Session.Discard(); // «Descartar» sustituye el editor de la sesión
        Assert.False(app.Session.IsDirty);
        library.SaveForm.Execute(null);

        Assert.Null(library.Form); // el formulario se cerró porque SE GUARDÓ
        Assert.Contains("se añadió", library.Status);
        Assert.Contains(app.Session.Current.Actions, a => a.Name == "Tras descartar"); // el mensaje de éxito es verdad
        Assert.True(app.Session.IsDirty); // y la sesión lo sabe: se puede guardar
        Assert.Contains(library.Rows, r => r.Name == "Tras descartar");
        Assert.NotEqual("Cambio que se descarta", app.Session.Current.Menus.First(m => m.Id == app.Session.Current.RootMenuId).Name); // lo descartado no volvió
    }

    [Fact]
    public void AfterDiscarding_AValidOperationChangesTheCurrentSession_AndItsDirtyState()
    {
        var app = new TestApp();
        var library = app.NewLibrary();
        app.Editor.RenameRootMenu("Cambio que se descarta");
        library.New.Execute(ActionKind.Shortcut); // formulario abierto mientras se descarta
        app.Session.Discard();
        var before = app.Session.Current.Actions.Count;
        library.SelectById("edit.copy");

        library.Duplicate.Execute(null);

        Assert.Equal(before + 1, app.Session.Current.Actions.Count);
        Assert.True(app.Session.IsDirty);
        Assert.Equal(before + 1, library.TotalCount);
    }

    [Fact]
    public void AFormForNewAction_SavedAfterDiscarding_CanBeSavedToDiskAndIsNotLost()
    {
        var app = new TestApp();
        var shell = app.NewShell();
        shell.Library.New.Execute(ActionKind.Shortcut);
        FillNewShortcut(shell.Library.Form!, "Persistente");
        app.Editor.RenameRootMenu("Cambio que se descarta");
        app.Session.Discard();

        shell.Library.SaveForm.Execute(null);
        shell.Save.Execute(null);

        Assert.Equal(1, app.Store.Saves);
        Assert.Contains(app.Store.LastSaved!.Actions, a => a.Name == "Persistente");
    }

    [Fact]
    public void EditingAnActionCreatedInDiscardedChanges_ReportsItInsteadOfAFalseSuccess()
    {
        var app = new TestApp();
        var created = ActionFormViewModel.ForNew(app.Session, app.Services, ActionKind.Shortcut);
        FillNewShortcut(created, "Efímera");
        Assert.True(created.Save().Success);
        var definition = app.Session.Current.Actions.Single(a => a.Name == "Efímera");
        var editing = ActionFormViewModel.ForEdit(app.Session, app.Services, definition);
        editing.Name = "Efímera renombrada";
        var saved = 0;
        editing.Saved += _ => saved++;

        app.Session.Discard(); // la acción nunca se guardó en disco: desaparece
        var result = editing.Save();

        Assert.False(result.Success);
        Assert.Equal(Radial.Core.Editing.EditCodes.NotFound, result.Code);
        Assert.Equal(0, saved); // ningún aviso de éxito
        Assert.NotEmpty(editing.SaveError);
        Assert.DoesNotContain(app.Session.Current.Actions, a => a.Id == definition.Id); // no se resucitó en silencio
        Assert.False(app.Session.IsDirty);
        Assert.Equal("Efímera renombrada", editing.Name); // el formulario conserva lo escrito
    }

    [Fact]
    public void AnEditFormIsAnIndependentDraft_SavingItAfterDiscarding_ReappliesItsValuesOnTheCurrentSession()
    {
        // Comportamiento fijado (R1): el formulario no se cierra ni se reconstruye al descartar la sesión.
        var app = new TestApp();
        var original = app.Session.Current.Actions.Single(a => a.Id == "edit.copy");
        var edited = original with { Name = "Copiar v2" };
        Assert.True(app.Editor.UpdateDefinition(edited).Success);
        var form = ActionFormViewModel.ForEdit(app.Session, app.Services, edited);
        form.Description = "Descripción del borrador";

        app.Session.Discard(); // el descarte revierte el nombre a «Copiar»
        Assert.Equal(original.Name, app.Session.Current.Actions.Single(a => a.Id == "edit.copy").Name);
        var result = form.Save();

        Assert.True(result.Success, result.Message);
        var current = app.Session.Current.Actions.Single(a => a.Id == "edit.copy");
        Assert.Equal("Copiar v2", current.Name); // los valores del borrador vuelven a la sesión vigente
        Assert.Equal("Descripción del borrador", current.Description);
        Assert.True(app.Session.IsDirty); // y la sesión lo sabe: se puede guardar o descartar de nuevo
    }

    // ───────────────────────── M2: Ctrl+S y Ctrl+K y el grabador ─────────────────────────

    private const int VkS = 0x53;
    private const int VkK = 0x4B;

    [Fact]
    public void OutsideRecording_CtrlS_AndCtrlK_KeepTheirGlobalMeaning()
    {
        var shell = new TestApp().NewShell();

        Assert.False(shell.IsRecordingShortcut);
        Assert.Equal(ShellViewModel.GlobalShortcut.Save, shell.ResolveGlobalShortcut(KeyModifiers.Control, VkS));
        Assert.Equal(ShellViewModel.GlobalShortcut.FocusCommands, shell.ResolveGlobalShortcut(KeyModifiers.Control, VkK));
    }

    [Theory]
    [InlineData(KeyModifiers.None, VkS)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, VkS)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, VkK)]
    [InlineData(KeyModifiers.Control, 0x41)] // Ctrl+A no es un atajo de la ventana
    [InlineData(KeyModifiers.Control, 0x1B)] // Escape
    public void OtherCombinations_AreNeverGlobalShortcuts(KeyModifiers modifiers, int virtualKey)
    {
        var shell = new TestApp().NewShell();

        Assert.Equal(ShellViewModel.GlobalShortcut.None, shell.ResolveGlobalShortcut(modifiers, virtualKey));
    }

    [Theory]
    [InlineData(VkS, "S")]
    [InlineData(VkK, "K")]
    public void WhileRecording_CtrlS_AndCtrlK_AreNotGlobalShortcuts_AndTheRecorderCapturesThem(int virtualKey, string expectedKey)
    {
        var app = new TestApp();
        var shell = app.NewShell();
        shell.Library.New.Execute(ActionKind.Shortcut);
        var recorder = shell.Library.Form!.Key;
        recorder.StartRecording();

        // La ventana no se queda la pulsación...
        Assert.True(shell.IsRecordingShortcut);
        Assert.Equal(ShellViewModel.GlobalShortcut.None, shell.ResolveGlobalShortcut(KeyModifiers.Control, virtualKey));

        // ...y el grabador la recoge como cualquier otra combinación.
        Assert.True(recorder.HandleKey(virtualKey, KeyModifiers.Control));
        Assert.Equal(KeyModifiers.Control, recorder.Modifiers);
        Assert.Equal(expectedKey, recorder.KeyName);
        Assert.NotNull(recorder.Chord);
        Assert.False(recorder.IsRecording);
        Assert.Equal(0, app.Store.Saves); // grabar Ctrl+S no guarda la configuración
    }

    [Fact]
    public void AfterRecording_TheGlobalShortcutsComeBack()
    {
        var shell = new TestApp().NewShell();
        shell.Library.New.Execute(ActionKind.Shortcut);
        var recorder = shell.Library.Form!.Key;
        recorder.StartRecording();
        recorder.HandleKey(VkS, KeyModifiers.Control);

        Assert.Equal(ShellViewModel.GlobalShortcut.Save, shell.ResolveGlobalShortcut(KeyModifiers.Control, VkS));

        recorder.StartRecording();
        recorder.StopRecording(); // Escape cancela la grabación
        Assert.Equal(ShellViewModel.GlobalShortcut.FocusCommands, shell.ResolveGlobalShortcut(KeyModifiers.Control, VkK));
    }

    [Fact]
    public void ClosingTheForm_WhileRecording_EndsTheRecording_AndRestoresTheGlobalShortcuts()
    {
        var shell = new TestApp().NewShell();
        shell.Library.New.Execute(ActionKind.Shortcut);
        shell.Library.Form!.Key.StartRecording();
        Assert.Equal(ShellViewModel.GlobalShortcut.None, shell.ResolveGlobalShortcut(KeyModifiers.Control, VkS));

        shell.Library.CancelForm.Execute(null);

        Assert.False(shell.IsRecordingShortcut);
        Assert.Equal(ShellViewModel.GlobalShortcut.Save, shell.ResolveGlobalShortcut(KeyModifiers.Control, VkS));
    }
}
