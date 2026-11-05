using System.Text.Json.Nodes;
using Radial.Core.Catalog;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Storage;

/// <summary>
/// Migración real de la versión 1 (cada elemento con su acción) a la 2 (biblioteca de acciones independiente): conserva menús,
/// posiciones, grupos, acciones y parámetros; recupera las definiciones sin duplicarlas; comunica las incoherencias; no pierde las
/// acciones desconocidas; y los archivos corruptos o de versión futura se siguen tratando igual.
/// </summary>
public class SchemaV1ToV2MigrationTests
{
    private static string Item(string id, string name, string position, string action, string? definitionId = null, string? icon = null) =>
        "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"position\":\"" + position + "\","
        + (icon is null ? string.Empty : "\"icon\":" + icon + ",")
        + (definitionId is null ? string.Empty : "\"definitionId\":\"" + definitionId + "\",")
        + "\"action\":" + action + "}";

    private static string V1(params string[] menus) =>
        "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"geometry\":{\"deadZoneRadius\":44,\"hysteresisDegrees\":7},"
        + "\"navigation\":{\"submenuDwellMs\":450,\"backDwellMs\":900,\"maxDepth\":4},\"menus\":[" + string.Join(",", menus) + "]}";

    private static string Menu(string id, string name, params string[] items) =>
        "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"items\":[" + string.Join(",", items) + "]}";

    private const string CtrlC = "{\"type\":\"sendKeys\",\"modifiers\":[\"Control\"],\"key\":\"C\"}";
    private const string CtrlV = "{\"type\":\"sendKeys\",\"modifiers\":[\"Control\"],\"key\":\"V\"}";
    private const string Group = "{\"type\":\"openMenu\",\"targetMenuId\":\"more\"}";
    private const string Folder = "{\"type\":\"openFolder\",\"path\":\"C:\\\\Docs\"}";
    private const string FutureAction = "{\"type\":\"futureAction\",\"nested\":{\"a\":[1,2,{\"b\":null}]}}";

    private static ConfigurationParseResult Migrate(string v1) => ConfigurationFile.Parse(v1);

    private static IEnumerable<RadialItem> AllItems(RadialConfiguration c) => c.Menus.SelectMany(m => m.Items);

    // Qué se conserva

    [Fact]
    public void EverythingIsKept_MenusPositionsGroupsActionsAndSettings()
    {
        var result = Migrate(V1(
            Menu("main", "Principal",
                Item("main.copy", "Copiar", "North", CtrlC, "edit.copy"),
                Item("main.paste", "Pegar", "East", CtrlV, "edit.paste"),
                Item("main.folder", "Carpeta", "South", Folder),
                Item("main.more", "Más", "West", Group)),
            Menu("more", "Más", Item("more.future", "Futura", "North", FutureAction))));

        Assert.True(result.IsOk, string.Join(" | ", result.Issues.Select(i => i.Message)));
        var config = result.Configuration!;
        Assert.Equal(1, result.FileVersion);
        Assert.Equal(new MenuId("main"), config.RootMenuId);
        Assert.Equal(44, config.Geometry.DeadZoneRadius);
        Assert.Equal(TimeSpan.FromMilliseconds(450), config.Navigation.SubmenuDwell);
        Assert.Equal(4, config.Navigation.MaxDepth);

        Assert.Equal(new[] { "main", "more" }, config.Menus.Select(m => m.Id.Value).ToArray());
        var main = config.Menus[0].Items;
        Assert.Equal(new[] { "main.copy", "main.paste", "main.folder", "main.more" }, main.Select(i => i.Id.Value).ToArray());
        Assert.Equal(
            new[] { RadialDirection.North, RadialDirection.East, RadialDirection.South, RadialDirection.West },
            main.Select(i => i.Position).ToArray());
        Assert.Equal(new SendKeysAction(new KeyChord(KeyModifiers.Control, "C")), main[0].Action);
        Assert.Equal(new SendKeysAction(new KeyChord(KeyModifiers.Control, "V")), main[1].Action);
        Assert.Equal(new OpenFolderAction(@"C:\Docs"), main[2].Action);
        Assert.Equal(new OpenMenuAction(new MenuId("more")), main[3].Action);
        Assert.Equal("Copiar", main[0].Name);
        Assert.True(main[3].IsGroup);
        Assert.Null(main[3].DefinitionId); // un grupo es estructura, no una definición
    }

    [Fact]
    public void TheDefinitionsAreRecoveredIntoTheLibrary_WithTheirFactoryData()
    {
        var config = Migrate(V1(Menu("main", "Principal", Item("main.copy", "Copiar", "North", CtrlC, "edit.copy")))).Configuration!;

        var definition = Assert.Single(config.Actions);
        Assert.Equal("edit.copy", definition.Id);
        Assert.True(definition.IsBuiltIn);
        Assert.Equal(ActionCategory.Editing, definition.Category);
        Assert.Equal("Copia la selección al portapapeles.", definition.Description);
        Assert.Contains("portapapeles", definition.TagList);
        Assert.Equal("edit.copy", config.Menus[0].Items[0].DefinitionId);
    }

    [Fact]
    public void TheDefinitionsOfThePhase5DefaultMenu_AreRecoveredAsFactoryActions()
    {
        var config = Migrate(V1(Menu("main", "Principal",
            Item("main.url", "Web", "North", "{\"type\":\"openUrl\",\"url\":\"https://www.wikipedia.org\"}", "web.url"),
            Item("main.folder", "Carpeta", "East", Folder, "files.folder")))).Configuration!;

        Assert.All(config.Actions, d => Assert.True(d.IsBuiltIn));
        Assert.Equal(ActionCategory.Browser, config.Actions.Single(d => d.Id == "web.url").Category);
        Assert.Equal(ActionCategory.Files, config.Actions.Single(d => d.Id == "files.folder").Category);
    }

    [Fact]
    public void ADefinitionPlacedTwice_BecomesOneDefinitionWithTwoPlacements_AndKeepsTheOwnLabels()
    {
        var config = Migrate(V1(Menu("main", "Principal",
            Item("main.copy", "Copiar", "North", CtrlC, "edit.copy"),
            Item("main.copy2", "Copiar aquí", "East", CtrlC, "edit.copy")))).Configuration!;

        Assert.Single(config.Actions);
        var items = config.Menus[0].Items;
        Assert.All(items, i => Assert.Equal("edit.copy", i.DefinitionId));
        Assert.Null(items[0].LabelOverride);
        Assert.Equal("Copiar aquí", items[1].LabelOverride);
        Assert.Equal("Copiar aquí", items[1].Name);
    }

    [Fact]
    public void AnIconThatDiffersFromTheDefinition_IsKeptAsAnOwnSetting()
    {
        var own = "{\"source\":\"glyph\",\"value\":\"Star\"}";
        var config = Migrate(V1(Menu("main", "Principal",
            Item("main.a", "Copiar", "North", CtrlC, "edit.copy", "{\"source\":\"glyph\",\"value\":\"Copy\"}"),
            Item("main.b", "Copiar", "East", CtrlC, "edit.copy", own)))).Configuration!;

        var items = config.Menus[0].Items;
        Assert.Null(items[0].IconOverride);
        Assert.Equal(new IconReference(IconSource.Glyph, "Star"), items[1].IconOverride);
        Assert.Equal(new IconReference(IconSource.Glyph, "Star"), items[1].Icon);
    }

    // Sin duplicar definiciones

    [Fact]
    public void IdenticalPlacementsWithoutADefinitionId_ShareOneGeneratedDefinition()
    {
        var config = Migrate(V1(Menu("main", "Principal",
            Item("main.a", "Abrir", "North", Folder),
            Item("main.b", "Abrir", "East", Folder),
            Item("main.c", "Otra", "South", Folder)))).Configuration!;

        Assert.Equal(new[] { "custom.abrir", "custom.otra" }, config.Actions.Select(a => a.Id).ToArray());
        Assert.False(config.Actions[0].IsBuiltIn);
        Assert.Equal(ActionCategory.Files, config.Actions[0].Category);
        Assert.Equal("custom.abrir", config.Menus[0].Items[0].DefinitionId);
        Assert.Equal("custom.abrir", config.Menus[0].Items[1].DefinitionId);
    }

    [Fact]
    public void GeneratedIdentifiers_AreUnique_WhenNamesCollide()
    {
        var config = Migrate(V1(Menu("main", "Principal",
            Item("main.a", "Abrir", "North", Folder),
            Item("main.b", "Abrir", "East", "{\"type\":\"openFolder\",\"path\":\"C:\\\\Otra\"}")))).Configuration!;

        Assert.Equal(new[] { "custom.abrir", "custom.abrir-2" }, config.Actions.Select(a => a.Id).ToArray());
    }

    // Incoherencias

    [Fact]
    public void TheSameDefinitionIdWithADifferentAction_KeepsBothAsSeparateDefinitions_AndWarns()
    {
        var first = "{\"type\":\"openUrl\",\"url\":\"https://a.example\"}";
        var second = "{\"type\":\"openUrl\",\"url\":\"https://b.example\"}";

        var result = Migrate(V1(Menu("main", "Principal",
            Item("main.one", "Uno", "North", first, "tool"),
            Item("main.two", "Dos", "East", second, "tool"))));

        Assert.True(result.IsOk);
        var config = result.Configuration!;
        Assert.Equal(new[] { "tool", "tool-2" }, config.Actions.Select(a => a.Id).ToArray());
        Assert.Equal(new OpenUrlAction("https://a.example"), config.Menus[0].Items[0].Action);
        Assert.Equal(new OpenUrlAction("https://b.example"), config.Menus[0].Items[1].Action);
        var note = Assert.Single(result.Issues, i => i.Code == ConfigurationCodes.MigrationNote);
        Assert.Equal(ValidationSeverity.Warning, note.Severity);
        Assert.Contains("main.two", note.Path);
    }

    [Fact]
    public void AnInvalidDefinitionId_IsReplacedByAGeneratedOne_AndWarns()
    {
        var result = Migrate(V1(Menu("main", "Principal", Item("main.a", "Mi acción", "North", Folder, "Mi Acción!"))));

        Assert.True(result.IsOk);
        Assert.Equal("custom.mi-accion", Assert.Single(result.Configuration!.Actions).Id);
        Assert.Single(result.Issues, i => i.Code == ConfigurationCodes.MigrationNote);
    }

    [Fact]
    public void ACleanFile_ProducesNoMigrationNotes()
    {
        var result = Migrate(V1(Menu("main", "Principal", Item("main.copy", "Copiar", "North", CtrlC, "edit.copy"))));

        Assert.DoesNotContain(result.Issues, i => i.Code == ConfigurationCodes.MigrationNote);
    }

    // Lo que no se toca

    [Fact]
    public void AnUnknownAction_IsNotLost_NorMovedToTheLibrary()
    {
        var config = Migrate(V1(Menu("main", "Principal", Item("main.future", "Futura", "North", FutureAction)))).Configuration!;

        var item = Assert.Single(config.Menus[0].Items);
        var unknown = Assert.IsType<UnknownAction>(item.Action);
        Assert.Equal("futureAction", unknown.Type);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(FutureAction), JsonNode.Parse(unknown.RawJson)));
        Assert.Empty(config.Actions);
        Assert.Null(item.DefinitionId);
    }

    [Fact]
    public void AnEmptyMenu_AndAFileWithoutItems_GetAnEmptyLibrary()
    {
        var config = Migrate(V1(Menu("main", "Principal"))).Configuration!;

        Assert.Empty(config.Actions);
        Assert.Empty(config.Menus[0].Items);
    }

    // Idempotencia y compatibilidad

    [Fact]
    public void TheMigratedConfiguration_IsWrittenAsVersion2_AndReadBackIdentically()
    {
        var config = Migrate(V1(
            Menu("main", "Principal",
                Item("main.copy", "Copiar", "North", CtrlC, "edit.copy"),
                Item("main.folder", "Carpeta", "South", Folder),
                Item("main.more", "Más", "West", Group)),
            Menu("more", "Más", Item("more.future", "Futura", "North", FutureAction)))).Configuration!;

        var written = ConfigurationFile.Serialize(config);
        var reread = ConfigurationFile.Parse(written);

        Assert.Contains("\"schemaVersion\": 2", written);
        Assert.True(reread.IsOk);
        Assert.Equal(2, reread.FileVersion);
        Assert.Equal(written, ConfigurationFile.Serialize(reread.Configuration!));
        Assert.DoesNotContain(reread.Issues, i => i.Code == ConfigurationCodes.MigrationNote);
    }

    [Fact]
    public void TheMigratedFile_NoLongerCarriesInlineActionsOnLinkedPlacements()
    {
        var written = ConfigurationFile.Serialize(Migrate(V1(Menu("main", "Principal", Item("main.copy", "Copiar", "North", CtrlC, "edit.copy")))).Configuration!);

        var root = JsonNode.Parse(written)!.AsObject();
        var item = root["menus"]![0]!["items"]![0]!.AsObject();
        Assert.Equal("edit.copy", item["actionId"]!.GetValue<string>());
        Assert.Null(item["action"]);
        Assert.Null(item["name"]); // el nombre es el de la definición
        Assert.Equal(1, root["actions"]!.AsArray().Count);
    }

    [Fact]
    public void ACorruptV1File_IsStillCorrupt_AndAFutureVersionIsStillRejected()
    {
        Assert.Equal(ConfigurationParseStatus.Corrupt, ConfigurationFile.Parse("{\"schemaVersion\":1,\"menus\":5}").Status);
        Assert.Equal(ConfigurationParseStatus.Corrupt, ConfigurationFile.Parse("{ roto").Status);
        Assert.Equal(ConfigurationParseStatus.FutureVersion, ConfigurationFile.Parse("{\"schemaVersion\":3}").Status);
    }

    [Fact]
    public void AMalformedItem_IsLeftAlone_AndTheFileIsRejectedAsBefore()
    {
        var result = Migrate(V1(Menu("main", "Principal", Item("main.bad", "Mala", "North", "{\"type\":5}"))));

        Assert.Equal(ConfigurationParseStatus.Invalid, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.ActionTypeInvalid);
    }

    [Fact]
    public void AVersion2FileWithAnInlineKnownAction_IsStillReadable_AndMovesToTheLibraryOnSave()
    {
        var v2 = "{\"schemaVersion\":2,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":["
            + "{\"id\":\"main.a\",\"name\":\"Carpeta\",\"position\":\"North\",\"action\":" + Folder + "}]}]}";

        var config = ConfigurationFile.Parse(v2).Configuration!;
        var written = ConfigurationFile.Serialize(config);

        Assert.Empty(config.Actions);
        Assert.Contains("\"actionId\": \"custom.carpeta\"", written);
    }
}
