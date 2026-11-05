using System.Text;
using System.Text.Json.Nodes;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Storage;

/// <summary>
/// El JSON de la configuración: ida y vuelta de todos los tipos de acción, acciones desconocidas conservadas íntegras, versiones del
/// esquema, migraciones encadenadas, límites de tamaño y profundidad, y rechazo controlado (sin lanzar) de contenido malformado.
/// </summary>
public class ConfigurationFileTests
{
    private const string UnknownRaw =
        "{\"type\":\"futureAction\",\"nested\":{\"list\":[1,2,{\"deep\":null}],\"n\":1.50},\"flag\":true,\"text\":\"hola\"}";

    private static readonly UnknownAction Unknown = new("futureAction", UnknownRaw);

    /// <summary>Una configuración con un submenú y los siete tipos de acción (seis conocidos y una desconocida).</summary>
    private static RadialConfiguration Sample()
    {
        var main = new MenuId("main");
        var more = new MenuId("more");
        return new RadialConfiguration
        {
            RootMenuId = main,
            Geometry = new GeometrySettings { DeadZoneRadius = 44, HysteresisDegrees = 7 },
            Navigation = new NavigationSettings
            {
                SubmenuDwell = TimeSpan.FromMilliseconds(450),
                BackDwell = TimeSpan.FromMilliseconds(900),
                MaxDepth = 4,
            },
            Menus =
            [
                new RadialMenu(
                    main,
                    "Principal",
                    [
                        new RadialItem(new ItemId("main.app"), "App", RadialDirection.North, new LaunchApplicationAction(@"C:\Apps\a.exe", "--x \"y\"", @"C:\Apps")),
                        new RadialItem(new ItemId("main.file"), "Archivo", RadialDirection.NorthEast, new OpenFileAction(@"C:\Docs\a.txt"), new IconReference(IconSource.File, @"C:\Docs\a.txt")),
                        new RadialItem(new ItemId("main.folder"), "Carpeta", RadialDirection.East, new OpenFolderAction(@"C:\Docs")),
                        new RadialItem(new ItemId("main.url"), "Web", RadialDirection.SouthEast, new OpenUrlAction("https://example.com/a?b=c")),
                        new RadialItem(
                            new ItemId("main.copy"),
                            "Copiar",
                            RadialDirection.South,
                            new SendKeysAction(new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, "C")),
                            new IconReference(IconSource.Glyph, "Copy"),
                            "edit.copy"),
                        new RadialItem(new ItemId("main.more"), "Más", RadialDirection.West, new OpenMenuAction(more), new IconReference(IconSource.Glyph, "Apps")),
                    ]),
                new RadialMenu(more, "Más", [new RadialItem(new ItemId("more.future"), "Futura", RadialDirection.North, Unknown)]),
            ],
        };
    }

    private static string Doc(string actionJson) => $$"""
        {
          "schemaVersion": 1,
          "rootMenuId": "main",
          "menus": [
            { "id": "main", "name": "Main", "items": [
              { "id": "i1", "name": "Uno", "position": "North", "action": {{actionJson}} }
            ] }
          ]
        }
        """;

    private static ConfigurationParseResult Parse(string json, ConfigurationMigrator? migrator = null) => ConfigurationFile.Parse(json, migrator);

    private static bool SameJson(string a, string b) => JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));

    // Ida y vuelta

    [Fact]
    public void ASerializedConfiguration_ParsesBackToTheSameConfiguration()
    {
        var original = Sample();

        var result = Parse(ConfigurationFile.Serialize(original));

        Assert.True(result.IsOk, string.Join(" | ", result.Issues.Select(i => $"{i.Code} {i.Path} {i.Message}")));
        var parsed = result.Configuration!;
        Assert.Equal(original.RootMenuId, parsed.RootMenuId);
        Assert.Equal(original.Geometry, parsed.Geometry);
        Assert.Equal(original.Navigation, parsed.Navigation);
        Assert.Equal(original.Menus.Count, parsed.Menus.Count);

        for (var m = 0; m < original.Menus.Count; m++)
        {
            Assert.Equal(original.Menus[m].Id, parsed.Menus[m].Id);
            Assert.Equal(original.Menus[m].Name, parsed.Menus[m].Name);
            Assert.Equal(original.Menus[m].Items.Count, parsed.Menus[m].Items.Count);

            for (var i = 0; i < original.Menus[m].Items.Count; i++)
            {
                var expected = original.Menus[m].Items[i];
                var actual = parsed.Menus[m].Items[i];
                Assert.Equal(expected.Id, actual.Id);
                Assert.Equal(expected.Name, actual.Name);
                Assert.Equal(expected.Position, actual.Position);
                Assert.Equal(expected.Icon, actual.Icon);
                Assert.Equal(expected.DefinitionId, actual.DefinitionId);

                if (expected.Action is UnknownAction unknown)
                {
                    var restored = Assert.IsType<UnknownAction>(actual.Action);
                    Assert.Equal(unknown.Type, restored.Type);
                    Assert.True(SameJson(unknown.RawJson, restored.RawJson));
                }
                else
                {
                    Assert.Equal(expected.Action, actual.Action);
                }
            }
        }
    }

    [Fact]
    public void Serializing_TheParsedResult_ProducesTheSameText()
    {
        var first = ConfigurationFile.Serialize(Sample());

        var second = ConfigurationFile.Serialize(Parse(first).Configuration!);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheFile_UsesTheCurrentSchemaVersion_CamelCaseNames_AndOmitsMissingValues()
    {
        var json = ConfigurationFile.Serialize(Sample());

        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"rootMenuId\": \"main\"", json);
        Assert.Contains("\"definitionId\": \"edit.copy\"", json);
        Assert.Contains("\"type\": \"sendKeys\"", json);
        Assert.Contains("\"submenuDwellMs\": 450", json);
        Assert.DoesNotContain("null", json.Replace("\"deep\": null", string.Empty)); // sin nulos: lo que falta no se escribe
        Assert.EndsWith("\n", json);
    }

    [Fact]
    public void EveryActionTypeOfTheModel_HasASerializer_SoANewTypeCannotBeForgotten()
    {
        var modelTypes = typeof(RadialAction).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(RadialAction)) && !t.IsAbstract)
            .ToHashSet();
        var sampled = Sample().Menus.SelectMany(m => m.Items).Select(i => i.Action.GetType()).ToHashSet();

        // Si falla: se añadió un tipo de acción a Core. Hay que darle serialización en ActionJson y añadirlo a Sample().
        Assert.True(modelTypes.SetEquals(sampled), "Tipos sin cubrir: " + string.Join(", ", modelTypes.Except(sampled).Select(t => t.Name)));
    }

    // Acciones desconocidas (7.1)

    [Fact]
    public void AnUnknownAction_IsKeptWithItsWholeJson_IncludingNestedPropertiesAndArrays()
    {
        var result = Parse(Doc(UnknownRaw));

        Assert.True(result.IsOk);
        var item = Assert.Single(result.Configuration!.Menus[0].Items);
        var action = Assert.IsType<UnknownAction>(item.Action);
        Assert.Equal("futureAction", action.Type);
        Assert.True(SameJson(UnknownRaw, action.RawJson));
        Assert.True(item.IsUnsupported);
    }

    [Fact]
    public void AnUnknownAction_SurvivesBeingRewrittenAndReadAgain_WithoutLosingData()
    {
        var parsed = Parse(Doc(UnknownRaw)).Configuration!;

        var reread = Parse(ConfigurationFile.Serialize(parsed)).Configuration!;

        var action = Assert.IsType<UnknownAction>(reread.Menus[0].Items[0].Action);
        Assert.True(SameJson(UnknownRaw, action.RawJson));
        var raw = JsonNode.Parse(action.RawJson)!.AsObject();
        Assert.Equal(1.5, raw["nested"]!["n"]!.GetValue<double>());
        Assert.Equal(2, raw["nested"]!["list"]!.AsArray()[1]!.GetValue<int>());
        Assert.Null(raw["nested"]!["list"]!.AsArray()[2]!["deep"]);
    }

    [Fact]
    public void AnUnknownAction_ProducesAWarning_NotAnError()
    {
        var result = Parse(Doc(UnknownRaw));

        Assert.True(result.IsOk);
        var warning = Assert.Single(result.Issues, i => i.Code == ValidationCodes.ActionUnknownType);
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
        Assert.Contains("i1", warning.Path);
    }

    [Theory]
    [InlineData("{}", ConfigurationCodes.ActionTypeMissing)]
    [InlineData("{\"type\":null}", ConfigurationCodes.ActionTypeMissing)]
    [InlineData("{\"type\":5}", ConfigurationCodes.ActionTypeInvalid)]
    [InlineData("{\"type\":\"\"}", ConfigurationCodes.ActionTypeInvalid)]
    [InlineData("{\"type\":\"  \"}", ConfigurationCodes.ActionTypeInvalid)]
    [InlineData("{\"type\":[\"openUrl\"]}", ConfigurationCodes.ActionTypeInvalid)]
    [InlineData("{\"type\":{\"a\":1}}", ConfigurationCodes.ActionTypeInvalid)]
    [InlineData("{\"type\":\"openUrl\",\"url\":5}", ConfigurationCodes.ActionPropertyInvalid)]
    [InlineData("{\"type\":\"openFolder\",\"path\":[\"a\"]}", ConfigurationCodes.ActionPropertyInvalid)]
    [InlineData("{\"type\":\"sendKeys\",\"key\":\"C\",\"modifiers\":\"Control\"}", ConfigurationCodes.ActionPropertyInvalid)]
    [InlineData("{\"type\":\"sendKeys\",\"key\":\"C\",\"modifiers\":[\"Hyper\"]}", ConfigurationCodes.ActionPropertyInvalid)]
    [InlineData("{\"type\":\"sendKeys\",\"key\":\"C\",\"modifiers\":[3]}", ConfigurationCodes.ActionPropertyInvalid)]
    public void AMalformedAction_IsRejectedInAControlledWay_WithoutThrowing(string actionJson, string expectedCode)
    {
        var result = Parse(Doc(actionJson));

        Assert.Equal(ConfigurationParseStatus.Invalid, result.Status);
        Assert.Null(result.Configuration);
        Assert.Contains(result.Issues, i => i.Code == expectedCode && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void AKnownActionMissingARequiredProperty_IsReportedByTheValidation()
    {
        var result = Parse(Doc("{\"type\":\"openUrl\"}"));

        Assert.Equal(ConfigurationParseStatus.Invalid, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ValidationCodes.ActionInvalid);
    }

    [Fact]
    public void AnItemWithoutAnAction_IsRejected()
    {
        var json = "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":[{\"id\":\"i\",\"name\":\"I\",\"position\":\"North\"}]}]}";

        var result = Parse(json);

        Assert.Equal(ConfigurationParseStatus.Invalid, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ValidationCodes.ActionMissing);
    }

    // Versiones del esquema

    [Fact]
    public void AFutureSchemaVersion_IsRejected_WithoutInterpretingTheRest()
    {
        var result = Parse("{\"schemaVersion\":99,\"somethingNew\":{\"a\":[1,2,3]},\"menus\":\"not even a list\"}");

        Assert.Equal(ConfigurationParseStatus.FutureVersion, result.Status);
        Assert.Null(result.Configuration);
        Assert.Equal(99, result.FileVersion);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.SchemaVersionFuture);
    }

    [Fact]
    public void ACurrentVersionPlusOne_IsFuture()
    {
        var result = Parse($"{{\"schemaVersion\":{ConfigurationFile.CurrentSchemaVersion + 1}}}");

        Assert.Equal(ConfigurationParseStatus.FutureVersion, result.Status);
    }

    [Theory]
    [InlineData("{}", ConfigurationCodes.SchemaVersionMissing)]
    [InlineData("{\"schemaVersion\":null}", ConfigurationCodes.SchemaVersionMissing)]
    [InlineData("{\"schemaVersion\":\"1\"}", ConfigurationCodes.SchemaVersionInvalid)]
    [InlineData("{\"schemaVersion\":1.5}", ConfigurationCodes.SchemaVersionInvalid)]
    [InlineData("{\"schemaVersion\":0}", ConfigurationCodes.SchemaVersionInvalid)]
    [InlineData("{\"schemaVersion\":-3}", ConfigurationCodes.SchemaVersionInvalid)]
    [InlineData("{\"schemaVersion\":true}", ConfigurationCodes.SchemaVersionInvalid)]
    public void AMissingOrInvalidSchemaVersion_IsCorrupt(string json, string expectedCode)
    {
        var result = Parse(json);

        Assert.Equal(ConfigurationParseStatus.Corrupt, result.Status);
        Assert.Contains(result.Issues, i => i.Code == expectedCode);
    }

    // Migraciones encadenadas

    private sealed class RenameRoot : IConfigurationMigration
    {
        public int FromVersion => 1;

        public void Migrate(JsonObject root)
        {
            root["rootMenuId"] = root["root"]!.DeepClone();
            root.Remove("root");
        }
    }

    private sealed class AddGeometry : IConfigurationMigration
    {
        public int FromVersion => 2;

        public void Migrate(JsonObject root) => root["geometry"] = new JsonObject { ["deadZoneRadius"] = 55 };
    }

    private const string VersionOneDocument =
        "{\"schemaVersion\":1,\"root\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":[]}]}";

    [Fact]
    public void Migrations_AreChained_FromTheFileVersionToTheTarget()
    {
        var migrator = new ConfigurationMigrator(3, [new RenameRoot(), new AddGeometry()]);

        var result = Parse(VersionOneDocument, migrator);

        Assert.True(result.IsOk, string.Join(" | ", result.Issues.Select(i => i.Message)));
        Assert.Equal(new MenuId("main"), result.Configuration!.RootMenuId);
        Assert.Equal(55.0, result.Configuration.Geometry.DeadZoneRadius);
        Assert.Equal(1, result.FileVersion);
    }

    [Fact]
    public void AFileAlreadyAtAnIntermediateVersion_OnlyRunsTheRemainingSteps()
    {
        var migrator = new ConfigurationMigrator(3, [new RenameRoot(), new AddGeometry()]);
        var versionTwo = "{\"schemaVersion\":2,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":[]}]}";

        var result = Parse(versionTwo, migrator);

        Assert.True(result.IsOk);
        Assert.Equal(55.0, result.Configuration!.Geometry.DeadZoneRadius);
    }

    [Fact]
    public void AMissingIntermediateMigration_FailsInAControlledWay()
    {
        var migrator = new ConfigurationMigrator(3, [new AddGeometry()]); // falta 1 -> 2

        var result = Parse(VersionOneDocument, migrator);

        Assert.Equal(ConfigurationParseStatus.Invalid, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.MigrationFailed);
    }

    private sealed class Throwing : IConfigurationMigration
    {
        public int FromVersion => 1;

        public void Migrate(JsonObject root) => throw new InvalidOperationException("paso roto");
    }

    [Fact]
    public void AMigrationThatThrows_FailsInAControlledWay()
    {
        var migrator = new ConfigurationMigrator(2, [new Throwing()]);

        var result = Parse(VersionOneDocument, migrator);

        Assert.Equal(ConfigurationParseStatus.Invalid, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.MigrationFailed);
    }

    [Fact]
    public void AVersionAboveTheMigratorsTarget_IsFuture()
    {
        var migrator = new ConfigurationMigrator(3, [new RenameRoot(), new AddGeometry()]);

        Assert.Equal(ConfigurationParseStatus.FutureVersion, Parse("{\"schemaVersion\":4}", migrator).Status);
    }

    [Fact]
    public void TheDefaultMigrator_TargetsTheCurrentVersion_AndHasNoStepsYet()
    {
        Assert.Equal(ConfigurationFile.CurrentSchemaVersion, ConfigurationMigrator.Default.TargetVersion);
    }

    [Fact]
    public void InvalidMigrationSets_AreRejectedWhenTheMigratorIsCreated()
    {
        Assert.Throws<ArgumentException>(() => new ConfigurationMigrator(0, []));
        Assert.Throws<ArgumentException>(() => new ConfigurationMigrator(3, [new RenameRoot(), new RenameRoot()]));
        Assert.Throws<ArgumentException>(() => new ConfigurationMigrator(2, [new AddGeometry()])); // parte de la 2: ya es la de destino
        Assert.Throws<ArgumentException>(() => new ConfigurationMigrator(1, [new RenameRoot()]));
    }

    // Límites y contenido malformado

    [Fact]
    public void AFileOverTheSizeLimit_IsCorrupt_WithoutBeingParsed()
    {
        var result = ConfigurationFile.Parse(new byte[ConfigurationFile.MaxBytes + 1]);

        Assert.Equal(ConfigurationParseStatus.Corrupt, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.TooLarge);
    }

    [Fact]
    public void AFileExactlyAtTheSizeLimit_IsNotRejectedForItsSize()
    {
        var bytes = new byte[ConfigurationFile.MaxBytes];
        Array.Fill(bytes, (byte)' ');

        var result = ConfigurationFile.Parse(bytes);

        Assert.DoesNotContain(result.Issues, i => i.Code == ConfigurationCodes.TooLarge);
    }

    [Fact]
    public void JsonNestedBeyondTheDepthLimit_IsCorrupt_WithoutThrowing()
    {
        var deep = new string('[', ConfigurationFile.MaxJsonDepth + 8) + new string(']', ConfigurationFile.MaxJsonDepth + 8);

        var result = Parse(deep);

        Assert.Equal(ConfigurationParseStatus.Corrupt, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.JsonInvalid);
    }

    [Fact]
    public void AnUnknownActionNestedBeyondTheDepthLimit_MakesTheWholeFileCorrupt()
    {
        var depth = ConfigurationFile.MaxJsonDepth + 4;
        var nested = "{\"type\":\"deep\",\"x\":" + string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth) + "}";

        var result = Parse(Doc(nested));

        Assert.Equal(ConfigurationParseStatus.Corrupt, result.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"schemaVersion\":1,")]
    [InlineData("{\"schemaVersion\":1} trailing")]
    [InlineData("{\"schemaVersion\":1,}")]
    [InlineData("{/* comment */\"schemaVersion\":1}")]
    public void ContentThatIsNotValidJson_IsCorrupt_WithoutThrowing(string json)
    {
        var result = Parse(json);

        Assert.Equal(ConfigurationParseStatus.Corrupt, result.Status);
        Assert.Null(result.Configuration);
        Assert.NotEmpty(result.Issues);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public void JsonThatIsNotAnObject_IsCorrupt(string json)
    {
        var result = Parse(json);

        Assert.Equal(ConfigurationParseStatus.Corrupt, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.RootInvalid);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"menus\":5}")]
    [InlineData("{\"schemaVersion\":1,\"menus\":[5]}")]
    [InlineData("{\"schemaVersion\":1,\"rootMenuId\":7}")]
    [InlineData("{\"schemaVersion\":1,\"geometry\":\"x\"}")]
    public void AWrongStructure_IsCorrupt_WithoutThrowing(string json)
    {
        var result = Parse(json);

        Assert.Equal(ConfigurationParseStatus.Corrupt, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.StructureInvalid);
    }

    [Fact]
    public void ATextFileWithAByteOrderMark_IsAccepted()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(ConfigurationFile.Serialize(Sample()))).ToArray();

        Assert.True(ConfigurationFile.Parse(bytes).IsOk);
    }

    [Fact]
    public void UnknownPropertiesOfTheKnownStructure_AreIgnored()
    {
        var json = "{\"schemaVersion\":1,\"extra\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"color\":\"red\",\"items\":[]}]}";

        Assert.True(Parse(json).IsOk);
    }

    // Contenido inválido que se detecta al convertir o validar

    [Theory]
    [InlineData("\"position\": \"Norte\"")]
    [InlineData("\"position\": 3")]
    [InlineData("\"position\": \"3\"")]
    public void AnInvalidPosition_IsRejected(string position)
    {
        var json = $$"""
            { "schemaVersion": 1, "rootMenuId": "main", "menus": [ { "id": "main", "name": "M", "items": [
              { "id": "i", "name": "I", {{position}}, "action": { "type": "openFolder", "path": "C:\\Temp" } } ] } ] }
            """;

        var result = Parse(json);

        Assert.NotEqual(ConfigurationParseStatus.Ok, result.Status);
    }

    [Fact]
    public void AnInvalidIconSource_IsRejected()
    {
        var json = """
            { "schemaVersion": 1, "rootMenuId": "main", "menus": [ { "id": "main", "name": "M", "items": [
              { "id": "i", "name": "I", "position": "North", "icon": { "source": "emoji", "value": "x" },
                "action": { "type": "openFolder", "path": "C:\\Temp" } } ] } ] }
            """;

        var result = Parse(json);

        Assert.Equal(ConfigurationParseStatus.Invalid, result.Status);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.FieldInvalid && i.Path.EndsWith("icon.source"));
    }

    [Fact]
    public void PositionNames_AreCaseInsensitive()
    {
        var result = Parse(Doc("{\"type\":\"openFolder\",\"path\":\"C:\\\\Temp\"}").Replace("\"North\"", "\"northeast\""));

        Assert.True(result.IsOk);
        Assert.Equal(RadialDirection.NorthEast, result.Configuration!.Menus[0].Items[0].Position);
    }

    [Fact]
    public void ValidationErrors_MakeTheFileInvalid_AndAreReportedWithTheirCodes()
    {
        var json = """
            { "schemaVersion": 1, "rootMenuId": "main", "menus": [ { "id": "main", "name": "M", "items": [
              { "id": "same", "name": "A", "position": "North", "action": { "type": "openFolder", "path": "C:\\A" } },
              { "id": "same", "name": "B", "position": "North", "action": { "type": "openMenu", "targetMenuId": "ghost" } } ] } ] }
            """;

        var result = Parse(json);

        Assert.Equal(ConfigurationParseStatus.Invalid, result.Status);
        Assert.Null(result.Configuration);
        Assert.Contains(result.Issues, i => i.Code == ValidationCodes.ItemDuplicateId);
        Assert.Contains(result.Issues, i => i.Code == ValidationCodes.ItemPositionDuplicate);
        Assert.Contains(result.Issues, i => i.Code == ValidationCodes.ActionMenuMissing);
    }

    [Fact]
    public void AnUnreachableMenu_IsAWarning_AndTheConfigurationStillLoads()
    {
        var json = "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":[]},{\"id\":\"orphan\",\"name\":\"O\",\"items\":[]}]}";

        var result = Parse(json);

        Assert.True(result.IsOk);
        Assert.Contains(result.Issues, i => i.Code == ValidationCodes.MenuUnreachable && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void TheDwellTimesAndDepth_AreReadFromTheFile()
    {
        var json = "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"navigation\":{\"submenuDwellMs\":250,\"backDwellMs\":1200,\"maxDepth\":2},\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":[]}]}";

        var navigation = Parse(json).Configuration!.Navigation;

        Assert.Equal(TimeSpan.FromMilliseconds(250), navigation.SubmenuDwell);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), navigation.BackDwell);
        Assert.Equal(2, navigation.MaxDepth);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("99999999999")]
    public void AnAbsurdDwell_IsRejectedWithoutThrowing(string milliseconds)
    {
        var json = $"{{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"navigation\":{{\"submenuDwellMs\":{milliseconds}}},\"menus\":[{{\"id\":\"main\",\"name\":\"M\",\"items\":[]}}]}}";

        var result = Parse(json);

        Assert.Equal(ConfigurationParseStatus.Invalid, result.Status);
    }

    [Fact]
    public void TheEmptyConfiguration_CanBeWrittenAndRead()
    {
        var result = Parse(ConfigurationFile.Serialize(RadialConfiguration.CreateEmpty()));

        Assert.True(result.IsOk);
        Assert.Single(result.Configuration!.Menus);
    }
}
