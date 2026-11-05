using Radial.Core.Editing;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Validation;
using Radial.Windows.Presentation;

namespace Radial.Windows.Tests.Presentation;

/// <summary>
/// El editor radial: jerarquía, ocho sectores, inspector y las operaciones de colocación y de grupos. Todo sin interfaz: lo que la vista
/// previa dibuja es <c>CurrentMenu</c>, así que lo que se comprueba aquí es lo que se vería.
/// </summary>
public class EditorViewModelTests
{
    // La configuración predeterminada de prueba: Copiar N, Pegar NE, Deshacer E, Seleccionar todo SE, Guardar S, grupo «Abrir» O.
    // Suroeste y Noroeste están libres. Dentro de «Abrir»: Carpeta N, Web E, Navegador S.
    private static EditorViewModel NewEditor(TestApp app) => app.NewEditor();

    private static void AssertValid(TestApp app) =>
        Assert.True(ConfigurationValidator.Validate(app.Editor.Current).IsValid, string.Join(" | ", ConfigurationValidator.Validate(app.Editor.Current).Errors.Select(e => e.Message)));

    private static MenuId GroupMenu(TestApp app, RadialDirection direction) =>
        ((OpenMenuAction)app.Editor.GetMenu(app.Editor.Current.RootMenuId)!.GetItem(direction)!.Action).TargetMenuId;

    [Fact]
    public void Initially_ShowsTheRootMenuWithEightSectors_AndTheWholeHierarchy()
    {
        var editor = NewEditor(new TestApp());

        Assert.Equal(8, editor.Sectors.Count);
        Assert.Equal(1, editor.Depth);
        Assert.False(editor.HasSelection);
        var root = Assert.Single(editor.Tree);
        Assert.True(root.IsRoot);
        Assert.Equal("Abrir", Assert.Single(root.Children).Name);
        Assert.Single(editor.Path);
        Assert.Equal(2, editor.Sectors.Count(s => s.IsEmpty));
    }

    [Fact]
    public void SelectingAnEmptySector_AndPlacingAnAction_UpdatesThePreviewAndTheSession()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        var invalidated = 0;
        editor.PreviewInvalidated += () => invalidated++;

        editor.SelectDirection(RadialDirection.SouthWest);
        Assert.True(editor.IsEmptySectorSelected);
        Assert.NotEmpty(editor.PickerResults);

        var result = editor.PlaceDefinition("edit.cut");

        Assert.True(result.Success, result.Message);
        Assert.Equal("Cortar", editor.SelectedItem?.Name);
        Assert.Equal("Cortar", editor.CurrentMenu.GetItem(RadialDirection.SouthWest)?.Name); // lo que dibuja la vista previa
        Assert.False(editor.Sectors.Single(s => s.Direction == RadialDirection.SouthWest).IsEmpty);
        Assert.True(invalidated > 0);
        Assert.True(app.Session.IsDirty);
        AssertValid(app);
    }

    [Fact]
    public void PlacingIntoAnOccupiedSector_FailsAndChangesNothing()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        var before = app.Editor.Current;

        var result = editor.PlaceDefinition("edit.cut", RadialDirection.North);

        Assert.False(result.Success);
        Assert.Equal(EditCodes.Occupied, result.Code);
        Assert.True(editor.StatusIsError);
        Assert.NotEmpty(editor.Status);
        Assert.Same(before, app.Editor.Current);
        Assert.Contains(app.Dialogs.Toasts, t => t.IsError);
    }

    [Fact]
    public void PlacingWithoutATarget_UsesTheSelectedFreeSector_OrTheFirstFreeOne()
    {
        var app = new TestApp();
        var editor = NewEditor(app);

        Assert.True(editor.PlaceDefinition("edit.cut").Success); // sin selección: el primer sector libre
        Assert.NotNull(editor.CurrentMenu.GetItem(RadialDirection.SouthWest));

        editor.SelectDirection(RadialDirection.NorthWest);
        Assert.True(editor.PlaceDefinition("edit.find").Success); // con un sector libre elegido: ese
        Assert.Equal("Buscar", editor.CurrentMenu.GetItem(RadialDirection.NorthWest)?.Name);

        var full = editor.PlaceDefinition("edit.new"); // ya no queda ninguno
        Assert.False(full.Success);
        Assert.Equal(EditCodes.Occupied, full.Code);
    }

    [Fact]
    public void TheSameActionCanBePlacedSeveralTimes_AndInSeveralMenus()
    {
        var app = new TestApp();
        var editor = NewEditor(app);

        Assert.True(editor.PlaceDefinition("edit.copy", RadialDirection.SouthWest).Success);
        Assert.True(editor.PlaceDefinition("edit.copy", RadialDirection.NorthWest).Success);
        editor.OpenMenu(GroupMenu(app, RadialDirection.West));
        Assert.True(editor.PlaceDefinition("edit.copy", RadialDirection.West).Success);

        Assert.Equal(4, app.Editor.PlacementsOf("edit.copy").Count); // la de fábrica y tres más
        Assert.Single(app.Editor.Current.Actions, a => a.Id == "edit.copy"); // una sola definición
        AssertValid(app);
    }

    [Fact]
    public void MovingASectorToAFreeOne_RelocatesIt_AndOntoAnOccupiedOne_SwapsThem()
    {
        var app = new TestApp();
        var editor = NewEditor(app);

        Assert.True(editor.MoveSector(RadialDirection.North, RadialDirection.SouthWest).Success);
        Assert.Equal("Copiar", editor.CurrentMenu.GetItem(RadialDirection.SouthWest)?.Name);
        Assert.Null(editor.CurrentMenu.GetItem(RadialDirection.North));

        Assert.True(editor.MoveSector(RadialDirection.NorthEast, RadialDirection.East).Success);
        Assert.Equal("Deshacer", editor.CurrentMenu.GetItem(RadialDirection.NorthEast)?.Name);
        Assert.Equal("Pegar", editor.CurrentMenu.GetItem(RadialDirection.East)?.Name);
        AssertValid(app);
    }

    [Fact]
    public void MovingAnEmptySector_Fails_WithAnExplanation()
    {
        var editor = NewEditor(new TestApp());

        var result = editor.MoveSector(RadialDirection.NorthWest, RadialDirection.North);

        Assert.False(result.Success);
        Assert.True(editor.StatusIsError);
    }

    [Fact]
    public void CreatingAGroup_AddsItToTheTree_SelectsItAndLetsYouEnterIt()
    {
        var app = new TestApp();
        var editor = NewEditor(app);

        var result = editor.CreateGroupAt();

        Assert.True(result.Success, result.Message);
        Assert.True(editor.IsGroupSelected);
        Assert.Equal(2, editor.Tree[0].Children.Count);

        editor.EnterSelectedGroup();

        Assert.Equal(2, editor.Depth);
        Assert.Equal(new[] { "Principal", "Grupo nuevo" }, editor.Path.Select(p => p.Name).ToArray());
        Assert.Equal(8, editor.Sectors.Count(s => s.IsEmpty));
        editor.GoUp();
        Assert.Equal(1, editor.Depth);
        AssertValid(app);
    }

    [Fact]
    public void ActionsCanBePlacedInsideAGroup_AndTheGroupIsNoLongerEmpty()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.CreateGroupAt(RadialDirection.SouthWest, "Mis cosas");
        editor.EnterSelectedGroup();

        Assert.True(editor.PlaceDefinition("edit.cut", RadialDirection.North).Success);
        Assert.True(editor.PlaceDefinition("edit.find", RadialDirection.South).Success);
        editor.GoUp();
        editor.SelectDirection(RadialDirection.SouthWest);

        Assert.Contains("2 elemento", editor.GroupContentsText);
        AssertValid(app);
    }

    [Fact]
    public void GroupsCannotBeNestedBeyondTheMaximumDepth()
    {
        var app = new TestApp(); // profundidad máxima 3: «Principal» › «Abrir» › un grupo más
        var editor = NewEditor(app);
        editor.OpenMenu(GroupMenu(app, RadialDirection.West));
        Assert.True(editor.CreateGroupAt(RadialDirection.West, "Nivel 3").Success);
        editor.EnterSelectedGroup();

        var tooDeep = editor.CreateGroupAt(RadialDirection.North, "Nivel 4");

        Assert.False(tooDeep.Success);
        Assert.Equal(EditCodes.TooDeep, tooDeep.Code);
        AssertValid(app);
    }

    [Fact]
    public async Task DeletingAGroupWithContent_AsksFirst_AndKeepsTheActionsInTheLibrary()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        var actionsBefore = app.Editor.Current.Actions.Count;
        editor.SelectDirection(RadialDirection.West);

        app.Dialogs.ConfirmAnswer = false;
        await editor.Remove.RunAsync();
        Assert.NotNull(editor.CurrentMenu.GetItem(RadialDirection.West)); // sin confirmar no se toca nada
        Assert.Contains("3", app.Dialogs.Confirmations[^1]);

        app.Dialogs.ConfirmAnswer = true;
        await editor.Remove.RunAsync();

        Assert.Null(editor.CurrentMenu.GetItem(RadialDirection.West));
        Assert.Equal(actionsBefore, app.Editor.Current.Actions.Count); // las definiciones siguen en la biblioteca
        Assert.Single(app.Editor.Current.Menus);
        AssertValid(app);
    }

    [Fact]
    public async Task DeletingAnEmptyGroup_NeedsNoConfirmation()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.CreateGroupAt(RadialDirection.SouthWest, "Vacío");
        app.Dialogs.Confirmations.Clear();

        await editor.Remove.RunAsync();

        Assert.Empty(app.Dialogs.Confirmations);
        Assert.Null(editor.CurrentMenu.GetItem(RadialDirection.SouthWest));
    }

    [Fact]
    public async Task RemovingAPlacement_KeepsTheDefinitionInTheLibrary()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.SelectDirection(RadialDirection.North);

        await editor.Remove.RunAsync();

        Assert.Null(editor.CurrentMenu.GetItem(RadialDirection.North));
        Assert.Contains(app.Editor.Current.Actions, a => a.Id == "edit.copy");
        AssertValid(app);
    }

    [Fact]
    public void DuplicatingAPlacement_UsesTheNextFreeSector_AndTheSameDefinition()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.SelectDirection(RadialDirection.North);

        editor.Duplicate.Execute(null);

        Assert.Equal("Copiar", editor.CurrentMenu.GetItem(RadialDirection.SouthWest)?.Name);
        Assert.Equal(2, app.Editor.PlacementsOf("edit.copy").Count);
        Assert.Equal(RadialDirection.SouthWest, editor.SelectedDirection);
        AssertValid(app);
    }

    [Fact]
    public void MovingAnActionToAnotherMenu_PutsItInTheFirstFreeSector()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        var itemId = editor.CurrentMenu.GetItem(RadialDirection.South)!.Id; // Guardar
        var openMenu = GroupMenu(app, RadialDirection.West);

        var result = editor.MoveToMenu(itemId, openMenu);

        Assert.True(result.Success, result.Message);
        Assert.Null(editor.CurrentMenu.GetItem(RadialDirection.South));
        Assert.Contains(app.Editor.GetMenu(openMenu)!.Items, i => i.Name == "Guardar");
        AssertValid(app);
    }

    [Fact]
    public void AGroupCannotBeMovedInsideItself()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        var groupItem = editor.CurrentMenu.GetItem(RadialDirection.West)!.Id;
        var inside = GroupMenu(app, RadialDirection.West);

        var result = editor.MoveToMenu(groupItem, inside);

        Assert.False(result.Success);
        AssertValid(app);
    }

    [Fact]
    public async Task ReplacingTheActionOfASector_KeepsTheSector_AndChangesWhatItDoes()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.SelectDirection(RadialDirection.North);

        var result = await editor.ReplaceSelectedAsync("edit.cut");

        Assert.True(result.Success, result.Message);
        Assert.Equal("Cortar", editor.CurrentMenu.GetItem(RadialDirection.North)?.Name);
        AssertValid(app);
    }

    [Fact]
    public async Task ReplacingAGroupWithContent_AsksFirst()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.SelectDirection(RadialDirection.West);

        app.Dialogs.ConfirmAnswer = false;
        var declined = await editor.ReplaceSelectedAsync("edit.cut");
        Assert.False(declined.Success);
        Assert.True(editor.IsGroupSelected);

        app.Dialogs.ConfirmAnswer = true;
        var accepted = await editor.ReplaceSelectedAsync("edit.cut");
        Assert.True(accepted.Success, accepted.Message);
        Assert.Equal("Cortar", editor.CurrentMenu.GetItem(RadialDirection.West)?.Name);
        AssertValid(app);
    }

    [Fact]
    public void RenamingAGroup_UpdatesTheTreeAndThePath()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.SelectDirection(RadialDirection.West);

        editor.LabelText = "Carpetas y webs";
        Assert.True(editor.CommitLabelText().Success);

        Assert.Equal("Carpetas y webs", editor.Tree[0].Children[0].Name);
        AssertValid(app);
    }

    [Fact]
    public void ChangingTheNameOfAPlacement_DoesNotChangeTheDefinition()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.SelectDirection(RadialDirection.North);

        editor.LabelText = "Copia rápida";
        Assert.True(editor.CommitLabelText().Success);

        Assert.Equal("Copia rápida", editor.CurrentMenu.GetItem(RadialDirection.North)?.Name);
        Assert.Equal("Copiar", app.Editor.Current.Actions.Single(a => a.Id == "edit.copy").Name);
        Assert.True(editor.HasLabelOverride);

        editor.ResetLabel.Execute(null);
        Assert.Equal("Copiar", editor.CurrentMenu.GetItem(RadialDirection.North)?.Name);
    }

    [Fact]
    public void AnInvalidName_IsRejected_AndTheTextGoesBackToWhatItWas()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.SelectDirection(RadialDirection.West);

        editor.LabelText = new string('x', 200);
        var result = editor.CommitLabelText();

        Assert.False(result.Success);
        Assert.Equal("Abrir", editor.LabelText);
    }

    [Fact]
    public void ChangingTheIcon_OfAPlacement_DoesNotTouchTheDefinition()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.SelectDirection(RadialDirection.North);
        var originalIcon = app.Editor.Current.Actions.Single(a => a.Id == "edit.copy").Icon;

        Assert.True(editor.SetSelectedIcon("Star").Success);

        Assert.Equal("Star", editor.InspectorIconName);
        Assert.Equal(originalIcon, app.Editor.Current.Actions.Single(a => a.Id == "edit.copy").Icon);
    }

    [Fact]
    public void ConvertingAnActionIntoAGroup_KeepsItsSector()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.SelectDirection(RadialDirection.North);

        editor.ConvertToGroup.Execute(null);

        Assert.True(editor.IsGroupSelected);
        Assert.Equal(2, editor.Tree[0].Children.Count);
        AssertValid(app);
    }

    [Fact]
    public void ChangesMadeInTheLibrary_ReachTheEditorImmediately()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        var invalidated = 0;
        editor.PreviewInvalidated += () => invalidated++;

        app.Editor.UpdateDefinition(app.Editor.Current.Actions.Single(a => a.Id == "edit.copy") with { Name = "Duplicar texto" });

        Assert.Equal("Duplicar texto", editor.CurrentMenu.GetItem(RadialDirection.North)?.Name);
        Assert.True(invalidated > 0);
    }

    [Fact]
    public void DeletingADefinition_FromTheLibrary_EmptiesItsSectorsInTheEditor()
    {
        var app = new TestApp();
        var editor = NewEditor(app);

        app.Editor.DeleteDefinition("edit.copy", removePlacements: true);

        Assert.Null(editor.CurrentMenu.GetItem(RadialDirection.North));
        AssertValid(app);
    }

    [Fact]
    public void DiscardingTheChanges_RestoresTheSavedMenu_AndNeverLeavesTheEditorOnAMissingMenu()
    {
        var app = new TestApp();
        var editor = NewEditor(app);
        editor.CreateGroupAt(RadialDirection.SouthWest, "Temporal");
        editor.EnterSelectedGroup();
        Assert.Equal(2, editor.Depth);

        app.Session.Discard();

        Assert.Equal(app.Session.Current.RootMenuId, editor.CurrentMenuId);
        Assert.Equal(1, editor.Depth);
        Assert.Null(editor.CurrentMenu.GetItem(RadialDirection.SouthWest));
        Assert.False(app.Session.IsDirty);
    }

    [Fact]
    public void ThePicker_FiltersByTheSearch_AndNeverOffersUnsupportedActions()
    {
        var app = new TestApp();
        var editor = NewEditor(app);

        editor.PickerSearch = "ctrl s";
        Assert.Equal("edit.save", editor.PickerResults[0].Id);

        editor.PickerSearch = "zzzzzz-sin-resultados";
        Assert.Empty(editor.PickerResults);
    }

    [Fact]
    public void OpeningAMenuThatDoesNotExist_IsIgnored()
    {
        var editor = NewEditor(new TestApp());

        editor.OpenMenu(new MenuId("no-existe"));

        Assert.Equal(1, editor.Depth);
    }

    [Fact]
    public void SelectingTheTreeNode_OpensThatMenu()
    {
        var app = new TestApp();
        var editor = NewEditor(app);

        editor.Tree[0].Children[0].IsSelected = true;

        Assert.Equal(GroupMenu(app, RadialDirection.West), editor.CurrentMenuId);
        Assert.Equal(2, editor.Depth);
    }
}
