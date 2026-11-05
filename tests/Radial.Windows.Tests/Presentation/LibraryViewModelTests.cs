using Radial.Core.Catalog;
using Radial.Core.Editing;
using Radial.Core.Model;
using Radial.Windows.Presentation;

namespace Radial.Windows.Tests.Presentation;

/// <summary>La biblioteca: búsqueda propia, filtros, secciones, indicador de «colocada» y crear, editar, duplicar y eliminar con seguridad.</summary>
public class LibraryViewModelTests
{
    private static void AddCustomUrl(TestApp app, string name, string url, params string[] tags)
    {
        var added = app.Editor.AddDefinition(new ActionDefinition(
            app.Editor.NewDefinitionId(name),
            name,
            ActionCategory.Browser,
            "Una web mía.",
            new IconReference(IconSource.Glyph, "Globe"),
            new OpenUrlAction(url),
            tags.Length == 0 ? null : tags));
        Assert.True(added.Success, added.Message);
    }

    [Fact]
    public void Initially_ShowsTheWholeLibrary_WithTheCustomSectionBeforeTheFactoryOne()
    {
        var app = new TestApp();
        AddCustomUrl(app, "Mi sitio", "https://example.com");
        var library = app.NewLibrary();

        Assert.Equal(app.Editor.Current.Actions.Count, library.Rows.Count);
        Assert.Equal("Mis acciones", library.Rows[0].SectionTitle);
        Assert.Equal("De fábrica", library.Rows[^1].SectionTitle);
        Assert.False(library.IsFiltering);
    }

    [Fact]
    public void SearchingByTheCombination_FindsTheShortcut_EvenWritingItLooseAndWithoutAccents()
    {
        var library = new TestApp().NewLibrary();

        library.SearchText = "ctrl s";

        Assert.Equal("edit.save", library.Rows[0].Id);
        Assert.True(library.IsFiltering);
    }

    [Fact]
    public void SearchingByTheKeyNameOfTheActiveLayout_FindsTheNtildeShortcut()
    {
        var app = new TestApp();
        var form = ActionFormViewModel.ForNew(app.Editor, app.Services, ActionKind.Shortcut);
        form.Name = "Atajo con eñe";
        form.Key.Control = true;
        form.Key.KeyName = "Oem3";
        Assert.True(form.Save().Success);
        var library = app.NewLibrary();

        library.SearchText = "ctrl ñ";

        Assert.Contains(library.Rows, r => r.Name == "Atajo con eñe");
    }

    [Fact]
    public void SearchingByCategoryTagAndDescription_Works()
    {
        var app = new TestApp();
        AddCustomUrl(app, "Mi sitio", "https://example.com", "etiquetaunica");
        var library = app.NewLibrary();

        library.SearchText = "etiquetaunica";
        Assert.Equal("Mi sitio", Assert.Single(library.Rows).Name);

        library.SearchText = "edicion"; // sin tilde: la categoría se llama «Edición»
        Assert.Contains(library.Rows, r => r.CategoryName == "Edición");
        Assert.Contains(library.Rows, r => r.Id == "edit.copy");
    }

    [Fact]
    public void TheFilters_CombineWithTheSearch_AndCanBeCleared()
    {
        var app = new TestApp();
        AddCustomUrl(app, "Mi sitio", "https://example.com");
        var library = app.NewLibrary();

        library.KindFilter = library.KindFilters.Single(f => f.Value == ActionKind.Url);
        Assert.All(library.Rows, r => Assert.Equal(ActionKind.Url, r.Kind));

        library.CategoryFilter = library.CategoryFilters.Single(f => f.Value == ActionCategory.Editing);
        Assert.Empty(library.Rows);
        Assert.False(library.HasResults);

        library.KindFilter = library.KindFilters[0];
        library.CategoryFilter = library.CategoryFilters[0];
        Assert.Equal(app.Editor.Current.Actions.Count, library.Rows.Count);
    }

    [Fact]
    public void ThePlacedIndicator_FollowsTheMenus()
    {
        var app = new TestApp();
        var library = app.NewLibrary();
        AddCustomUrl(app, "Mi sitio", "https://example.com");

        Assert.False(library.Rows.Single(r => r.Name == "Mi sitio").IsPlaced);
        Assert.True(library.Rows.Single(r => r.Id == "edit.copy").IsPlaced);

        library.PlacedFilter = library.PlacedFilters.Single(f => f.Value == false);
        Assert.All(library.Rows, r => Assert.False(r.IsPlaced));

        // Colocar la acción en un sector libre cambia el indicador sin recargar nada.
        var id = app.Editor.Current.Actions.Single(a => a.Name == "Mi sitio").Id;
        Assert.True(app.Editor.PlaceDefinition(app.Editor.Current.RootMenuId, Radial.Core.Geometry.RadialDirection.SouthWest, id).Success);
        library.PlacedFilter = library.PlacedFilters.Single(f => f.Value == true);

        Assert.Contains(library.Rows, r => r.Id == id && r.Placements == 1);
    }

    [Fact]
    public void CreatingAnActionFromTheForm_AddsItSelectsItAndMarksTheSessionDirty()
    {
        var app = new TestApp();
        var library = app.NewLibrary();
        Assert.False(app.Session.IsDirty);

        library.New.Execute(ActionKind.Url);
        Assert.True(library.IsFormOpen);
        library.Form!.Name = "Docs";
        library.Form.Url = "https://docs.example.com";
        library.SaveForm.Execute(null);

        Assert.False(library.IsFormOpen);
        Assert.Equal("Docs", library.Selected?.Name);
        Assert.True(app.Session.IsDirty);
    }

    [Fact]
    public void AnInvalidForm_StaysOpen_AndAddsNothing()
    {
        var app = new TestApp();
        var library = app.NewLibrary();
        var before = app.Editor.Current.Actions.Count;
        library.New.Execute(ActionKind.Url);
        library.Form!.Name = "Mala";
        library.Form.Url = "javascript:alert(1)";

        library.SaveForm.Execute(null);

        Assert.True(library.IsFormOpen);
        Assert.Equal(before, app.Editor.Current.Actions.Count);
        Assert.False(app.Session.IsDirty);
    }

    [Fact]
    public async Task CancellingAFormWithChanges_AsksFirst_AndKeepsItIfTheUserDeclines()
    {
        var app = new TestApp();
        var library = app.NewLibrary();
        library.New.Execute(ActionKind.Url);
        library.Form!.Name = "Algo";

        app.Dialogs.ConfirmAnswer = false;
        await library.CancelForm.RunAsync();
        Assert.True(library.IsFormOpen);

        app.Dialogs.ConfirmAnswer = true;
        await library.CancelForm.RunAsync();
        Assert.False(library.IsFormOpen);
    }

    [Fact]
    public void Duplicating_CreatesAnIndependentCopy_AndSelectsIt()
    {
        var app = new TestApp();
        var library = app.NewLibrary();
        library.SelectById("edit.copy");

        library.Duplicate.Execute(null);

        Assert.Equal("Copiar (copia)", library.Selected?.Name);
        Assert.NotEqual("edit.copy", library.Selected?.Id);
        Assert.False(library.Selected!.IsBuiltIn);
        Assert.False(library.Selected.IsPlaced);
    }

    [Fact]
    public async Task DeletingAnUnplacedAction_NeedsConfirmation_AndRemovesIt()
    {
        var app = new TestApp();
        AddCustomUrl(app, "Mi sitio", "https://example.com");
        var library = app.NewLibrary();
        library.SelectById(app.Editor.Current.Actions.Single(a => a.Name == "Mi sitio").Id);

        app.Dialogs.ConfirmAnswer = false;
        await library.Delete.RunAsync();
        Assert.Contains(app.Editor.Current.Actions, a => a.Name == "Mi sitio");

        app.Dialogs.ConfirmAnswer = true;
        await library.Delete.RunAsync();
        Assert.DoesNotContain(app.Editor.Current.Actions, a => a.Name == "Mi sitio");
    }

    [Fact]
    public async Task DeletingAPlacedAction_WarnsAboutThePlacements_AndRemovesThemToo()
    {
        var app = new TestApp();
        var library = app.NewLibrary();
        library.SelectById("edit.copy");
        Assert.NotEmpty(app.Editor.PlacementsOf("edit.copy"));

        app.Dialogs.ConfirmAnswer = false;
        await library.Delete.RunAsync();
        Assert.Contains(app.Editor.Current.Actions, a => a.Id == "edit.copy"); // sin confirmar no se toca nada
        Assert.NotEmpty(app.Editor.PlacementsOf("edit.copy"));
        Assert.Contains("colocada", app.Dialogs.Confirmations[^1]);

        app.Dialogs.ConfirmAnswer = true;
        await library.Delete.RunAsync();
        Assert.DoesNotContain(app.Editor.Current.Actions, a => a.Id == "edit.copy");
        Assert.Empty(app.Editor.PlacementsOf("edit.copy"));
        Assert.True(Radial.Core.Validation.ConfigurationValidator.Validate(app.Editor.Current).IsValid);
    }

    [Fact]
    public void AnUnsupportedAction_IsShown_ButCannotBeEditedOrDuplicated()
    {
        var unknown = new ActionDefinition("future.one", "Acción futura", ActionCategory.Custom, string.Empty, new IconReference(IconSource.Glyph, "Apps"), new UnknownAction("futureAction", "{\"type\":\"futureAction\"}"));
        var configuration = StorageSamplesWith(unknown);
        var app = new TestApp(configuration);
        var library = app.NewLibrary();

        library.SelectById("future.one");

        Assert.True(library.Selected!.IsUnsupported);
        Assert.False(library.Edit.CanExecute(null));
        Assert.False(library.Duplicate.CanExecute(null));
        Assert.True(library.Delete.CanExecute(null));
    }

    [Fact]
    public void RestoringTheFactoryActions_AddsOnlyTheMissingOnes()
    {
        var app = new TestApp();
        var library = app.NewLibrary();
        app.Editor.DeleteDefinition("edit.find", removePlacements: true);
        Assert.DoesNotContain(library.Rows, r => r.Id == "edit.find");

        library.RestoreFactory.Execute(null);

        Assert.Contains(library.Rows, r => r.Id == "edit.find");
    }

    [Fact]
    public void ChangesMadeElsewhere_RefreshTheListAndKeepTheSelection()
    {
        var app = new TestApp();
        var library = app.NewLibrary();
        library.SelectById("edit.paste");

        app.Editor.UpdateDefinition(app.Editor.Current.Actions.Single(a => a.Id == "edit.paste") with { Name = "Pegar aquí" });

        Assert.Equal("edit.paste", library.Selected?.Id);
        Assert.Equal("Pegar aquí", library.Selected?.Name);
    }

    [Fact]
    public void RequestingToPlace_RaisesTheEventWithTheId()
    {
        var library = new TestApp().NewLibrary();
        string? requested = null;
        library.PlaceRequested += id => requested = id;

        library.RequestPlace("edit.cut");

        Assert.Equal("edit.cut", requested);
    }

    private static RadialConfiguration StorageSamplesWith(ActionDefinition extra)
    {
        var baseConfiguration = Radial.Windows.Tests.Storage.StorageSamples.Defaults();
        return baseConfiguration with { Actions = [.. baseConfiguration.Actions, extra] };
    }
}
