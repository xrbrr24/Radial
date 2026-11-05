using System.Text.Json.Nodes;
using Radial.Core.Catalog;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Catalog;

/// <summary>
/// La biblioteca de acciones como parte de la configuración: definiciones independientes de las colocaciones, que se guardan aunque
/// no estén colocadas, y una sola fuente de verdad entre la definición y las instancias que usa el motor.
/// </summary>
public class LibraryTests
{
    private static readonly MenuId Main = new("main");
    private static readonly MenuId Sub = new("sub");

    private static ActionDefinition Def(string id, string name, RadialAction? action = null, ActionCategory? category = null, IReadOnlyList<string>? tags = null) =>
        new(id, name, category ?? ActionCategory.Custom, string.Empty, new IconReference(IconSource.Glyph, "Apps"), action ?? new OpenFolderAction(@"C:\" + id), tags);

    private static RadialConfiguration Config(IReadOnlyList<ActionDefinition> actions, params RadialMenu[] menus) =>
        new() { RootMenuId = Main, Actions = actions, Menus = menus };

    // Materialización: una sola fuente de verdad

    [Fact]
    public void Materialize_TakesTheNameActionAndIconFromTheDefinition()
    {
        var definition = Def("a", "Original");
        var placement = new RadialItem(new ItemId("main.a"), "Stale", RadialDirection.North, new OpenFolderAction("stale"), new IconReference(IconSource.Glyph, "Stale"), "a");

        var item = LibraryResolver.Materialize(placement, definition);

        Assert.Equal("Original", item.Name);
        Assert.Equal(definition.Action, item.Action);
        Assert.Equal(definition.Icon, item.Icon);
        Assert.Equal(new ItemId("main.a"), item.Id);
        Assert.Equal(RadialDirection.North, item.Position);
    }

    [Fact]
    public void Materialize_KeepsTheOwnLabelAndIcon_OfThePlacement()
    {
        var definition = Def("a", "Original");
        var placement = definition.ToItem(new ItemId("main.a"), RadialDirection.East) with
        {
            LabelOverride = "Mío",
            IconOverride = new IconReference(IconSource.Glyph, "Star"),
        };

        var item = LibraryResolver.Materialize(placement, definition);

        Assert.Equal("Mío", item.Name);
        Assert.Equal(new IconReference(IconSource.Glyph, "Star"), item.Icon);
        Assert.Equal(definition.Action, item.Action);
    }

    [Fact]
    public void Resolve_RegeneratesEveryLinkedPlacement_WhenTheDefinitionChanges()
    {
        var original = Def("a", "Uno");
        var config = Config([original], new RadialMenu(Main, "P", [original.ToItem(new ItemId("main.a"), RadialDirection.North), original.ToItem(new ItemId("main.b"), RadialDirection.East)]));
        var edited = original with { Name = "Dos", Action = new OpenFolderAction(@"C:\Nueva") };

        var resolved = LibraryResolver.Resolve(config with { Actions = [edited] });

        Assert.All(resolved.Menus[0].Items, i =>
        {
            Assert.Equal("Dos", i.Name);
            Assert.Equal(new OpenFolderAction(@"C:\Nueva"), i.Action);
            Assert.Equal("a", i.DefinitionId);
        });
        Assert.True(ConfigurationValidator.Validate(resolved).IsValid);
    }

    [Fact]
    public void Resolve_LeavesGroupsLooseItemsAndDanglingReferencesAlone()
    {
        var group = new RadialItem(new ItemId("main.sub"), "Más", RadialDirection.East, new OpenMenuAction(Sub));
        var loose = new RadialItem(new ItemId("main.loose"), "Suelto", RadialDirection.North, new OpenFolderAction("x"));
        var dangling = new RadialItem(new ItemId("main.dangling"), "Roto", RadialDirection.South, new OpenFolderAction("y"), null, "no.existe");
        var config = Config([], new RadialMenu(Main, "P", [group, loose, dangling]), new RadialMenu(Sub, "S", []));

        var resolved = LibraryResolver.Resolve(config);

        Assert.Same(config, resolved); // nada que regenerar: la misma instancia
    }

    [Fact]
    public void AnInconsistentPlacement_IsReportedByTheValidation_NotSilentlyAccepted()
    {
        var definition = Def("a", "Original");
        var stale = definition.ToItem(new ItemId("main.a"), RadialDirection.North) with { Action = new OpenFolderAction("contradictoria") };

        var result = ConfigurationValidator.Validate(Config([definition], new RadialMenu(Main, "P", [stale])));

        Assert.Contains(result.Errors, e => e.Code == ValidationCodes.ItemDefinitionConflict);
    }

    [Fact]
    public void APlacementOfAMissingDefinition_IsReported()
    {
        var item = new RadialItem(new ItemId("main.a"), "A", RadialDirection.North, new OpenFolderAction("x"), null, "no.existe");

        var result = ConfigurationValidator.Validate(Config([], new RadialMenu(Main, "P", [item])));

        Assert.Contains(result.Errors, e => e.Code == ValidationCodes.ItemDefinitionMissing);
    }

    [Fact]
    public void AGroup_CannotBeLinkedToALibraryAction()
    {
        var item = new RadialItem(new ItemId("main.g"), "G", RadialDirection.North, new OpenMenuAction(Sub), null, "a");

        var result = ConfigurationValidator.Validate(Config([Def("a", "A")], new RadialMenu(Main, "P", [item]), new RadialMenu(Sub, "S", [])));

        Assert.Contains(result.Errors, e => e.Code == ValidationCodes.ItemGroupWithDefinition);
    }

    // Validación de la biblioteca

    [Fact]
    public void ADuplicatedDefinitionId_IsReported()
    {
        var result = ConfigurationValidator.Validate(Config([Def("a", "Uno"), Def("a", "Dos")], new RadialMenu(Main, "P", [])));

        Assert.Contains(result.Errors, e => e.Code == ValidationCodes.LibraryDuplicateId);
    }

    [Fact]
    public void AnInvalidDefinition_IsReported_WithItsProblems()
    {
        var invalid = Def("a", "A", new OpenUrlAction("ftp://no-permitido"));

        var result = ConfigurationValidator.Validate(Config([invalid], new RadialMenu(Main, "P", [])));

        Assert.Contains(result.Errors, e => e.Code == ValidationCodes.LibraryDefinitionInvalid && e.Path.Contains("actions[a]"));
    }

    [Fact]
    public void AnUnknownActionInTheLibrary_IsAWarning_NotAnError()
    {
        var future = Def("future", "Futura", new UnknownAction("futureAction", "{\"type\":\"futureAction\"}"));

        var result = ConfigurationValidator.Validate(Config([future], new RadialMenu(Main, "P", [])));

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Code == ValidationCodes.ActionUnknownType);
    }

    [Fact]
    public void ALibraryOverTheLimit_IsRejected()
    {
        var library = Enumerable.Range(0, ConfigurationLimits.MaxLibraryActions + 1).Select(n => Def($"a{n}", "A")).ToList();

        var result = ConfigurationValidator.Validate(Config(library, new RadialMenu(Main, "P", [])));

        Assert.Contains(result.Errors, e => e.Code == ValidationCodes.LibraryTooLarge);
    }

    [Fact]
    public void ADefinitionWithTooManyOrRepeatedTags_IsReported()
    {
        var tooMany = Def("a", "A", tags: Enumerable.Range(0, ConfigurationLimits.MaxTags + 1).Select(n => "t" + n).ToList());
        var repeated = Def("b", "B", tags: ["uno", "UNO"]);
        var blank = Def("c", "C", tags: ["ok", " "]);
        var padded = Def("d", "D", tags: [" espacio"]);

        foreach (var definition in new[] { tooMany, repeated, blank, padded })
        {
            Assert.NotEmpty(definition.GetProblems());
        }

        Assert.Empty(Def("e", "E", tags: ["uno", "dos"]).GetProblems());
    }

    // Persistencia independiente de las colocaciones

    [Fact]
    public void ADefinitionNotPlacedAnywhere_SurvivesBeingWrittenAndRead()
    {
        var placed = Def("placed", "Colocada");
        var unplaced = Def("unplaced", "Sin colocar", new SendKeysAction(new KeyChord(KeyModifiers.Control | KeyModifiers.Alt, "Oem3")), ActionCategory.Editing, ["a", "b"]) with
        {
            Description = "Una descripción.",
            IsBuiltIn = true,
        };
        var config = Config([placed, unplaced], new RadialMenu(Main, "P", [placed.ToItem(new ItemId("main.placed"), RadialDirection.North)]));

        var reread = ConfigurationFile.Parse(ConfigurationFile.Serialize(config)).Configuration!;

        Assert.Equal(new[] { "placed", "unplaced" }, reread.Actions.Select(a => a.Id).ToArray());
        Assert.Equal(unplaced, reread.Actions[1]);
        Assert.Single(reread.Menus[0].Items);
    }

    [Fact]
    public void TheSameDefinition_PlacedSeveralTimes_IsStoredOnceInTheLibrary()
    {
        var definition = Def("a", "A");
        var config = Config(
            [definition],
            new RadialMenu(Main, "P", [definition.ToItem(new ItemId("main.1"), RadialDirection.North), definition.ToItem(new ItemId("main.2"), RadialDirection.East), new RadialItem(new ItemId("main.sub"), "S", RadialDirection.South, new OpenMenuAction(Sub))]),
            new RadialMenu(Sub, "S", [definition.ToItem(new ItemId("sub.1"), RadialDirection.North)]));

        var written = JsonNode.Parse(ConfigurationFile.Serialize(config))!;

        Assert.Single(written["actions"]!.AsArray());
        var placements = written["menus"]!.AsArray().SelectMany(m => m!["items"]!.AsArray()).Where(i => i!["actionId"] is not null).ToList();
        Assert.Equal(3, placements.Count);
        Assert.All(placements, p => Assert.Equal("a", p!["actionId"]!.GetValue<string>()));
        Assert.All(placements, p => Assert.Null(p!["action"])); // la acción no se repite en cada colocación
    }

    [Fact]
    public void EditingTheDefinitionInTheFile_ChangesEveryPlacement_OnTheNextRead()
    {
        var definition = Def("a", "Uno");
        var config = Config([definition], new RadialMenu(Main, "P", [definition.ToItem(new ItemId("main.1"), RadialDirection.North), definition.ToItem(new ItemId("main.2"), RadialDirection.East)]));
        var json = ConfigurationFile.Serialize(config).Replace("\"name\": \"Uno\"", "\"name\": \"Editada\"", StringComparison.Ordinal);

        var reread = ConfigurationFile.Parse(json).Configuration!;

        Assert.All(reread.Menus[0].Items, i => Assert.Equal("Editada", i.Name));
    }

    [Fact]
    public void ARepeatedLibraryRoundTrip_IsStable()
    {
        var definition = Def("a", "A", tags: ["x"]);
        var config = Config([definition], new RadialMenu(Main, "P", [definition.ToItem(new ItemId("main.1"), RadialDirection.North) with { LabelOverride = "Otro" }]));

        var first = ConfigurationFile.Serialize(config);
        var second = ConfigurationFile.Serialize(ConfigurationFile.Parse(first).Configuration!);

        Assert.Equal(first, second);
    }

    [Fact]
    public void AReferenceToAMissingLibraryAction_RejectsTheFile()
    {
        var json = "{\"schemaVersion\":2,\"rootMenuId\":\"main\",\"actions\":[],\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":[{\"id\":\"i\",\"position\":\"North\",\"actionId\":\"no.existe\"}]}]}";

        var result = ConfigurationFile.Parse(json);

        Assert.Equal(ConfigurationParseStatus.Invalid, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.ActionRefMissing);
    }

    [Fact]
    public void APlacementWithBothAnActionIdAndAnInlineAction_IsRejected_NoTwoSourcesOfTruth()
    {
        var json = "{\"schemaVersion\":2,\"rootMenuId\":\"main\",\"actions\":[{\"id\":\"a\",\"name\":\"A\",\"category\":\"custom\",\"icon\":{\"source\":\"glyph\",\"value\":\"Apps\"},"
            + "\"action\":{\"type\":\"openFolder\",\"path\":\"C:\\\\A\"}}],\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":[{\"id\":\"i\",\"position\":\"North\",\"actionId\":\"a\","
            + "\"action\":{\"type\":\"openFolder\",\"path\":\"C:\\\\B\"}}]}]}";

        var result = ConfigurationFile.Parse(json);

        Assert.Equal(ConfigurationParseStatus.Invalid, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.ItemActionConflict);
    }

    [Fact]
    public void ACategoryThatIsNotOneOfTheSeven_IsKept()
    {
        var definition = Def("a", "A", category: new ActionCategory("juegos"));
        var config = Config([definition], new RadialMenu(Main, "P", []));

        var reread = ConfigurationFile.Parse(ConfigurationFile.Serialize(config)).Configuration!;

        Assert.Equal(new ActionCategory("juegos"), reread.Actions[0].Category);
    }

    // Normalización: las colocaciones sueltas pasan a la biblioteca sin duplicar

    [Fact]
    public void Normalize_MovesLooseActionsIntoTheLibrary_AndReusesIdenticalOnes()
    {
        var loose1 = new RadialItem(new ItemId("main.1"), "Abrir", RadialDirection.North, new OpenFolderAction(@"C:\X"));
        var loose2 = new RadialItem(new ItemId("main.2"), "Abrir", RadialDirection.East, new OpenFolderAction(@"C:\X"));
        var other = new RadialItem(new ItemId("main.3"), "Abrir", RadialDirection.South, new OpenFolderAction(@"C:\Y"));
        var config = Config([], new RadialMenu(Main, "P", [loose1, loose2, other]));

        var normalized = LibraryResolver.Normalize(config);

        Assert.Equal(new[] { "custom.abrir", "custom.abrir-2" }, normalized.Actions.Select(a => a.Id).ToArray());
        Assert.Equal(new[] { "custom.abrir", "custom.abrir", "custom.abrir-2" }, normalized.Menus[0].Items.Select(i => i.DefinitionId).ToArray());
        Assert.True(ConfigurationValidator.Validate(normalized).IsValid);
    }

    [Fact]
    public void Normalize_IsAnIdentity_ForAConfigurationThatIsAlreadyLinked()
    {
        var definition = Def("a", "A");
        var config = Config([definition], new RadialMenu(Main, "P", [definition.ToItem(new ItemId("main.1"), RadialDirection.North)]));

        Assert.Same(config, LibraryResolver.Normalize(config));
    }

    [Fact]
    public void Normalize_RecreatesADanglingDefinitionUnderItsOwnId_WhenValid()
    {
        var item = new RadialItem(new ItemId("main.1"), "Carpeta", RadialDirection.North, new OpenFolderAction(@"C:\X"), null, "files.folder");

        var normalized = LibraryResolver.Normalize(Config([], new RadialMenu(Main, "P", [item])));

        Assert.Equal("files.folder", Assert.Single(normalized.Actions).Id);
        Assert.Equal("files.folder", normalized.Menus[0].Items[0].DefinitionId);
    }

    [Fact]
    public void Normalize_IsDeterministic()
    {
        var item = new RadialItem(new ItemId("main.1"), "Carpeta", RadialDirection.North, new OpenFolderAction(@"C:\X"));
        var config = Config([], new RadialMenu(Main, "P", [item]));

        Assert.Equal(ConfigurationFile.Serialize(config), ConfigurationFile.Serialize(config));
        Assert.Equal(LibraryResolver.Normalize(config).Actions.Single().Id, LibraryResolver.Normalize(config).Actions.Single().Id);
    }

    [Fact]
    public void Normalize_DoesNotTouchGroupsNorUnknownActions()
    {
        var group = new RadialItem(new ItemId("main.g"), "G", RadialDirection.North, new OpenMenuAction(Sub));
        var unknown = new RadialItem(new ItemId("main.u"), "U", RadialDirection.East, new UnknownAction("x", "{\"type\":\"x\"}"));

        var normalized = LibraryResolver.Normalize(Config([], new RadialMenu(Main, "P", [group, unknown]), new RadialMenu(Sub, "S", [])));

        Assert.Empty(normalized.Actions);
        Assert.Null(normalized.Menus[0].Items[0].DefinitionId);
        Assert.Null(normalized.Menus[0].Items[1].DefinitionId);
    }

    // Ayudas

    [Theory]
    [InlineData("Mi acción", "mi-accion")]
    [InlineData("  ¡Hola, Mundo!  ", "hola-mundo")]
    [InlineData("Ñandú", "nandu")]
    [InlineData("", "accion")]
    [InlineData("***", "accion")]
    [InlineData("A--B", "a-b")]
    public void Slug_ReducesATextToAnIdentifierFragment(string text, string expected)
    {
        Assert.Equal(expected, LibraryResolver.Slug(text));
        Assert.True(ActionDefinition.IsValidId("custom." + LibraryResolver.Slug(text)));
    }

    [Fact]
    public void Slug_IsBoundedSoTheIdentifierFits()
    {
        var slug = LibraryResolver.Slug(new string('a', 500));

        Assert.True(("custom." + slug).Length <= ConfigurationLimits.MaxIdLength);
    }

    [Fact]
    public void UniqueId_AddsASuffix_WhileTheIdIsTaken_AndStaysWithinTheLimit()
    {
        var used = new HashSet<string> { "a", "a-2", "a-3" };

        Assert.Equal("a-4", LibraryResolver.UniqueId("a", used));
        Assert.Equal("libre", LibraryResolver.UniqueId("libre", used));

        var longId = new string('x', ConfigurationLimits.MaxIdLength);
        var taken = new HashSet<string> { longId };
        var next = LibraryResolver.UniqueId(longId, taken);
        Assert.NotEqual(longId, next);
        Assert.True(next.Length <= ConfigurationLimits.MaxIdLength);
    }

    [Fact]
    public void Categories_AreOpen_AndTheSevenInitialOnesAreKnown()
    {
        Assert.Equal(
            new[] { "editing", "browser", "applications", "files", "system", "multimedia", "custom" },
            ActionCategory.Standard.Select(c => c.Id).ToArray());
        Assert.All(ActionCategory.Standard, c => Assert.True(c.IsValid));
        Assert.False(default(ActionCategory).IsValid);
        Assert.False(new ActionCategory("Con Espacios").IsValid);
        Assert.False(new ActionCategory(new string('a', 33)).IsValid);
        Assert.True(new ActionCategory("mi-categoria").IsValid);
    }

    [Fact]
    public void TheFactoryShortcuts_AreAllValid_Unique_AndBuiltIn()
    {
        var shortcuts = FactoryActions.Shortcuts;

        Assert.All(shortcuts, d => Assert.Empty(d.GetProblems()));
        Assert.Equal(shortcuts.Count, shortcuts.Select(d => d.Id).Distinct().Count());
        Assert.All(shortcuts, d => Assert.True(d.IsBuiltIn));
        Assert.All(shortcuts, d => Assert.IsType<SendKeysAction>(d.Action));
        Assert.True(shortcuts.Count >= 40);
        foreach (var category in new[] { ActionCategory.Editing, ActionCategory.Browser, ActionCategory.System, ActionCategory.Multimedia })
        {
            Assert.Contains(shortcuts, d => d.Category == category);
        }
    }

    [Fact]
    public void TwoDefinitionsWithTheSameContent_AreEqualByValue_IncludingTheirTags()
    {
        var a = Def("a", "A", tags: ["x", "y"]);
        var b = Def("a", "A", tags: ["x", "y"]);
        var c = Def("a", "A", tags: ["x", "z"]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }
}
