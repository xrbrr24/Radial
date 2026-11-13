using Radial.Core.Catalog;
using Radial.Core.Editing;
using Radial.Core.Model;
using Radial.Windows.Presentation;

namespace Radial.Windows.Tests.Presentation;

/// <summary>El formulario de los tipos de acción nuevos: insertar texto, volumen, ventana activa y la lista de argumentos.</summary>
public class NewActionFormTests
{
    private static ActionFormViewModel NewForm(TestApp app, ActionKind kind) => ActionFormViewModel.ForNew(app.Session, app.Services, kind);

    private static ActionDefinition SavedBy(TestApp app, string name) => app.Editor.Current.Actions.Single(a => a.Name == name);

    [Fact]
    public void TheNewKinds_AreOfferedWhenCreatingAnAction_AndInTheLibraryFilters()
    {
        Assert.Contains(ActionKind.Text, ActionFormViewModel.CreatableKinds);
        Assert.Contains(ActionKind.Volume, ActionFormViewModel.CreatableKinds);
        Assert.Contains(ActionKind.Window, ActionFormViewModel.CreatableKinds);
        Assert.Contains(ActionFormViewModel.KindOptions, o => o.Kind == ActionKind.Text && o.Name == "Insertar texto");

        var library = new LibraryViewModel(new TestApp().Session, new TestApp().Services);
        Assert.Contains(library.KindFilters, f => f.Value == ActionKind.Volume);
    }

    // Insertar texto

    [Fact]
    public void AnEmptyTextForm_CannotBeSaved_AndSaysWhy()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Text);
        form.Name = "Firma";

        var result = form.Save();

        Assert.False(result.Success);
        Assert.Contains(form.Problems, p => p.Contains("texto"));
        Assert.DoesNotContain(app.Editor.Current.Actions, a => a.Name == "Firma");
    }

    [Fact]
    public void ATextAction_IsSavedExactly_WithoutTrimmingOrChangingLineBreaks()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Text);
        form.Name = "Firma";
        form.Text = "  Un saludo,\r\nAna \t";

        var result = form.Save();

        Assert.True(result.Success, result.Message);
        var saved = SavedBy(app, "Firma");
        Assert.Equal(new TypeTextAction("  Un saludo,\r\nAna \t"), saved.Action);
        Assert.Equal(ActionCategory.Editing, saved.Category);
        Assert.Equal("Keyboard", saved.Icon.Value);
    }

    [Fact]
    public void ATextWithControlCharacters_IsRejectedByTheDomainRules()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Text);
        form.Name = "Mala";
        form.Text = "pitido\u0007";

        Assert.False(form.CanSave);
        Assert.False(form.Save().Success);
    }

    // Volumen

    [Fact]
    public void TheVolumeForm_StartsWithAnIncreaseOfFivePoints()
    {
        var form = NewForm(new TestApp(), ActionKind.Volume);

        Assert.Equal(VolumeOperation.Increase, form.VolumeOperation);
        Assert.Equal("5", form.VolumePercent);
        Assert.True(form.UsesVolumePercent);
    }

    [Theory]
    [InlineData(VolumeOperation.Increase, "10", 10)]
    [InlineData(VolumeOperation.Decrease, " 3 ", 3)]
    [InlineData(VolumeOperation.SetLevel, "0", 0)]
    [InlineData(VolumeOperation.SetLevel, "100", 100)]
    public void AVolumeAction_IsSavedWithItsNumber(VolumeOperation operation, string text, int expected)
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Volume);
        form.Name = "Volumen";
        form.VolumeOperation = operation;
        form.VolumePercent = text;

        var result = form.Save();

        Assert.True(result.Success, result.Message);
        var saved = SavedBy(app, "Volumen");
        Assert.Equal(new VolumeAction(operation, expected), saved.Action);
        Assert.Equal(ActionCategory.Multimedia, saved.Category);
    }

    [Theory]
    [InlineData(VolumeOperation.Increase, "")]
    [InlineData(VolumeOperation.Increase, "abc")]
    [InlineData(VolumeOperation.Increase, "-5")]
    [InlineData(VolumeOperation.Increase, "5.5")]
    [InlineData(VolumeOperation.Increase, "0")]
    [InlineData(VolumeOperation.Decrease, "101")]
    [InlineData(VolumeOperation.SetLevel, "150")]
    public void ABadVolumeNumber_BlocksSaving_AndNothingIsAdded(VolumeOperation operation, string text)
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Volume);
        form.Name = "Volumen";
        form.VolumeOperation = operation;
        form.VolumePercent = text;

        var result = form.Save();

        Assert.False(result.Success);
        Assert.NotEmpty(form.Problems);
        Assert.DoesNotContain(app.Editor.Current.Actions, a => a.Name == "Volumen");
    }

    [Fact]
    public void ToggleMute_NeedsNoNumber_EvenIfTheFieldHoldsGarbage()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Volume);
        form.Name = "Silencio";
        form.VolumePercent = "no es un número";
        form.VolumeOperation = VolumeOperation.ToggleMute;

        var result = form.Save();

        Assert.True(result.Success, result.Message);
        Assert.False(form.UsesVolumePercent);
        Assert.Equal(new VolumeAction(VolumeOperation.ToggleMute, 0), SavedBy(app, "Silencio").Action);
    }

    [Fact]
    public void TheVolumeNumberLabel_FollowsTheOperation()
    {
        var form = NewForm(new TestApp(), ActionKind.Volume);

        form.VolumeOperation = VolumeOperation.SetLevel;
        Assert.Contains("NIVEL", form.VolumePercentLabel);

        form.VolumeOperation = VolumeOperation.Decrease;
        Assert.Contains("DIFERENCIA", form.VolumePercentLabel);
    }

    // Ventana

    [Theory]
    [InlineData(WindowOperation.Minimize)]
    [InlineData(WindowOperation.Maximize)]
    [InlineData(WindowOperation.Restore)]
    [InlineData(WindowOperation.Close)]
    public void AWindowAction_IsSavedWithItsOperation(WindowOperation operation)
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Window);
        form.Name = "Ventana";
        form.WindowOperation = operation;

        var result = form.Save();

        Assert.True(result.Success, result.Message);
        var saved = SavedBy(app, "Ventana");
        Assert.Equal(new WindowControlAction(operation), saved.Action);
        Assert.Equal(ActionCategory.System, saved.Category);
    }

    // Editar lo ya guardado

    [Fact]
    public void EditingASavedAction_LoadsItsValues_AndSavingKeepsTheSameIdentity()
    {
        var app = new TestApp();
        var create = NewForm(app, ActionKind.Volume);
        create.Name = "Bajar";
        create.VolumeOperation = VolumeOperation.Decrease;
        create.VolumePercent = "15";
        Assert.True(create.Save().Success);
        var saved = SavedBy(app, "Bajar");

        var edit = ActionFormViewModel.ForEdit(app.Session, app.Services, saved);

        Assert.True(edit.IsVolume);
        Assert.Equal(VolumeOperation.Decrease, edit.VolumeOperation);
        Assert.Equal("15", edit.VolumePercent);

        edit.VolumePercent = "20";
        Assert.True(edit.Save().Success);
        Assert.Equal(new VolumeAction(VolumeOperation.Decrease, 20), SavedBy(app, "Bajar").Action);
        Assert.Equal(saved.Id, SavedBy(app, "Bajar").Id);
    }

    [Fact]
    public void EditingATextAndAWindowAction_LoadsTheirValues()
    {
        var app = new TestApp();
        var text = NewForm(app, ActionKind.Text);
        text.Name = "T";
        text.Text = "uno\r\ndos";
        Assert.True(text.Save().Success);
        var window = NewForm(app, ActionKind.Window);
        window.Name = "V";
        window.WindowOperation = WindowOperation.Close;
        Assert.True(window.Save().Success);

        var editText = ActionFormViewModel.ForEdit(app.Session, app.Services, SavedBy(app, "T"));
        var editWindow = ActionFormViewModel.ForEdit(app.Session, app.Services, SavedBy(app, "V"));

        Assert.True(editText.IsText);
        Assert.Equal("uno\r\ndos", editText.Text);
        Assert.True(editWindow.IsWindow);
        Assert.Equal(WindowOperation.Close, editWindow.WindowOperation);
    }

    // Argumentos

    [Fact]
    public void TheApplicationArguments_AreShownOnePerLine_AndSavedAsAList()
    {
        var app = new TestApp();
        var definition = new ActionDefinition(
            "app.test",
            "Prueba",
            ActionCategory.Applications,
            string.Empty,
            new IconReference(IconSource.Glyph, "Application"),
            new LaunchApplicationAction(@"C:\Apps\a.exe", ["--abrir", "mi documento.txt", "a & b"], @"C:\Trabajo"));
        Assert.True(app.Editor.AddDefinition(definition).Success);

        var form = ActionFormViewModel.ForEdit(app.Session, app.Services, definition);

        Assert.Equal(string.Join(Environment.NewLine, "--abrir", "mi documento.txt", "a & b"), form.Arguments);
        Assert.True(form.Save().Success);
        Assert.Equal(definition.Action, SavedBy(app, "Prueba").Action); // sin cambios: los mismos argumentos, con sus espacios y símbolos
    }

    [Fact]
    public void AnArgumentWithLineBreaks_CannotBeRoundTrippedByTheForm_SoItBlocksSavingUntilTheFieldIsEdited()
    {
        var app = new TestApp();
        var definition = new ActionDefinition(
            "app.salto",
            "Con salto",
            ActionCategory.Applications,
            string.Empty,
            new IconReference(IconSource.Glyph, "Application"),
            new LaunchApplicationAction(@"C:\Apps\a.exe", ["linea1\nlinea2"]));
        Assert.True(app.Editor.AddDefinition(definition).Success);

        var form = ActionFormViewModel.ForEdit(app.Session, app.Services, definition);

        Assert.False(form.CanSave);
        Assert.False(form.Save().Success);
        Assert.Equal(definition.Action, SavedBy(app, "Con salto").Action); // lo guardado no se tocó

        form.Arguments = "linea1";
        Assert.True(form.CanSave);
    }

    [Fact]
    public void BlankArgumentLines_AreIgnored_AndEachLineIsTrimmed()
    {
        var app = new TestApp();
        var form = NewForm(app, ActionKind.Application);
        form.Name = "Editor";
        form.Path = @"C:\Apps\editor.exe";
        form.Arguments = "\r\n  --a  \r\n\r\n\t\r\nb c\r\n";

        Assert.True(form.Save().Success);
        Assert.Equal(new LaunchApplicationAction(@"C:\Apps\editor.exe", ["--a", "b c"], null), SavedBy(app, "Editor").Action);
    }

    // Textos de la interfaz

    [Fact]
    public void TheSummaries_DescribeTheNewActionsInOneLine()
    {
        Assert.Equal("Sube el volumen 5 puntos", UiText.Summary(new VolumeAction(VolumeOperation.Increase, 5)));
        Assert.Equal("Baja el volumen 10 puntos", UiText.Summary(new VolumeAction(VolumeOperation.Decrease, 10)));
        Assert.Equal("Fija el volumen al 30 %", UiText.Summary(new VolumeAction(VolumeOperation.SetLevel, 30)));
        Assert.Equal("Activa o quita el silencio", UiText.Summary(new VolumeAction(VolumeOperation.ToggleMute)));
        Assert.Equal("Minimizar la ventana activa", UiText.Summary(new WindowControlAction(WindowOperation.Minimize)));
        Assert.Equal("Cerrar la ventana activa", UiText.Summary(new WindowControlAction(WindowOperation.Close)));

        var longText = UiText.Summary(new TypeTextAction("Primera línea\r\nSegunda línea que es larguísima y sigue y sigue y sigue"));
        Assert.DoesNotContain('\n', longText);
        Assert.DoesNotContain('\r', longText);
        Assert.EndsWith("…»", longText);
        Assert.StartsWith("Escribe «Primera línea", longText);

        Assert.Equal(
            @"C:\Apps\a.exe --x ""mi doc.txt""",
            UiText.Summary(new LaunchApplicationAction(@"C:\Apps\a.exe", ["--x", "mi doc.txt"])));
    }
}
