using Radial.Core.Catalog;
using Radial.Core.Editing;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Editing;

/// <summary>
/// El editor de la configuración: biblioteca, colocaciones y grupos. Cada operación se aplica entera o no se aplica (la
/// configuración queda exactamente igual) y el resultado siempre es una configuración válida: sin identificadores duplicados,
/// grupos huérfanos, referencias rotas, ciclos, profundidades superiores al límite ni dos acciones en el mismo sector.
/// </summary>
public class ConfigurationEditorTests
{
    private static readonly MenuId Main = new("main");
    private static readonly MenuId More = new("more");

    private static ActionDefinition Factory(string id) => FactoryActions.Shortcuts.Single(d => d.Id == id);

    private static ActionDefinition Custom(string id, string name) =>
        new(id, name, ActionCategory.Custom, string.Empty, new IconReference(IconSource.Glyph, "Link"), new OpenUrlAction("https://example.com/" + id));

    /// <summary>
    /// main: N copiar, E pegar, W grupo «Más» (→ more). more: S «Web». Biblioteca: copiar, pegar, cortar (sin colocar) y web.
    /// </summary>
    private static RadialConfiguration Fixture(int maxDepth = 3)
    {
        var copy = Factory("edit.copy");
        var paste = Factory("edit.paste");
        var cut = Factory("edit.cut");
        var web = Custom("custom.web", "Web");
        return new RadialConfiguration
        {
            RootMenuId = Main,
            Navigation = new NavigationSettings { MaxDepth = maxDepth },
            Actions = [copy, paste, cut, web],
            Menus =
            [
                new RadialMenu(
                    Main,
                    "Principal",
                    [
                        copy.ToItem(Main, RadialDirection.North),
                        paste.ToItem(Main, RadialDirection.East),
                        new RadialItem(new ItemId("main.more"), "Más", RadialDirection.West, new OpenMenuAction(More), new IconReference(IconSource.Glyph, "Apps")),
                    ]),
                new RadialMenu(More, "Más", [web.ToItem(More, RadialDirection.South)]),
            ],
        };
    }

    private static ConfigurationEditor Editor(int maxDepth = 3) => new(Fixture(maxDepth));

    private static void AssertValid(ConfigurationEditor editor)
    {
        var result = ConfigurationValidator.Validate(editor.Current);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors.Select(e => e.Message)));
        var ids = editor.Current.Menus.SelectMany(m => m.Items).Select(i => i.Id.Value).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    private static void AssertRejected(ConfigurationEditor editor, EditResult result, string code, RadialConfiguration before)
    {
        Assert.False(result.Success);
        Assert.Equal(code, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Same(before, editor.Current); // no cambió nada
    }

    private static RadialItem ItemAt(ConfigurationEditor editor, MenuId menu, RadialDirection position) =>
        editor.GetMenu(menu)!.GetItem(position)!;

    // Consultas

    [Fact]
    public void Depth_Height_Path_AndParentGroup_DescribeTheTree()
    {
        var editor = Editor();

        Assert.Equal(1, editor.DepthOf(Main));
        Assert.Equal(2, editor.DepthOf(More));
        Assert.Equal(0, editor.DepthOf(new MenuId("no-existe")));
        Assert.Equal(2, editor.HeightOf(Main));
        Assert.Equal(1, editor.HeightOf(More));
        Assert.Equal(new[] { Main, More }, editor.PathTo(More).ToArray());
        Assert.Equal(new[] { Main }, editor.PathTo(Main).ToArray());
        Assert.Empty(editor.PathTo(new MenuId("no-existe")));
        Assert.Equal(new ItemId("main.more"), editor.ParentGroupOf(More)!.Item.Id);
        Assert.Null(editor.ParentGroupOf(Main));
    }

    [Fact]
    public void PlacementsOf_ListsEveryPlacementOfADefinition()
    {
        var editor = Editor();
        Assert.True(editor.PlaceDefinition(More, RadialDirection.North, "edit.copy").Success);

        var placements = editor.PlacementsOf("edit.copy");

        Assert.Equal(new[] { Main, More }, placements.Select(p => p.Menu).ToArray());
        Assert.Empty(editor.PlacementsOf("edit.cut"));
    }

    // Biblioteca

    [Fact]
    public void AddingADefinition_StoresItWithoutPlacingItAnywhere()
    {
        var editor = Editor();
        var definition = Custom("custom.nueva", "Nueva");

        var result = editor.AddDefinition(definition);

        Assert.True(result.Success);
        Assert.Contains(editor.Current.Actions, a => a.Id == "custom.nueva");
        Assert.Empty(editor.PlacementsOf("custom.nueva"));
        AssertValid(editor);
    }

    [Fact]
    public void AddingADuplicateOrInvalidDefinition_IsRejected()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.AddDefinition(Custom("edit.copy", "Repetida")), EditCodes.Duplicate, before);
        AssertRejected(editor, editor.AddDefinition(Custom("Mal Id", "X")), EditCodes.Invalid, before);
        AssertRejected(editor, editor.AddDefinition(Custom("custom.ok", "") with { Name = "" }), EditCodes.Invalid, before);
        AssertRejected(editor, editor.AddDefinition(Custom("custom.ok", "Futura") with { Action = new UnknownAction("x", "{\"type\":\"x\"}") }), EditCodes.Invalid, before);
        AssertRejected(editor, editor.AddDefinition(Custom("custom.ok", "Grupo") with { Action = new OpenMenuAction(More) }), EditCodes.Invalid, before);
        AssertRejected(editor, editor.AddDefinition(Custom("custom.ok", "Url") with { Action = new OpenUrlAction("ftp://no.permitido") }), EditCodes.Invalid, before);
    }

    [Fact]
    public void UpdatingADefinition_RegeneratesAllItsPlacements_WithoutLosingReferences()
    {
        var editor = Editor();
        Assert.True(editor.PlaceDefinition(More, RadialDirection.North, "edit.copy").Success);
        var idsBefore = editor.PlacementsOf("edit.copy").Select(p => p.Item.Id).ToList();

        var edited = Factory("edit.copy") with { Name = "Copiar texto", Action = new SendKeysAction(new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, "C")) };
        var result = editor.UpdateDefinition(edited);

        Assert.True(result.Success);
        var placements = editor.PlacementsOf("edit.copy");
        Assert.Equal(idsBefore, placements.Select(p => p.Item.Id).ToList());
        Assert.All(placements, p =>
        {
            Assert.Equal("Copiar texto", p.Item.Name);
            Assert.Equal(edited.Action, p.Item.Action);
        });
        AssertValid(editor);
    }

    [Fact]
    public void UpdatingADefinition_KeepsTheOwnLabelsOfItsPlacements()
    {
        var editor = Editor();
        var north = ItemAt(editor, Main, RadialDirection.North);
        Assert.True(editor.SetLabel(north.Id, "Mi copia").Success);

        Assert.True(editor.UpdateDefinition(Factory("edit.copy") with { Name = "Otro nombre" }).Success);

        Assert.Equal("Mi copia", ItemAt(editor, Main, RadialDirection.North).Name);
        Assert.Equal("Otro nombre", editor.Current.Actions.Single(a => a.Id == "edit.copy").Name);
    }

    [Fact]
    public void UpdatingAMissingDefinition_OrToAnInvalidOne_IsRejected()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.UpdateDefinition(Custom("no.existe", "X")), EditCodes.NotFound, before);
        AssertRejected(editor, editor.UpdateDefinition(Factory("edit.copy") with { Name = " " }), EditCodes.Invalid, before);
        AssertRejected(editor, editor.UpdateDefinition(Factory("edit.copy") with { Action = new UnknownAction("x", "{\"type\":\"x\"}") }), EditCodes.Invalid, before);
    }

    [Fact]
    public void DeletingAPlacedDefinition_IsRefused_ExplainingWhereItIsUsed()
    {
        var editor = Editor();
        var before = editor.Current;

        var result = editor.DeleteDefinition("edit.copy", removePlacements: false);

        AssertRejected(editor, result, EditCodes.InUse, before);
        Assert.Contains("Principal", result.Message);
    }

    [Fact]
    public void DeletingAPlacedDefinition_WithConfirmation_RemovesItsPlacementsToo()
    {
        var editor = Editor();
        Assert.True(editor.PlaceDefinition(More, RadialDirection.North, "edit.copy").Success);

        var result = editor.DeleteDefinition("edit.copy", removePlacements: true);

        Assert.True(result.Success);
        Assert.DoesNotContain(editor.Current.Actions, a => a.Id == "edit.copy");
        Assert.Empty(editor.Current.Menus.SelectMany(m => m.Items).Where(i => i.DefinitionId == "edit.copy"));
        Assert.Single(editor.Current.Menus[0].Items, i => i.IsGroup); // los grupos no se tocan
        AssertValid(editor);
    }

    [Fact]
    public void DeletingAnUnplacedDefinition_NeedsNoConfirmation()
    {
        var editor = Editor();

        Assert.True(editor.DeleteDefinition("edit.cut", removePlacements: false).Success);

        Assert.DoesNotContain(editor.Current.Actions, a => a.Id == "edit.cut");
        Assert.Equal(EditCodes.NotFound, editor.DeleteDefinition("edit.cut", false).Code);
    }

    [Fact]
    public void DuplicatingADefinition_CreatesACustomCopy_NotPlacedAnywhere()
    {
        var editor = Editor();

        var first = editor.DuplicateDefinition("edit.copy", out var copy);
        var second = editor.DuplicateDefinition("edit.copy", out var copy2);

        Assert.True(first.Success);
        Assert.Equal("edit.copy-copia", copy!.Id);
        Assert.Equal("Copiar (copia)", copy.Name);
        Assert.False(copy.IsBuiltIn);
        Assert.Equal(copy.Action, Factory("edit.copy").Action);
        Assert.Empty(editor.PlacementsOf(copy.Id));
        Assert.True(second.Success);
        Assert.Equal("edit.copy-copia-2", copy2!.Id);
        Assert.False(editor.DuplicateDefinition("no.existe", out var none).Success);
        Assert.Null(none);
        AssertValid(editor);
    }

    [Fact]
    public void NewDefinitionId_IsDerivedFromTheName_AndUnique()
    {
        var editor = Editor();

        Assert.Equal("custom.mi-accion", editor.NewDefinitionId("Mi acción"));
        Assert.True(editor.AddDefinition(Custom("custom.mi-accion", "Mi acción")).Success);
        Assert.Equal("custom.mi-accion-2", editor.NewDefinitionId("Mi acción"));
    }

    [Fact]
    public void RestoreFactory_AddsOnlyTheMissingFactoryActions()
    {
        var editor = Editor();
        Assert.True(editor.DeleteDefinition("edit.cut", false).Success);

        var added = editor.RestoreFactory(FactoryActions.Shortcuts.Where(d => d.Id is "edit.cut" or "edit.copy" or "edit.undo"));

        Assert.Equal(2, added); // cut y undo; copy ya estaba
        Assert.Contains(editor.Current.Actions, a => a.Id == "edit.cut");
        Assert.Contains(editor.Current.Actions, a => a.Id == "edit.undo");
        Assert.Equal(0, editor.RestoreFactory(FactoryActions.Shortcuts.Where(d => d.Id == "edit.copy")));
        AssertValid(editor);
    }

    // Colocaciones

    [Fact]
    public void PlacingADefinition_InAFreeSector_LinksItToTheLibrary()
    {
        var editor = Editor();

        var result = editor.PlaceDefinition(Main, RadialDirection.South, "edit.cut");

        Assert.True(result.Success);
        var item = ItemAt(editor, Main, RadialDirection.South);
        Assert.Equal("edit.cut", item.DefinitionId);
        Assert.Equal("Cortar", item.Name);
        Assert.Equal(Factory("edit.cut").Action, item.Action);
        AssertValid(editor);
    }

    [Fact]
    public void PlacingInAnOccupiedSector_IsRejected_NamingWhoIsThere()
    {
        var editor = Editor();
        var before = editor.Current;

        var result = editor.PlaceDefinition(Main, RadialDirection.North, "edit.cut");

        AssertRejected(editor, result, EditCodes.Occupied, before);
        Assert.Contains("norte", result.Message);
        Assert.Contains("Copiar", result.Message);
    }

    [Fact]
    public void PlacingAMissingDefinitionOrInAMissingMenu_IsRejected()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.PlaceDefinition(Main, RadialDirection.South, "no.existe"), EditCodes.NotFound, before);
        AssertRejected(editor, editor.PlaceDefinition(new MenuId("no-existe"), RadialDirection.South, "edit.cut"), EditCodes.NotFound, before);
    }

    [Fact]
    public void AnUnknownActionInTheLibrary_CannotBePlaced()
    {
        var future = Custom("future", "Futura") with { Action = new UnknownAction("futureAction", "{\"type\":\"futureAction\"}") };
        var editor = new ConfigurationEditor(Fixture() with { Actions = [.. Fixture().Actions, future] });
        var before = editor.Current;

        AssertRejected(editor, editor.PlaceDefinition(Main, RadialDirection.South, "future"), EditCodes.NotAllowed, before);
    }

    [Fact]
    public void TheSameDefinition_CanBePlacedSeveralTimes_WithDistinctIds()
    {
        var editor = Editor();

        Assert.True(editor.PlaceDefinition(Main, RadialDirection.South, "edit.copy").Success);
        Assert.True(editor.PlaceDefinition(Main, RadialDirection.SouthEast, "edit.copy").Success);
        Assert.True(editor.PlaceDefinition(More, RadialDirection.North, "edit.copy").Success);

        var placements = editor.PlacementsOf("edit.copy");
        Assert.Equal(4, placements.Count);
        Assert.Equal(4, placements.Select(p => p.Item.Id).Distinct().Count());
        AssertValid(editor);
    }

    [Fact]
    public void PlacingWithAnExplicitIdThatExists_IsRejected()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.PlaceDefinition(Main, RadialDirection.South, "edit.cut", new ItemId("main.edit.copy")), EditCodes.Duplicate, before);
    }

    [Fact]
    public void RemovingAPlacement_KeepsTheDefinitionInTheLibrary()
    {
        var editor = Editor();

        var result = editor.RemovePlacement(new ItemId("main.edit.copy"));

        Assert.True(result.Success);
        Assert.Null(editor.GetMenu(Main)!.GetItem(RadialDirection.North));
        Assert.Contains(editor.Current.Actions, a => a.Id == "edit.copy");
        AssertValid(editor);
    }

    [Fact]
    public void RemovingAGroupWithRemovePlacement_IsRefused_ItNeedsDeleteGroup()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.RemovePlacement(new ItemId("main.more")), EditCodes.NotAllowed, before);
        AssertRejected(editor, editor.RemovePlacement(new ItemId("no.existe")), EditCodes.NotFound, before);
    }

    [Fact]
    public void MovingAnItem_ToAFreeSector_KeepsItsIdentity()
    {
        var editor = Editor();

        var result = editor.MoveItem(new ItemId("main.edit.copy"), Main, RadialDirection.South);

        Assert.True(result.Success);
        Assert.Null(editor.GetMenu(Main)!.GetItem(RadialDirection.North));
        Assert.Equal(new ItemId("main.edit.copy"), ItemAt(editor, Main, RadialDirection.South).Id);
        AssertValid(editor);
    }

    [Fact]
    public void MovingToTheSameSector_IsANoOp()
    {
        var editor = Editor();
        var before = editor.Current;

        var result = editor.MoveItem(new ItemId("main.edit.copy"), Main, RadialDirection.North);

        Assert.True(result.Success);
        Assert.Same(before, editor.Current);
    }

    [Fact]
    public void MovingToAnOccupiedSector_IsRejected_UnlessSwapping()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.MoveItem(new ItemId("main.edit.copy"), Main, RadialDirection.East), EditCodes.Occupied, before);

        var swap = editor.MoveItem(new ItemId("main.edit.copy"), Main, RadialDirection.East, swap: true);

        Assert.True(swap.Success);
        Assert.Equal("edit.paste", ItemAt(editor, Main, RadialDirection.North).DefinitionId);
        Assert.Equal("edit.copy", ItemAt(editor, Main, RadialDirection.East).DefinitionId);
        AssertValid(editor);
    }

    [Fact]
    public void SwappingAcrossMenus_IsNotAllowed()
    {
        var editor = Editor();
        Assert.True(editor.PlaceDefinition(More, RadialDirection.North, "edit.cut").Success);
        var before = editor.Current;

        AssertRejected(editor, editor.MoveItem(new ItemId("main.edit.copy"), More, RadialDirection.North, swap: true), EditCodes.NotAllowed, before);
    }

    [Fact]
    public void MovingAnItemToAnotherMenu_RemovesItFromTheFirst()
    {
        var editor = Editor();

        var result = editor.MoveItem(new ItemId("main.edit.copy"), More, RadialDirection.North);

        Assert.True(result.Success);
        Assert.Null(editor.GetMenu(Main)!.GetItem(RadialDirection.North));
        Assert.Equal("edit.copy", ItemAt(editor, More, RadialDirection.North).DefinitionId);
        AssertValid(editor);
    }

    [Fact]
    public void MovingToAMissingMenuOrOfAMissingItem_IsRejected()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.MoveItem(new ItemId("no.existe"), Main, RadialDirection.South), EditCodes.NotFound, before);
        AssertRejected(editor, editor.MoveItem(new ItemId("main.edit.copy"), new MenuId("no-existe"), RadialDirection.South), EditCodes.NotFound, before);
    }

    [Fact]
    public void DuplicatingAPlacement_CopiesItsOwnSettings_ToAFreeSector()
    {
        var editor = Editor();
        Assert.True(editor.SetLabel(new ItemId("main.edit.copy"), "Mi copia").Success);

        var result = editor.DuplicatePlacement(new ItemId("main.edit.copy"), RadialDirection.South);

        Assert.True(result.Success);
        var copy = ItemAt(editor, Main, RadialDirection.South);
        Assert.Equal("edit.copy", copy.DefinitionId);
        Assert.Equal("Mi copia", copy.LabelOverride);
        Assert.NotEqual(new ItemId("main.edit.copy"), copy.Id);
        AssertValid(editor);
    }

    [Fact]
    public void DuplicatingToAnOccupiedSector_OrDuplicatingAGroup_IsRejected()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.DuplicatePlacement(new ItemId("main.edit.copy"), RadialDirection.East), EditCodes.Occupied, before);
        AssertRejected(editor, editor.DuplicatePlacement(new ItemId("main.more"), RadialDirection.South), EditCodes.NotAllowed, before);
    }

    [Fact]
    public void ReplacingTheAction_OfAPlacement_KeepsItsSlot_AndDropsItsOwnSettings()
    {
        var editor = Editor();
        Assert.True(editor.SetLabel(new ItemId("main.edit.copy"), "Mi copia").Success);

        var result = editor.ReplaceAction(new ItemId("main.edit.copy"), "edit.cut");

        Assert.True(result.Success);
        var item = ItemAt(editor, Main, RadialDirection.North);
        Assert.Equal(new ItemId("main.edit.copy"), item.Id);
        Assert.Equal("edit.cut", item.DefinitionId);
        Assert.Equal("Cortar", item.Name);
        Assert.Null(item.LabelOverride);
        AssertValid(editor);
    }

    [Fact]
    public void ReplacingTheActionOfAGroup_OrWithAMissingAction_IsRejected()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.ReplaceAction(new ItemId("main.more"), "edit.cut"), EditCodes.NotAllowed, before);
        AssertRejected(editor, editor.ReplaceAction(new ItemId("main.edit.copy"), "no.existe"), EditCodes.NotFound, before);
    }

    [Fact]
    public void SettingAndClearingTheLabelAndIcon_OfAPlacement()
    {
        var editor = Editor();
        var id = new ItemId("main.edit.copy");

        Assert.True(editor.SetLabel(id, "  Duplicar  ").Success);
        Assert.True(editor.SetIcon(id, new IconReference(IconSource.Glyph, "Star")).Success);
        var item = ItemAt(editor, Main, RadialDirection.North);
        Assert.Equal("Duplicar", item.Name);
        Assert.Equal(new IconReference(IconSource.Glyph, "Star"), item.Icon);

        Assert.True(editor.SetLabel(id, "  ").Success);
        Assert.True(editor.SetIcon(id, null).Success);
        item = ItemAt(editor, Main, RadialDirection.North);
        Assert.Equal("Copiar", item.Name);
        Assert.Equal(Factory("edit.copy").Icon, item.Icon);
        Assert.Equal("edit.copy", Editor().Current.Actions.First(a => a.Id == "edit.copy").Id);
        AssertValid(editor);
    }

    [Fact]
    public void ALabelThatIsTooLong_IsRejected()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.SetLabel(new ItemId("main.edit.copy"), new string('x', ConfigurationLimits.MaxNameLength + 1)), EditCodes.Invalid, before);
    }

    // Grupos

    [Fact]
    public void CreatingAGroup_AddsTheItemAndAnEmptySubmenu()
    {
        var editor = Editor();

        var result = editor.CreateGroup(Main, RadialDirection.South, "Mis cosas");

        Assert.True(result.Success);
        var group = ItemAt(editor, Main, RadialDirection.South);
        Assert.True(group.IsGroup);
        Assert.Equal("Mis cosas", group.Name);
        var target = ((OpenMenuAction)group.Action).TargetMenuId;
        Assert.Equal("menu.mis-cosas", target.Value);
        Assert.Empty(editor.GetMenu(target)!.Items);
        Assert.Equal(2, editor.DepthOf(target));
        AssertValid(editor);
    }

    [Fact]
    public void CreatingTwoGroupsWithTheSameName_GivesUniqueIds()
    {
        var editor = Editor();

        Assert.True(editor.CreateGroup(Main, RadialDirection.South, "Nuevo").Success);
        Assert.True(editor.CreateGroup(Main, RadialDirection.SouthEast, "Nuevo").Success);

        var targets = editor.GetMenu(Main)!.Items.Where(i => i.IsGroup).Select(i => ((OpenMenuAction)i.Action).TargetMenuId.Value).ToList();
        Assert.Equal(3, targets.Distinct().Count());
        AssertValid(editor);
    }

    [Fact]
    public void CreatingAGroup_InAnOccupiedSector_OrWithoutAName_IsRejected()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.CreateGroup(Main, RadialDirection.North, "X"), EditCodes.Occupied, before);
        AssertRejected(editor, editor.CreateGroup(Main, RadialDirection.South, "  "), EditCodes.Invalid, before);
        AssertRejected(editor, editor.CreateGroup(new MenuId("no-existe"), RadialDirection.South, "X"), EditCodes.NotFound, before);
    }

    [Fact]
    public void CreatingAGroup_BeyondTheMaximumDepth_IsRejected_WithAHint()
    {
        var editor = Editor(maxDepth: 2);
        var before = editor.Current;

        var result = editor.CreateGroup(More, RadialDirection.North, "Demasiado hondo");

        AssertRejected(editor, result, EditCodes.TooDeep, before);
        Assert.Contains("Ajustes", result.Message);
    }

    [Fact]
    public void GroupsCanBeNested_UpToTheMaximumDepth()
    {
        var editor = Editor(maxDepth: 3);

        Assert.True(editor.CreateGroup(More, RadialDirection.North, "Nivel tres").Success);

        var third = ((OpenMenuAction)ItemAt(editor, More, RadialDirection.North).Action).TargetMenuId;
        Assert.Equal(3, editor.DepthOf(third));
        Assert.Equal(new[] { Main, More, third }, editor.PathTo(third).ToArray());
        Assert.False(editor.CreateGroup(third, RadialDirection.North, "Cuarto").Success);
        AssertValid(editor);
    }

    [Fact]
    public void RenamingAGroup_RenamesTheItemAndItsSubmenu()
    {
        var editor = Editor();

        var result = editor.RenameGroup(new ItemId("main.more"), "  Otros  ");

        Assert.True(result.Success);
        Assert.Equal("Otros", ItemAt(editor, Main, RadialDirection.West).Name);
        Assert.Equal("Otros", editor.GetMenu(More)!.Name);
        var before = editor.Current;
        AssertRejected(editor, editor.RenameGroup(new ItemId("main.more"), ""), EditCodes.Invalid, before);
        AssertRejected(editor, editor.RenameGroup(new ItemId("main.edit.copy"), "X"), EditCodes.NotFound, before); // no es un grupo
    }

    [Fact]
    public void RenamingTheRootMenu_Works_AndRejectsEmptyNames()
    {
        var editor = Editor();

        Assert.True(editor.RenameRootMenu("Inicio").Success);
        Assert.Equal("Inicio", editor.GetMenu(Main)!.Name);
        Assert.False(editor.RenameRootMenu(" ").Success);
    }

    [Fact]
    public void SettingTheLabelOfAGroup_RenamesIt()
    {
        var editor = Editor();

        Assert.True(editor.SetLabel(new ItemId("main.more"), "Renombrado").Success);

        Assert.Equal("Renombrado", editor.GetMenu(More)!.Name);
    }

    [Fact]
    public void DeletingANonEmptyGroup_IsRefused_UnlessCascading_AndNothingIsLost()
    {
        var editor = Editor();
        var before = editor.Current;

        var refused = editor.DeleteGroup(new ItemId("main.more"), GroupDeletion.RefuseIfNotEmpty);

        AssertRejected(editor, refused, EditCodes.GroupNotEmpty, before);
        Assert.Contains("1 elemento", refused.Message);
        Assert.Contains("biblioteca", refused.Message);

        var cascade = editor.DeleteGroup(new ItemId("main.more"), GroupDeletion.Cascade);

        Assert.True(cascade.Success);
        Assert.Null(editor.GetMenu(More));
        Assert.Null(editor.GetMenu(Main)!.GetItem(RadialDirection.West));
        Assert.Contains(editor.Current.Actions, a => a.Id == "custom.web"); // la acción sigue en la biblioteca
        AssertValid(editor);
    }

    [Fact]
    public void DeletingAnEmptyGroup_NeedsNoConfirmation()
    {
        var editor = Editor();
        Assert.True(editor.CreateGroup(Main, RadialDirection.South, "Vacío").Success);
        var group = ItemAt(editor, Main, RadialDirection.South);

        var result = editor.DeleteGroup(group.Id, GroupDeletion.RefuseIfNotEmpty);

        Assert.True(result.Success);
        Assert.Equal(2, editor.Current.Menus.Count);
        AssertValid(editor);
    }

    [Fact]
    public void DeletingAGroup_DeletesItsNestedSubmenus_ButNotTheOnesTheStillUse()
    {
        var editor = Editor();
        Assert.True(editor.CreateGroup(More, RadialDirection.North, "Hondo").Success);
        Assert.Equal(3, editor.Current.Menus.Count);

        Assert.True(editor.DeleteGroup(new ItemId("main.more"), GroupDeletion.Cascade).Success);

        Assert.Single(editor.Current.Menus); // «more» y su submenú anidado desaparecen juntos
        AssertValid(editor);
    }

    [Fact]
    public void ASubmenuStillUsedByAnotherGroup_IsKept_WhenOneOfTheGroupsIsDeleted()
    {
        var shared = Fixture();
        var second = new RadialItem(new ItemId("main.more2"), "Más otra vez", RadialDirection.NorthWest, new OpenMenuAction(More), new IconReference(IconSource.Glyph, "Apps"));
        var config = shared with { Menus = [shared.Menus[0] with { Items = [.. shared.Menus[0].Items, second] }, shared.Menus[1]] };
        var editor = new ConfigurationEditor(config);

        Assert.True(editor.DeleteGroup(new ItemId("main.more"), GroupDeletion.Cascade).Success);

        Assert.NotNull(editor.GetMenu(More)); // el otro grupo sigue abriéndolo
        Assert.Null(editor.GetMenu(Main)!.GetItem(RadialDirection.West));
        Assert.NotNull(editor.GetMenu(Main)!.GetItem(RadialDirection.NorthWest));
        AssertValid(editor);
    }

    [Fact]
    public void DescribeGroup_CountsWhatWouldBeDeleted()
    {
        var editor = Editor();
        Assert.True(editor.CreateGroup(More, RadialDirection.North, "Hondo").Success);

        var contents = editor.DescribeGroup(new ItemId("main.more"));

        Assert.Equal(new GroupContents(Menus: 2, Placements: 2, Groups: 1), contents);
        Assert.Equal(new GroupContents(0, 0, 0), editor.DescribeGroup(new ItemId("main.edit.copy")));
    }

    [Fact]
    public void DeletingAnItemThatIsNotAGroup_ThroughDeleteGroup_IsRejected()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.DeleteGroup(new ItemId("main.edit.copy"), GroupDeletion.Cascade), EditCodes.NotFound, before);
    }

    [Fact]
    public void MovingAGroup_ToAnotherSector_KeepsItsSubmenu()
    {
        var editor = Editor();

        Assert.True(editor.MoveItem(new ItemId("main.more"), Main, RadialDirection.South).Success);

        Assert.Equal("Más", ItemAt(editor, Main, RadialDirection.South).Name);
        Assert.Equal(2, editor.DepthOf(More));
        AssertValid(editor);
    }

    [Fact]
    public void AGroup_CannotBeMovedInsideItself_OrIntoItsOwnDescendants()
    {
        var editor = Editor();
        Assert.True(editor.CreateGroup(More, RadialDirection.North, "Hondo").Success);
        var deep = ((OpenMenuAction)ItemAt(editor, More, RadialDirection.North).Action).TargetMenuId;
        var before = editor.Current;

        AssertRejected(editor, editor.MoveItem(new ItemId("main.more"), More, RadialDirection.NorthEast), EditCodes.Cycle, before);
        AssertRejected(editor, editor.MoveItem(new ItemId("main.more"), deep, RadialDirection.North), EditCodes.Cycle, before);
    }

    [Fact]
    public void MovingAGroup_ThatWouldExceedTheMaximumDepth_IsRejected()
    {
        // main -> more -> hondo (profundidad 3, el máximo). Mover «more» (altura 2) dentro de «hondo» no cabe, ni dentro de un submenú de nivel 2.
        var editor = Editor(maxDepth: 3);
        Assert.True(editor.CreateGroup(Main, RadialDirection.South, "Otro").Success);
        var other = ((OpenMenuAction)ItemAt(editor, Main, RadialDirection.South).Action).TargetMenuId;
        Assert.True(editor.CreateGroup(More, RadialDirection.North, "Hondo").Success);
        var before = editor.Current;

        // «Otro» (altura 1) cabe dentro de «more» (nivel 2 → 3); «more» (altura 2) dentro de «Otro» (nivel 2) llegaría a 4.
        var result = editor.MoveItem(new ItemId("main.more"), other, RadialDirection.North);

        AssertRejected(editor, result, EditCodes.TooDeep, before);
        Assert.True(editor.MoveItem(ItemAt(editor, Main, RadialDirection.South).Id, More, RadialDirection.NorthEast).Success);
        AssertValid(editor);
    }

    [Fact]
    public void ConvertingAnActionToAGroup_KeepsTheSlot_AndTheActionStaysInTheLibrary()
    {
        var editor = Editor();

        var result = editor.ConvertToGroup(new ItemId("main.edit.paste"), "Pegados");

        Assert.True(result.Success);
        var group = ItemAt(editor, Main, RadialDirection.East);
        Assert.True(group.IsGroup);
        Assert.Equal(new ItemId("main.edit.paste"), group.Id);
        Assert.Equal("Pegados", group.Name);
        Assert.Contains(editor.Current.Actions, a => a.Id == "edit.paste");
        Assert.Null(group.DefinitionId);
        AssertValid(editor);
    }

    [Fact]
    public void ConvertingAGroupToAnAction_NeedsConfirmationIfItHasContent()
    {
        var editor = Editor();
        var before = editor.Current;

        AssertRejected(editor, editor.ConvertGroupToAction(new ItemId("main.more"), "edit.cut", deleteContents: false), EditCodes.GroupNotEmpty, before);

        var result = editor.ConvertGroupToAction(new ItemId("main.more"), "edit.cut", deleteContents: true);

        Assert.True(result.Success);
        var item = ItemAt(editor, Main, RadialDirection.West);
        Assert.Equal("edit.cut", item.DefinitionId);
        Assert.Null(editor.GetMenu(More));
        AssertValid(editor);
    }

    [Fact]
    public void ConvertingAnEmptyGroupToAnAction_Works_WithoutConfirmation()
    {
        var editor = Editor();
        Assert.True(editor.CreateGroup(Main, RadialDirection.South, "Vacío").Success);
        var group = ItemAt(editor, Main, RadialDirection.South);

        Assert.True(editor.ConvertGroupToAction(group.Id, "edit.cut", deleteContents: false).Success);

        Assert.Equal("edit.cut", ItemAt(editor, Main, RadialDirection.South).DefinitionId);
        AssertValid(editor);
    }

    [Fact]
    public void ConvertingToAGroup_BeyondTheMaximumDepth_IsRejected()
    {
        var editor = Editor(maxDepth: 2);
        var before = editor.Current;

        AssertRejected(editor, editor.ConvertToGroup(new ItemId("more.custom.web"), "X"), EditCodes.TooDeep, before);
        AssertRejected(editor, editor.ConvertToGroup(new ItemId("main.more"), "X"), EditCodes.NotAllowed, before);
    }

    // Ajustes

    [Fact]
    public void Navigation_CanBeChanged_WithinTheLimits()
    {
        var editor = Editor();

        var result = editor.SetNavigation(new NavigationSettings { SubmenuDwell = TimeSpan.FromMilliseconds(300), BackDwell = TimeSpan.FromMilliseconds(500), MaxDepth = 5 });

        Assert.True(result.Success);
        Assert.Equal(300, editor.Current.Navigation.SubmenuDwell.TotalMilliseconds);
        Assert.Equal(5, editor.Current.Navigation.MaxDepth);
    }

    [Fact]
    public void Navigation_CannotBeSetBelowTheExistingDepth_NorOutOfRange()
    {
        var editor = Editor(); // el árbol tiene 2 niveles
        var before = editor.Current;

        var tooShallow = editor.SetNavigation(new NavigationSettings { MaxDepth = 1 });
        AssertRejected(editor, tooShallow, EditCodes.Invalid, before);
        Assert.Contains("niveles", tooShallow.Message);
        AssertRejected(editor, editor.SetNavigation(new NavigationSettings { SubmenuDwell = TimeSpan.FromMilliseconds(1) }), EditCodes.Invalid, before);
        AssertRejected(editor, editor.SetNavigation(new NavigationSettings { BackDwell = TimeSpan.FromMinutes(5) }), EditCodes.Invalid, before);
        AssertRejected(editor, editor.SetNavigation(new NavigationSettings { MaxDepth = ConfigurationLimits.MaxDepthLimit + 1 }), EditCodes.Invalid, before);
    }

    [Fact]
    public void Geometry_IsValidated()
    {
        var editor = Editor();
        var before = editor.Current;

        Assert.True(editor.SetGeometry(new GeometrySettings { DeadZoneRadius = 50, HysteresisDegrees = 4 }).Success);
        var current = editor.Current;
        AssertRejected(editor, editor.SetGeometry(new GeometrySettings { DeadZoneRadius = 0 }), EditCodes.Invalid, current);
        Assert.NotSame(before, current);
    }

    // Eventos e invariantes

    [Fact]
    public void Changed_IsRaisedOnlyBySuccessfulEdits()
    {
        var editor = Editor();
        var raised = 0;
        editor.Changed += () => raised++;

        editor.PlaceDefinition(Main, RadialDirection.North, "edit.cut"); // ocupado
        Assert.Equal(0, raised);

        editor.PlaceDefinition(Main, RadialDirection.South, "edit.cut");
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ALongSequenceOfEdits_AlwaysLeavesAValidConfiguration()
    {
        var editor = Editor(maxDepth: 4);
        var directions = Enum.GetValues<RadialDirection>();

        for (var round = 0; round < 3; round++)
        {
            foreach (var direction in directions)
            {
                editor.PlaceDefinition(Main, direction, round % 2 == 0 ? "edit.cut" : "custom.web");
                editor.CreateGroup(More, direction, $"G{round}{direction}");
                editor.MoveItem(new ItemId("main.edit.copy"), More, direction, swap: round == 1);
                editor.DuplicateDefinition("edit.paste", out _);
                AssertValid(editor);
            }

            foreach (var menu in editor.Current.Menus.ToList())
            {
                foreach (var item in menu.Items.Where(i => i.IsGroup).ToList())
                {
                    editor.DeleteGroup(item.Id, round == 2 ? GroupDeletion.Cascade : GroupDeletion.RefuseIfNotEmpty);
                    AssertValid(editor);
                }
            }
        }

        AssertValid(editor);
        Assert.All(editor.Current.Menus, m => Assert.Equal(m.Items.Count, m.Items.Select(i => i.Position).Distinct().Count()));
    }

    [Fact]
    public void EveryMenuOfTheResult_IsReachableFromTheRoot_NoOrphanGroupsRemain()
    {
        var editor = Editor(maxDepth: 4);
        Assert.True(editor.CreateGroup(More, RadialDirection.North, "A").Success);
        Assert.True(editor.CreateGroup(Main, RadialDirection.South, "B").Success);
        Assert.True(editor.DeleteGroup(new ItemId("main.more"), GroupDeletion.Cascade).Success);

        Assert.All(editor.Current.Menus, m => Assert.True(editor.DepthOf(m.Id) >= 1, m.Id.Value));
        Assert.Empty(ConfigurationValidator.Validate(editor.Current).Warnings);
    }
}
