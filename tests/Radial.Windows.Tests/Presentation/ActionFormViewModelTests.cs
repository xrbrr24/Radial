using Radial.Core.Catalog;
using Radial.Core.Editing;
using Radial.Core.Model;
using Radial.Windows.Presentation;

namespace Radial.Windows.Tests.Presentation;

/// <summary>El formulario de una acción: por tipo, con validación del dominio, y sin guardar nunca una definición inválida.</summary>
public class ActionFormViewModelTests
{
    private static ActionFormViewModel NewForm(TestApp app, ActionKind kind) => ActionFormViewModel.ForNew(app.Editor, app.Services, kind);

    [Fact]
    public void AnEmptyShortcutForm_CannotBeSaved_AndSaysWhy()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Shortcut);
        var before = app.Editor.Current.Actions.Count;

        var result = form.Save();

        Assert.False(result.Success);
        Assert.Equal(before, app.Editor.Current.Actions.Count);
        Assert.NotEmpty(form.Problems);
        Assert.False(form.CanSave);
    }

    [Fact]
    public void AShortcutRecordedWithTheLayoutKey_IsSavedWithTheStableName()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Shortcut);
        form.Name = "Mi Ñ";
        form.Key.StartRecording();
        form.Key.HandleKey(0xC0, KeyModifiers.Control | KeyModifiers.Alt);

        var result = form.Save();

        Assert.True(result.Success, result.Message);
        var saved = app.Editor.Current.Actions.Single(a => a.Name == "Mi Ñ");
        Assert.Equal(new SendKeysAction(new KeyChord(KeyModifiers.Control | KeyModifiers.Alt, "Oem3")), saved.Action);
        Assert.False(saved.IsBuiltIn);
        Assert.Empty(app.Editor.PlacementsOf(saved.Id)); // se guarda sin colocar
    }

    [Fact]
    public void AnUrlWithAnotherScheme_IsRejected_AndNothingIsAdded()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Url);
        form.Name = "Correo";
        form.Url = "mailto:alguien@example.com";
        var before = app.Editor.Current.Actions.Count;

        var result = form.Save();

        Assert.False(result.Success);
        Assert.Equal(before, app.Editor.Current.Actions.Count);
        Assert.NotEmpty(form.Problems);
    }

    [Fact]
    public void AnHttpsUrl_IsSaved_AsAnUnplacedWebAction()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Url);
        form.Name = "Mi web";
        form.Url = " https://example.com/a?b=c ";

        var result = form.Save();

        Assert.True(result.Success, result.Message);
        var saved = app.Editor.Current.Actions.Single(a => a.Name == "Mi web");
        Assert.Equal(new OpenUrlAction("https://example.com/a?b=c"), saved.Action); // sin los espacios de alrededor
        Assert.Equal(ActionCategory.Browser, saved.Category);
    }

    [Fact]
    public void AnApplicationAction_KeepsPathArgumentsAndWorkingDirectory_AndBlanksBecomeNull()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Application);
        form.Name = "Editor";
        form.Path = @"C:\Apps\editor.exe";
        form.Arguments = "--nueva-ventana";
        form.WorkingDirectory = "   ";

        var result = form.Save();

        Assert.True(result.Success, result.Message);
        var saved = app.Editor.Current.Actions.Single(a => a.Name == "Editor");
        Assert.Equal(new LaunchApplicationAction(@"C:\Apps\editor.exe", "--nueva-ventana", null), saved.Action);
    }

    [Fact]
    public void PickingAFile_FillsThePath_AndSuggestsANameWhenThereIsNone()
    {
        var app = new TestApp();
        app.Picker.File = @"C:\Docs\Informe anual.pdf";
        var form = NewForm(app, ActionKind.File);

        form.Pick.Execute(null);

        Assert.Equal(@"C:\Docs\Informe anual.pdf", form.Path);
        Assert.Equal("Informe anual", form.Name);
    }

    [Fact]
    public void CancellingThePicker_ChangesNothing()
    {
        var app = new TestApp();
        app.Picker.Folder = null;
        var form = NewForm(app, ActionKind.Folder);
        form.Path = @"C:\Antes";

        form.Pick.Execute(null);

        Assert.Equal(@"C:\Antes", form.Path);
    }

    [Fact]
    public void TheTags_AreSplitByCommas_TrimmedAndDeduplicated()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Url);
        form.Name = "Etiquetada";
        form.Url = "https://example.com";
        form.TagsText = " trabajo , Trabajo,, texto ";

        Assert.True(form.Save().Success);

        var saved = app.Editor.Current.Actions.Single(a => a.Name == "Etiquetada");
        Assert.Equal(new[] { "trabajo", "texto" }, saved.TagList);
    }

    [Fact]
    public void Editing_KeepsTheId_TheBuiltInFlag_AndTheReferencesToIt()
    {
        var app = new TestApp();
        var copy = app.Editor.Current.Actions.Single(a => a.Id == "edit.copy");
        var placementsBefore = app.Editor.PlacementsOf("edit.copy").Count;
        var form = ActionFormViewModel.ForEdit(app.Editor, app.Services, copy);
        form.Name = "Copiar texto";
        form.Key.KeyName = "Insert";
        form.Key.Control = true;

        var result = form.Save();

        Assert.True(result.Success, result.Message);
        var edited = app.Editor.Current.Actions.Single(a => a.Id == "edit.copy");
        Assert.Equal("Copiar texto", edited.Name);
        Assert.True(edited.IsBuiltIn);
        Assert.Equal(placementsBefore, app.Editor.PlacementsOf("edit.copy").Count);
        Assert.All(app.Editor.PlacementsOf("edit.copy"), p => Assert.Equal("Copiar texto", p.Item.Name)); // el radial muestra el cambio
    }

    [Fact]
    public void AnUnknownActionCannotBeEdited()
    {
        var app = new TestApp();
        var unknown = new ActionDefinition("future.one", "Futura", ActionCategory.Custom, string.Empty, new IconReference(IconSource.Glyph, "Apps"), new UnknownAction("futureAction", "{\"type\":\"futureAction\"}"));

        Assert.Throws<InvalidOperationException>(() => ActionFormViewModel.ForEdit(app.Editor, app.Services, unknown));
    }

    [Fact]
    public void TheProblems_StayHiddenUntilTheUserTouchesSomething()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Url);

        Assert.Empty(form.Problems);
        Assert.False(form.HasChanges);

        form.Url = "ftp://x";

        Assert.NotEmpty(form.Problems);
        Assert.True(form.HasChanges);
    }

    [Fact]
    public void ANameThatIsTooLong_IsRejected()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Url);
        form.Name = new string('x', 200);
        form.Url = "https://example.com";

        Assert.False(form.Save().Success);
    }

    [Fact]
    public void TwoActionsWithTheSameName_GetDifferentIds()
    {
        var app = new TestApp();
        var first = NewForm(app, ActionKind.Url);
        first.Name = "Web";
        first.Url = "https://example.com";
        Assert.True(first.Save().Success);

        var second = NewForm(app, ActionKind.Url);
        second.Name = "Web";
        second.Url = "https://example.org";
        Assert.True(second.Save().Success);

        Assert.Equal(2, app.Editor.Current.Actions.Count(a => a.Name == "Web" && a.Action is OpenUrlAction { Url: "https://example.com" or "https://example.org" }));
        Assert.Equal(app.Editor.Current.Actions.Count, app.Editor.Current.Actions.Select(a => a.Id).Distinct().Count());
    }
}
