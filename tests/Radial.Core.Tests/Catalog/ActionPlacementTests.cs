using Radial.Core.Catalog;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Catalog;

/// <summary>
/// Definiciones frente a colocaciones: una definición del repertorio (identificador estable) se puede colocar en varios menús y
/// posiciones; cada colocación (el elemento del menú) tiene su propio identificador, único en toda la configuración. Los grupos
/// son estructura del árbol de menús, no definiciones.
/// </summary>
public class ActionPlacementTests
{
    private static readonly MenuId Main = new("main");
    private static readonly MenuId Sub = new("sub");

    private static RadialConfiguration Configuration(params RadialMenu[] menus) =>
        new() { RootMenuId = Main, Actions = BuiltInShortcuts.All, Menus = menus };

    [Fact]
    public void ADerivedItemId_CombinesTheMenuAndTheDefinition()
    {
        var item = BuiltInShortcuts.Copy.ToItem(Main, RadialDirection.North);

        Assert.Equal(new ItemId("main.edit.copy"), item.Id);
        Assert.Equal("edit.copy", item.DefinitionId);
    }

    [Fact]
    public void TheSameDefinition_PlacedInTwoMenus_GetsTwoDistinctItemIds_AndTheConfigurationIsValid()
    {
        var copy = BuiltInShortcuts.Copy;
        var configuration = Configuration(
            new RadialMenu(Main, "Principal", [copy.ToItem(Main, RadialDirection.North), new RadialItem(new ItemId("main.sub"), "Más", RadialDirection.East, new OpenMenuAction(Sub))]),
            new RadialMenu(Sub, "Más", [copy.ToItem(Sub, RadialDirection.South)]));

        var first = configuration.Menus[0].Items[0];
        var second = configuration.Menus[1].Items[0];

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.DefinitionId, second.DefinitionId);
        Assert.Equal(first.Action, second.Action);
        Assert.True(ConfigurationValidator.Validate(configuration).IsValid);
    }

    [Fact]
    public void TheSameDefinition_PlacedTwiceInTheSameMenu_NeedsAnExplicitItemId()
    {
        var copy = BuiltInShortcuts.Copy;

        var clash = Configuration(new RadialMenu(Main, "Principal", [copy.ToItem(Main, RadialDirection.North), copy.ToItem(Main, RadialDirection.South)]));
        var explicitIds = Configuration(new RadialMenu(
            Main,
            "Principal",
            [copy.ToItem(new ItemId("main.copy-1"), RadialDirection.North), copy.ToItem(new ItemId("main.copy-2"), RadialDirection.South)]));

        Assert.Contains(ConfigurationValidator.Validate(clash).Errors, e => e.Code == ValidationCodes.ItemDuplicateId);
        Assert.True(ConfigurationValidator.Validate(explicitIds).IsValid);
    }

    [Fact]
    public void ReusingADefinition_ManyTimes_NeverMutatesIt()
    {
        var copy = BuiltInShortcuts.Copy;

        var items = Enumerable.Range(0, 5).Select(n => copy.ToItem(new ItemId($"menu{n}.copy"), (RadialDirection)n)).ToList();

        Assert.Equal(5, items.Select(i => i.Id).Distinct().Count());
        Assert.All(items, i => Assert.Equal("edit.copy", i.DefinitionId));
        Assert.All(items, i => Assert.Same(copy.Action, i.Action)); // se comparte la misma acción inmutable
        Assert.Equal("Copiar", copy.Name);
    }

    [Fact]
    public void ADefinitionIdIsStable_WhileTheItemIdChangesWithThePlacement()
    {
        var north = BuiltInShortcuts.Paste.ToItem(Main, RadialDirection.North);
        var moved = BuiltInShortcuts.Paste.ToItem(Sub, RadialDirection.West);

        Assert.Equal(north.DefinitionId, moved.DefinitionId);
        Assert.NotEqual(north.Id, moved.Id);
    }

    [Fact]
    public void AGroup_HasNoDefinition_AndCannotBeOne()
    {
        var group = new RadialItem(new ItemId("main.sub"), "Más", RadialDirection.East, new OpenMenuAction(Sub));
        var asDefinition = new ActionDefinition(
            "group.sub",
            "Más",
            ActionCategory.Files,
            "Un grupo no es una acción del repertorio.",
            new IconReference(IconSource.Glyph, "Apps"),
            new OpenMenuAction(Sub));

        Assert.Null(group.DefinitionId);
        Assert.True(group.IsGroup);
        Assert.NotEmpty(asDefinition.GetProblems());
        Assert.Throws<ArgumentException>(() => new ActionCatalog([asDefinition]));
    }

    [Fact]
    public void AnUnknownAction_CannotBeADefinition()
    {
        var definition = new ActionDefinition(
            "future.action",
            "Futura",
            ActionCategory.Files,
            "No se puede ejecutar.",
            new IconReference(IconSource.Glyph, "Apps"),
            new UnknownAction("futureAction", "{\"type\":\"futureAction\"}"));

        Assert.NotEmpty(definition.GetProblems());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyDefinitionId_OnAnItem_IsAValidationError(string definitionId)
    {
        var item = new RadialItem(new ItemId("main.x"), "X", RadialDirection.North, new OpenFolderAction(@"C:\Temp"), null, definitionId);

        var result = ConfigurationValidator.Validate(Configuration(new RadialMenu(Main, "Principal", [item])));

        Assert.Contains(result.Errors, e => e.Code == ValidationCodes.ItemDefinitionIdInvalid);
    }

    [Fact]
    public void ADefinitionIdThatIsTooLong_OnAnItem_IsAValidationError()
    {
        var item = new RadialItem(new ItemId("main.x"), "X", RadialDirection.North, new OpenFolderAction(@"C:\Temp"), null, new string('a', ConfigurationLimits.MaxIdLength + 1));

        var result = ConfigurationValidator.Validate(Configuration(new RadialMenu(Main, "Principal", [item])));

        Assert.Contains(result.Errors, e => e.Code == ValidationCodes.ItemDefinitionIdInvalid);
    }

    [Fact]
    public void AnItemWithoutADefinition_IsValid()
    {
        var item = new RadialItem(new ItemId("main.x"), "X", RadialDirection.North, new OpenFolderAction(@"C:\Temp"));

        Assert.True(ConfigurationValidator.Validate(Configuration(new RadialMenu(Main, "Principal", [item]))).IsValid);
    }
}
