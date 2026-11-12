using System.Text.Json.Nodes;
using Radial.Core.Editing;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Storage;

/// <summary>
/// Destinos imposibles y acciones inválidas AL CARGAR <c>config.json</c>: la política de destinos es una sola (<c>ActionTargets</c>); un
/// destino que el ejecutor rechazaría siempre es un aviso (la configuración carga); y en la carga tolerante una acción de tipo conocido
/// con problemas propios se neutraliza ELLA sola, conservando su JSON, en vez de invalidar toda la configuración. La lectura estricta
/// (la del editor) no cambia.
/// </summary>
public class ConfigurationLoadValidationTests
{
    private const string GoodFolder = "{\"type\":\"openFolder\",\"path\":\"C:\\\\Docs\"}";

    private static string Doc(params string[] actions)
    {
        var items = string.Join(
            ",",
            actions.Select((action, index) => $"{{\"id\":\"i{index}\",\"name\":\"Elemento {index}\",\"position\":\"{Positions[index]}\",\"action\":{action}}}"));
        return "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":[" + items + "]}]}";
    }

    private static readonly string[] Positions = ["North", "East", "South", "West"];

    private static ConfigurationParseResult Strict(string json) => ConfigurationFile.Parse(json);

    private static ConfigurationParseResult Tolerant(string json) => ConfigurationFile.Parse(json, salvageInvalidActions: true);

    // La política de destinos

    [Theory]
    [InlineData(@"C:\Docs\a.txt", PathVerdict.Absolute)]
    [InlineData(@"D:\", PathVerdict.Absolute)]
    [InlineData(@"\\servidor\recurso\a.txt", PathVerdict.Absolute)]
    [InlineData(@"docs\a.txt", PathVerdict.Relative)]
    [InlineData(@"..\a.txt", PathVerdict.Relative)]
    [InlineData("a.txt", PathVerdict.Relative)]
    [InlineData("C:a.txt", PathVerdict.Relative)]
    [InlineData(@"\a.txt", PathVerdict.Relative)]
    [InlineData(@"\\?\C:\Docs", PathVerdict.Forbidden)]
    [InlineData(@"\\.\PhysicalDrive0", PathVerdict.Forbidden)]
    [InlineData("//./pipe/x", PathVerdict.Forbidden)]
    [InlineData(@"\??\C:\Docs", PathVerdict.Forbidden)]
    [InlineData(@"C:\Docs\a.txt:flujo", PathVerdict.Forbidden)]
    public void APath_IsClassifiedByItsShapeAlone(string path, PathVerdict expected) =>
        Assert.Equal(expected, ActionTargets.Classify(path));

    [Theory]
    [InlineData("notepad.exe", true)]
    [InlineData("calc", true)]
    [InlineData(@"C:\Windows\notepad.exe", true)]
    [InlineData(@"\\servidor\recurso\a.exe", true)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("ms-settings:", false)]
    [InlineData("shell:startup", false)]
    [InlineData(@"programas\a.exe", false)]
    [InlineData("a/b.exe", false)]
    [InlineData(@"\\?\C:\a.exe", false)]
    public void AProgramTarget_IsAnAbsolutePathOrABareName(string target, bool expected) =>
        Assert.Equal(expected, ActionTargets.IsProgramTarget(target));

    [Fact]
    public void ActionsThatDoNotOpenFilesOrPrograms_HaveNoTargetProblems()
    {
        Assert.Empty(ActionTargets.GetProblems(new OpenUrlAction("https://example.com")));
        Assert.Empty(ActionTargets.GetProblems(new SendKeysAction(new KeyChord(KeyModifiers.Control, "C"))));
        Assert.Empty(ActionTargets.GetProblems(new OpenMenuAction(new MenuId("a"))));
        Assert.Empty(ActionTargets.GetProblems(new UnknownAction("futura", "{\"type\":\"futura\"}")));
        Assert.Empty(ActionTargets.GetProblems(null));
    }

    [Fact]
    public void AnEmptyPath_IsTheBusinessOfTheActionsOwnValidation_NotOfTheTargetPolicy()
    {
        Assert.Empty(ActionTargets.GetProblems(new OpenFolderAction("")));
        Assert.Empty(ActionTargets.GetProblems(new LaunchApplicationAction("   ")));
    }

    [Fact]
    public void EachKindOfImpossibleTarget_IsReported()
    {
        Assert.Single(ActionTargets.GetProblems(new LaunchApplicationAction(@"programas\a.exe")));
        Assert.Single(ActionTargets.GetProblems(new LaunchApplicationAction("ms-settings:")));
        Assert.Single(ActionTargets.GetProblems(new LaunchApplicationAction("notepad.exe", null, "datos")));
        Assert.Equal(2, ActionTargets.GetProblems(new LaunchApplicationAction("a\\b.exe", null, "datos")).Count);
        Assert.Single(ActionTargets.GetProblems(new OpenFileAction("informe.pdf")));
        Assert.Single(ActionTargets.GetProblems(new OpenFileAction(@"C:\Docs\informe.pdf:flujo")));
        Assert.Single(ActionTargets.GetProblems(new OpenFolderAction(@"..\Documentos")));
        Assert.Single(ActionTargets.GetProblems(new OpenFolderAction(@"\\?\C:\Datos")));
    }

    [Fact]
    public void AValidTarget_HasNoProblems()
    {
        Assert.Empty(ActionTargets.GetProblems(new LaunchApplicationAction("notepad.exe")));
        Assert.Empty(ActionTargets.GetProblems(new LaunchApplicationAction(@"C:\Apps\a.exe", "--x", @"C:\Apps")));
        Assert.Empty(ActionTargets.GetProblems(new OpenFileAction(@"C:\Docs\a.txt")));
        Assert.Empty(ActionTargets.GetProblems(new OpenFolderAction(@"\\servidor\recurso")));
    }

    // Avisos al validar

    [Fact]
    public void AnImpossibleTarget_IsAWarning_AndTheConfigurationStillLoads()
    {
        var result = Strict(Doc(GoodFolder, "{\"type\":\"openFolder\",\"path\":\"docs\"}"));

        Assert.True(result.IsOk);
        var warning = Assert.Single(result.Issues, i => i.Code == ValidationCodes.ActionTargetRejected);
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
        Assert.Contains("carpeta", warning.Message);
        Assert.Equal(2, result.Configuration!.Menus[0].Items.Count); // nada se descarta
    }

    [Fact]
    public void ALinkedPlacement_IsNotWarnedAgain_TheLibraryDefinitionAlreadyIs()
    {
        // Un archivo v2: una definición con ruta relativa y dos colocaciones enlazadas a ella.
        const string json = """
            {
              "schemaVersion": 2,
              "rootMenuId": "main",
              "actions": [
                { "id": "x", "name": "X", "category": "custom", "icon": { "source": "glyph", "value": "Apps" }, "action": { "type": "openFolder", "path": "relativa" } }
              ],
              "menus": [
                { "id": "main", "name": "M", "items": [
                  { "id": "a", "position": "North", "actionId": "x" },
                  { "id": "b", "position": "East", "actionId": "x" }
                ] }
              ]
            }
            """;

        var result = Strict(json);

        Assert.True(result.IsOk);
        Assert.Single(result.Issues, i => i.Code == ValidationCodes.ActionTargetRejected);
    }

    [Fact]
    public void AConfigurationWithOnlyValidTargets_HasNoTargetWarnings()
    {
        var result = Strict(Doc(GoodFolder, "{\"type\":\"launchApplication\",\"path\":\"notepad.exe\"}", "{\"type\":\"openUrl\",\"url\":\"https://example.com\"}"));

        Assert.True(result.IsOk);
        Assert.DoesNotContain(result.Issues, i => i.Code == ValidationCodes.ActionTargetRejected);
    }

    // Carga tolerante: una acción inválida no tumba la configuración

    [Theory]
    [InlineData("{\"type\":\"openUrl\",\"url\":\"mailto:alguien@example.com\"}")]
    [InlineData("{\"type\":\"openUrl\",\"url\":\"javascript:alert(1)\"}")]
    [InlineData("{\"type\":\"openUrl\",\"url\":\"\"}")]
    [InlineData("{\"type\":\"openFolder\"}")]
    [InlineData("{\"type\":\"sendKeys\",\"key\":\"NoExiste\",\"modifiers\":[\"Control\"]}")]
    public void TheStrictReading_StillRejectsAnInvalidAction_AsTheEditorDoes(string action)
    {
        Assert.Equal(ConfigurationParseStatus.Invalid, Strict(Doc(action)).Status);
    }

    [Theory]
    [InlineData("{\"type\":\"openUrl\",\"url\":\"mailto:alguien@example.com\"}", "openUrl")]
    [InlineData("{\"type\":\"openUrl\",\"url\":\"javascript:alert(1)\"}", "openUrl")]
    [InlineData("{\"type\":\"openUrl\",\"url\":\"\"}", "openUrl")]
    [InlineData("{\"type\":\"openFolder\"}", "openFolder")]
    [InlineData("{\"type\":\"sendKeys\",\"key\":\"NoExiste\",\"modifiers\":[\"Control\"]}", "sendKeys")]
    public void TheTolerantLoad_NeutralizesOnlyTheInvalidAction_AndKeepsTheRestOfTheConfiguration(string bad, string type)
    {
        var result = Tolerant(Doc(bad, GoodFolder));

        Assert.True(result.IsOk, string.Join(" | ", result.Issues.Select(i => i.Message)));
        var items = result.Configuration!.Menus[0].Items;
        Assert.Equal(2, items.Count);
        var neutralized = Assert.IsType<UnknownAction>(items[0].Action);
        Assert.Equal(type, neutralized.Type);
        Assert.Equal(new OpenFolderAction(@"C:\Docs"), items[1].Action); // el resto, intacto

        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.ActionNeutralized && i.Severity == ValidationSeverity.Warning && i.Path.Length > 0);
        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Error);
        Assert.True(items[0].IsUnsupported);
    }

    [Fact]
    public void ANeutralizedAction_KeepsItsOriginalJson_AndSurvivesTheRoundTripWithoutLosingData()
    {
        const string bad = "{\"type\":\"openUrl\",\"url\":\"mailto:alguien@example.com\"}";
        var first = Tolerant(Doc(bad, GoodFolder));

        var written = ConfigurationFile.Serialize(first.Configuration!);
        var second = Tolerant(written);

        Assert.Contains("mailto:alguien@example.com", written);
        Assert.True(second.IsOk);
        var again = Assert.IsType<UnknownAction>(second.Configuration!.Menus[0].Items[0].Action);
        Assert.Equal("openUrl", again.Type);
        Assert.Contains("mailto:alguien@example.com", again.RawJson);

        // Estable: tras la primera normalización, volver a guardar y cargar no cambia nada.
        var stable = ConfigurationFile.Serialize(second.Configuration);
        Assert.Equal(stable, ConfigurationFile.Serialize(Tolerant(stable).Configuration!));
    }

    [Fact]
    public void ANeutralizedLibraryDefinition_NeutralizesItsPlacements_ConsistentlyAndWithOneWarningPerSource()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "rootMenuId": "main",
              "actions": [
                { "id": "x", "name": "X", "category": "custom", "icon": { "source": "glyph", "value": "Apps" }, "action": { "type": "openUrl", "url": "ftp://no.permitido" } },
                { "id": "ok", "name": "Ok", "category": "custom", "icon": { "source": "glyph", "value": "Apps" }, "action": { "type": "openFolder", "path": "C:\\Docs" } }
              ],
              "menus": [
                { "id": "main", "name": "M", "items": [
                  { "id": "a", "position": "North", "actionId": "x" },
                  { "id": "b", "position": "East", "actionId": "ok" }
                ] }
              ]
            }
            """;

        var result = Tolerant(json);

        Assert.True(result.IsOk, string.Join(" | ", result.Issues.Select(i => i.Message)));
        var items = result.Configuration!.Menus[0].Items;
        Assert.IsType<UnknownAction>(items.Single(i => i.Id.Value == "a").Action);
        Assert.Equal(new OpenFolderAction(@"C:\Docs"), items.Single(i => i.Id.Value == "b").Action);
        Assert.Single(result.Issues, i => i.Code == ConfigurationCodes.ActionNeutralized);
    }

    [Theory]
    [InlineData("{\"type\":\"openUrl\",\"url\":5}")]                       // tipo JSON equivocado: sigue siendo estructura
    [InlineData("{\"type\":\"openMenu\"}")]                                // un grupo sin destino es estructura del menú
    [InlineData("{\"type\":\"openMenu\",\"targetMenuId\":\"no-existe\"}")]
    [InlineData("{}")]
    public void TheTolerantLoad_DoesNotHideStructuralProblems(string action)
    {
        Assert.Equal(ConfigurationParseStatus.Invalid, Tolerant(Doc(action)).Status);
    }

    [Fact]
    public void TheTolerantLoad_DoesNotHideOtherValidationErrors()
    {
        var duplicate = "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":["
            + "{\"id\":\"x\",\"name\":\"A\",\"position\":\"North\",\"action\":" + GoodFolder + "},"
            + "{\"id\":\"x\",\"name\":\"B\",\"position\":\"North\",\"action\":" + GoodFolder + "}]}]}";

        Assert.Equal(ConfigurationParseStatus.Invalid, Tolerant(duplicate).Status);
    }

    [Fact]
    public void ARelativePathInATolerantLoad_IsOnlyAWarning_NotNeutralized()
    {
        var result = Tolerant(Doc("{\"type\":\"openFolder\",\"path\":\"docs\"}"));

        Assert.True(result.IsOk);
        Assert.IsType<OpenFolderAction>(result.Configuration!.Menus[0].Items[0].Action); // se conserva y falla al ejecutarse
        Assert.Contains(result.Issues, i => i.Code == ValidationCodes.ActionTargetRejected);
        Assert.DoesNotContain(result.Issues, i => i.Code == ConfigurationCodes.ActionNeutralized);
    }

    // Una acción inerte conserva identidad, vínculo y colocaciones al guardar y volver a cargar

    private const string InertOriginal = "{ \"type\": \"openUrl\", \"url\": \"mailto:alguien@example.com\", \"note\": \"campo extra\" }";

    // Biblioteca: una definición inválida («x», con una propiedad que Radial no conoce) y una buena. Tres colocaciones: dos enlazadas a «x»
    // (una con nombre e icono propios) y una a la buena.
    private const string InertDoc = """
        {
          "schemaVersion": 2,
          "rootMenuId": "main",
          "actions": [
            { "id": "x", "name": "Correo", "category": "custom", "icon": { "source": "glyph", "value": "Link" },
              "action": { "type": "openUrl", "url": "mailto:alguien@example.com", "note": "campo extra" } },
            { "id": "ok", "name": "Docs", "category": "custom", "icon": { "source": "glyph", "value": "Folder" },
              "action": { "type": "openFolder", "path": "C:\Docs" } }
          ],
          "menus": [
            { "id": "main", "name": "M", "items": [
              { "id": "a", "position": "North", "actionId": "x", "label": "Mi correo", "icon": { "source": "glyph", "value": "Star" } },
              { "id": "b", "position": "East", "actionId": "x" },
              { "id": "c", "position": "South", "actionId": "ok" }
            ] }
          ]
        }
        """;

    [Fact]
    public void AnInertAction_KeepsItsIdentityItsLinkAndItsPlacements_AfterSavingAndLoadingAgain()
    {
        var first = Tolerant(InertDoc);
        Assert.True(first.IsOk, string.Join(" | ", first.Issues.Select(i => i.Message)));

        var written = ConfigurationFile.Serialize(first.Configuration!);
        var second = Tolerant(written);
        Assert.True(second.IsOk, string.Join(" | ", second.Issues.Select(i => i.Message)));
        var config = second.Configuration!;

        // Biblioteca: misma definición, JSON original completo (también lo que Radial no conoce).
        var definition = Assert.Single(config.Actions, d => d.Id == "x");
        var inert = Assert.IsType<UnknownAction>(definition.Action);
        Assert.Equal("openUrl", inert.Type);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(InertOriginal), JsonNode.Parse(inert.RawJson)), inert.RawJson);
        Assert.Equal("Correo", definition.Name);
        Assert.Equal(new OpenFolderAction(@"C:\Docs"), config.Actions.Single(d => d.Id == "ok").Action);

        // Colocaciones: mismos identificadores y posiciones, SIGUEN enlazadas a la definición y conservan sus ajustes propios.
        var items = config.Menus[0].Items;
        var a = items.Single(i => i.Id.Value == "a");
        var b = items.Single(i => i.Id.Value == "b");
        var c = items.Single(i => i.Id.Value == "c");
        Assert.Equal(3, items.Count);
        Assert.Equal("x", a.DefinitionId);
        Assert.Equal("x", b.DefinitionId);
        Assert.Equal("ok", c.DefinitionId);
        Assert.Equal(RadialDirection.North, a.Position);
        Assert.Equal(RadialDirection.East, b.Position);
        Assert.Equal("Mi correo", a.LabelOverride);
        Assert.Equal(new IconReference(IconSource.Glyph, "Star"), a.IconOverride);
        Assert.Equal(inert, a.Action);
        Assert.Equal(inert, b.Action);
        Assert.True(a.IsUnsupported);

        // El archivo: las colocaciones son referencias (sin la acción en línea); el JSON vive una sola vez, en la biblioteca.
        var fileItems = JsonNode.Parse(written)!["menus"]![0]!["items"]!.AsArray();
        Assert.All(fileItems, item => Assert.Null(item!["action"]));
        Assert.Equal("x", fileItems[0]!["actionId"]!.GetValue<string>());
        Assert.Equal("x", fileItems[1]!["actionId"]!.GetValue<string>());

        // Estable: volver a guardar no cambia nada.
        Assert.Equal(written, ConfigurationFile.Serialize(config));
    }

    [Fact]
    public void AnInertAction_IsReportedOncePerAction_NotOncePerPlacementNorPerLoad()
    {
        var first = Tolerant(InertDoc);
        var second = Tolerant(ConfigurationFile.Serialize(first.Configuration!));

        foreach (var result in new[] { first, second })
        {
            Assert.Single(result.Issues, i => i.Code == ConfigurationCodes.ActionNeutralized);
            var unknown = Assert.Single(result.Issues, i => i.Code == ValidationCodes.ActionUnknownType);
            Assert.Equal("actions[x]", unknown.Path);
        }
    }

    [Fact]
    public void TheMessageOfAnInertAction_OnlyOffersWhatCanReallyBeDone()
    {
        var unknown = Assert.Single(Tolerant(InertDoc).Issues, i => i.Code == ValidationCodes.ActionUnknownType);

        Assert.DoesNotContain("Corrígela", unknown.Message);
        Assert.Contains("eliminarla", unknown.Message);      // se puede borrar desde la biblioteca
        Assert.Contains("config.json", unknown.Message);     // o corregir el archivo a mano con Radial cerrado
        Assert.Contains("cerrar Radial", unknown.Message);
    }

    [Fact]
    public void DeletingTheLibraryDefinitionOfAnInertAction_StillRemovesItsPlacements_AndPersistsThat()
    {
        var loaded = Tolerant(ConfigurationFile.Serialize(Tolerant(InertDoc).Configuration!)).Configuration!;
        var editor = new ConfigurationEditor(loaded);
        Assert.Equal(2, editor.PlacementsOf("x").Count);

        var refused = editor.DeleteDefinition("x", removePlacements: false);
        Assert.False(refused.Success);
        Assert.Equal(EditCodes.InUse, refused.Code);
        Assert.Contains(editor.Current.Actions, a => a.Id == "x"); // no cambió nada

        var done = editor.DeleteDefinition("x", removePlacements: true);
        Assert.True(done.Success, done.Message);
        Assert.DoesNotContain(editor.Current.Actions, a => a.Id == "x");
        Assert.Equal("c", Assert.Single(editor.Current.Menus[0].Items).Id.Value);

        var reloaded = Tolerant(ConfigurationFile.Serialize(editor.Current));
        Assert.True(reloaded.IsOk);
        Assert.DoesNotContain(reloaded.Issues, i => i.Code == ConfigurationCodes.ActionNeutralized);
        Assert.Equal("c", Assert.Single(reloaded.Configuration!.Menus[0].Items).Id.Value);
    }

    [Fact]
    public void AnUnknownPlacementWithoutADefinition_StaysInlineAsBefore()
    {
        // Lo que no tiene definición en la biblioteca (una acción de un tipo que esta versión no conoce, puesta directamente en el elemento) se guarda en línea, como siempre.
        var json = """
            {
              "schemaVersion": 2,
              "rootMenuId": "main",
              "actions": [
                { "id": "x", "name": "X", "category": "custom", "icon": { "source": "glyph", "value": "Link" },
                  "action": { "type": "openFolder", "path": "C:\Docs" } }
              ],
              "menus": [
                { "id": "main", "name": "M", "items": [
                  { "id": "f", "name": "Futura", "position": "North", "icon": { "source": "glyph", "value": "Link" }, "action": { "type": "futura", "dato": 1 } }
                ] }
              ]
            }
            """;

        var parsed = Tolerant(json);
        var written = ConfigurationFile.Serialize(parsed.Configuration!);

        Assert.True(parsed.IsOk);
        var item = JsonNode.Parse(written)!["menus"]![0]!["items"]![0]!;
        Assert.NotNull(item["action"]);
        Assert.Equal("futura", item["action"]!["type"]!.GetValue<string>());
        Assert.Null(item["actionId"]);
    }
}
