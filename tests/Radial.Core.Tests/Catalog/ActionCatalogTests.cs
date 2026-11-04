using Radial.Core.Catalog;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Catalog;

public class ActionCatalogTests
{
    private static ActionDefinition Definition(
        string id = "edit.copy",
        string name = "Copiar",
        string description = "Copia.",
        IconReference? icon = null,
        RadialAction? action = null) =>
        new(id, name, ActionCategory.Editing, description, icon ?? new IconReference(IconSource.Glyph, "Copy"), action ?? new SendKeysAction(new KeyChord(KeyModifiers.Control, "C")));

    // El repertorio base

    [Fact]
    public void TheBuiltInShortcuts_AreAllValid()
    {
        Assert.All(BuiltInShortcuts.All, d => Assert.Empty(d.GetProblems()));
        _ = new ActionCatalog(BuiltInShortcuts.All); // no lanza
    }

    [Fact]
    public void TheBuiltInShortcuts_AreAllSendKeysActions_SoOneExecutorServesThemAll()
    {
        Assert.All(BuiltInShortcuts.All, d => Assert.IsType<SendKeysAction>(d.Action));
    }

    [Theory]
    [InlineData("edit.copy", KeyModifiers.Control, "C")]
    [InlineData("edit.paste", KeyModifiers.Control, "V")]
    [InlineData("edit.undo", KeyModifiers.Control, "Z")]
    [InlineData("edit.select-all", KeyModifiers.Control, "A")]
    [InlineData("edit.save", KeyModifiers.Control, "S")]
    public void EachBuiltInShortcut_HasItsCombination(string id, KeyModifiers modifiers, string key)
    {
        var definition = new ActionCatalog(BuiltInShortcuts.All).Get(id);

        var action = Assert.IsType<SendKeysAction>(definition.Action);
        Assert.Equal(new KeyChord(modifiers, key), action.Chord);
    }

    [Fact]
    public void TheBuiltInIds_AreStable()
    {
        Assert.Equal(["edit.copy", "edit.paste", "edit.undo", "edit.select-all", "edit.save"], BuiltInShortcuts.All.Select(d => d.Id));
    }

    // Definiciones

    [Fact]
    public void ADefinition_BecomesAMenuItem_WithItsIdNameIconAndAction()
    {
        var definition = BuiltInShortcuts.Copy;

        var item = definition.ToItem(RadialDirection.East);

        Assert.Equal(new ItemId("edit.copy"), item.Id);
        Assert.Equal("Copiar", item.Name);
        Assert.Equal(RadialDirection.East, item.Position);
        Assert.Equal(definition.Action, item.Action);
        Assert.Equal(definition.Icon, item.Icon);
        Assert.False(item.IsGroup);
    }

    [Fact]
    public void ADefinitionWithAnInvalidAction_ReportsTheProblemsOfTheAction()
    {
        var definition = Definition(action: new SendKeysAction(new KeyChord(KeyModifiers.Control, "Ñ")));

        Assert.NotEmpty(definition.GetProblems());
    }

    [Fact]
    public void AMenuGroup_IsNotACatalogAction() =>
        Assert.NotEmpty(Definition(action: new OpenMenuAction(new MenuId("otro"))).GetProblems());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Edit.Copy")] // mayúsculas
    [InlineData("edit copy")]
    [InlineData("edit_copy")]
    [InlineData("edición.copiar")]
    public void AnIdentifierOutsideTheAllowedAlphabet_IsRejected(string id) => Assert.NotEmpty(Definition(id: id).GetProblems());

    [Fact]
    public void AnIdentifierThatIsTooLong_IsRejected() =>
        Assert.NotEmpty(Definition(id: new string('a', ConfigurationLimits.MaxIdLength + 1)).GetProblems());

    [Fact]
    public void ANameThatIsEmptyOrTooLong_IsRejected()
    {
        Assert.NotEmpty(Definition(name: "").GetProblems());
        Assert.NotEmpty(Definition(name: new string('x', ConfigurationLimits.MaxNameLength + 1)).GetProblems());
    }

    [Fact]
    public void ADescriptionIsRequired() => Assert.NotEmpty(Definition(description: " ").GetProblems());

    [Fact]
    public void AnInvalidIconOrCategory_IsRejected()
    {
        Assert.NotEmpty(Definition(icon: new IconReference(IconSource.Glyph, "")).GetProblems());
        Assert.NotEmpty(Definition(icon: new IconReference((IconSource)99, "Copy")).GetProblems());
        Assert.NotEmpty((Definition() with { Category = (ActionCategory)99 }).GetProblems());
    }

    // Catálogo

    [Fact]
    public void TwoDefinitionsWithTheSameId_AreRejected()
    {
        var error = Assert.Throws<ArgumentException>(() => new ActionCatalog([Definition(), Definition(name: "Otra")]));

        Assert.Contains("repetido", error.Message);
    }

    [Fact]
    public void ACatalogWithAnInvalidDefinition_IsRejected_NamingIt()
    {
        var error = Assert.Throws<ArgumentException>(() => new ActionCatalog([Definition(id: "bad.one", name: "")]));

        Assert.Contains("bad.one", error.Message);
    }

    [Fact]
    public void TheCatalogLooksDefinitionsUpById_AndKeepsTheirOrder()
    {
        var catalog = new ActionCatalog(BuiltInShortcuts.All);

        Assert.True(catalog.TryGet("edit.undo", out var undo));
        Assert.Equal("Deshacer", undo.Name);
        Assert.False(catalog.TryGet("no.existe", out _));
        Assert.False(catalog.TryGet(null!, out _));
        Assert.Throws<KeyNotFoundException>(() => catalog.Get("no.existe"));
        Assert.Equal(BuiltInShortcuts.All, catalog.Definitions);
    }

    [Fact]
    public void TheCatalog_FiltersByCategory()
    {
        var web = Definition(id: "web.url", name: "Web", action: new OpenUrlAction("https://example.com")) with { Category = ActionCategory.Web };
        var catalog = new ActionCatalog([.. BuiltInShortcuts.All, web]);

        Assert.Equal([web], catalog.InCategory(ActionCategory.Web));
        Assert.Equal(5, catalog.InCategory(ActionCategory.Editing).Count());
        Assert.Empty(catalog.InCategory(ActionCategory.Files));
    }

    [Fact]
    public void AMenuBuiltFromTheCatalog_PassesTheConfigurationValidation()
    {
        var root = new MenuId("main");
        var items = BuiltInShortcuts.All.Select((d, i) => d.ToItem((RadialDirection)i)).ToList();
        var configuration = new RadialConfiguration { RootMenuId = root, Menus = [new RadialMenu(root, "Principal", items)] };

        Assert.True(ConfigurationValidator.Validate(configuration).IsValid);
    }
}
