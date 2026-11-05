using Radial.Core.Catalog;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Core.Validation;
using Radial.Windows.Shortcuts;
using Radial.Windows.Storage;

namespace Radial.Windows.Tests.Storage;

/// <summary>La configuración inicial: las ocho acciones de siempre, generadas del repertorio, con un grupo que ya enseña la navegación.</summary>
public class DefaultConfigurationTests
{
    private static readonly string[] ExpectedActionNames = ["Copiar", "Pegar", "Deshacer", "Seleccionar todo", "Guardar", "Carpeta", "Web", "Navegador"];

    private static IEnumerable<RadialItem> AllItems(RadialConfiguration configuration) => configuration.Menus.SelectMany(m => m.Items);

    [Fact]
    public void TheDefaultConfiguration_IsValid_WithoutWarnings()
    {
        var result = ConfigurationValidator.Validate(StorageSamples.Defaults());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void TheDefaultConfiguration_ForThisMachine_IsValidToo()
    {
        Assert.True(ConfigurationValidator.Validate(DefaultConfiguration.Create()).IsValid);
    }

    [Fact]
    public void TheEightCurrentActions_AreKept()
    {
        var actions = AllItems(StorageSamples.Defaults()).Where(i => !i.IsGroup).ToList();

        Assert.Equal(8, actions.Count);
        Assert.Equal(ExpectedActionNames.OrderBy(n => n), actions.Select(i => i.Name).OrderBy(n => n));
        Assert.All(actions, i => Assert.False(i.IsUnsupported));
    }

    [Fact]
    public void TheDefaultMenu_HasAGroupWithItsOwnSubmenu_SoNavigationWorksFromTheFirstRun()
    {
        var configuration = StorageSamples.Defaults();
        var root = configuration.Menus.Single(m => m.Id == configuration.RootMenuId);

        var group = Assert.Single(root.Items, i => i.IsGroup);
        var target = ((OpenMenuAction)group.Action).TargetMenuId;

        Assert.True(configuration.TryGetMenu(target, out var submenu));
        Assert.Equal(new[] { "Carpeta", "Navegador", "Web" }, submenu!.Items.Select(i => i.Name).OrderBy(n => n).ToArray());
        Assert.Null(group.DefinitionId); // un grupo es estructura del árbol, no una acción del repertorio
    }

    [Fact]
    public void EveryItem_HasItsOwnUniqueId_AndEveryActionRemembersItsDefinition()
    {
        var items = AllItems(StorageSamples.Defaults()).ToList();

        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        Assert.All(items.Where(i => !i.IsGroup), i => Assert.False(string.IsNullOrWhiteSpace(i.DefinitionId)));
        Assert.All(items.Where(i => !i.IsGroup), i => Assert.StartsWith(i.DefinitionId!, i.Id.Value[(i.Id.Value.IndexOf('.') + 1)..]));
    }

    [Fact]
    public void ThePlacedActions_ComeFromTheLibraryOfTheConfiguration()
    {
        var configuration = StorageSamples.Defaults();
        var used = AllItems(configuration).Where(i => !i.IsGroup).Select(i => i.DefinitionId!).ToHashSet();

        Assert.All(used, id => Assert.True(configuration.TryGetDefinition(id, out _), $"La acción '{id}' colocada no está en la biblioteca."));
        Assert.Equal(8, used.Count);
    }

    [Fact]
    public void TheLibrary_HoldsMoreActionsThanTheOnesPlaced_SoTheyCanBeUsedLater()
    {
        var configuration = StorageSamples.Defaults();
        var placed = AllItems(configuration).Where(i => !i.IsGroup).Select(i => i.DefinitionId!).ToHashSet();
        var unplaced = configuration.Actions.Where(a => !placed.Contains(a.Id)).ToList();

        Assert.True(configuration.Actions.Count > 40);
        Assert.NotEmpty(unplaced);
        Assert.Contains(unplaced, a => a.Id == "media.play-pause");
        Assert.Contains(unplaced, a => a.Id == "app.notepad");
    }

    [Fact]
    public void TheLibrary_CoversEverySeedCategory_AndAllItsActionsAreFactoryActions()
    {
        var library = StorageSamples.Defaults().Actions;

        foreach (var category in new[] { ActionCategory.Editing, ActionCategory.Browser, ActionCategory.Applications, ActionCategory.Files, ActionCategory.System, ActionCategory.Multimedia })
        {
            Assert.Contains(library, a => a.Category == category);
        }

        Assert.DoesNotContain(library, a => a.Category == ActionCategory.Custom); // lo personalizado lo crea el usuario
        Assert.All(library, a => Assert.True(a.IsBuiltIn));
        Assert.Equal(library.Count, library.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void EveryFactoryAction_IsValid_AndEveryFactoryShortcutIsSendable()
    {
        var library = FactoryLibrary.Create(FactoryPaths.FromEnvironment());

        Assert.All(library, a => Assert.Empty(a.GetProblems()));
        Assert.All(library.Where(a => a.Action is SendKeysAction), a => Assert.True(ShortcutParser.TryParse(((SendKeysAction)a.Action).Chord, out _), a.Id));
    }

    [Fact]
    public void TheDestinations_ComeFromTheArguments_NotFromHiddenState()
    {
        var configuration = DefaultConfiguration.Create(@"D:\Navegador\n.exe", @"D:\Mis cosas", "https://example.org/");
        var actions = AllItems(configuration).Select(i => i.Action).ToList();

        Assert.Contains<RadialAction>(new LaunchApplicationAction(@"D:\Navegador\n.exe"), actions);
        Assert.Contains<RadialAction>(new OpenFolderAction(@"D:\Mis cosas"), actions);
        Assert.Contains<RadialAction>(new OpenUrlAction("https://example.org/"), actions);
    }

    [Fact]
    public void TheDefaultConfiguration_SurvivesBeingWrittenAndRead()
    {
        var original = StorageSamples.Defaults();

        var parsed = ConfigurationFile.Parse(ConfigurationFile.Serialize(original));

        Assert.True(parsed.IsOk);
        Assert.Equal(ConfigurationFile.Serialize(original), ConfigurationFile.Serialize(parsed.Configuration!));
    }

    [Fact]
    public void TheDefaultMenu_UsesTheSameSectorLayoutAsBefore()
    {
        var configuration = StorageSamples.Defaults();
        var root = configuration.Menus.Single(m => m.Id == configuration.RootMenuId);

        Assert.Equal("Copiar", root.GetItem(RadialDirection.North)!.Name);
        Assert.Equal("Pegar", root.GetItem(RadialDirection.NorthEast)!.Name);
        Assert.Equal("Deshacer", root.GetItem(RadialDirection.East)!.Name);
        Assert.Equal("Seleccionar todo", root.GetItem(RadialDirection.SouthEast)!.Name);
        Assert.Equal("Guardar", root.GetItem(RadialDirection.South)!.Name);
    }
}
