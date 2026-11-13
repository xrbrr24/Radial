using System.Text.Json.Nodes;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Storage;

/// <summary>
/// Persistencia de los tipos de acción nuevos (insertar texto, volumen, ventana activa y argumentos como lista): lectura, escritura,
/// compatibilidad con la línea de argumentos antigua, rechazo estricto de lo inválido y neutralización en la carga tolerante
/// conservando el JSON original.
/// </summary>
public class NewActionTypesJsonTests
{
    private const string GoodFolder = "{\"type\":\"openFolder\",\"path\":\"C:\\\\Docs\"}";

    private static readonly string[] Positions = ["North", "East", "South", "West"];

    private static string Doc(params string[] actions)
    {
        var items = string.Join(
            ",",
            actions.Select((action, index) => $"{{\"id\":\"i{index}\",\"name\":\"Elemento {index}\",\"position\":\"{Positions[index]}\",\"action\":{action}}}"));
        return "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":[" + items + "]}]}";
    }

    private static ConfigurationParseResult Strict(string json) => ConfigurationFile.Parse(json);

    private static ConfigurationParseResult Tolerant(string json) => ConfigurationFile.Parse(json, salvageInvalidActions: true);

    private static T OnlyAction<T>(ConfigurationParseResult result)
        where T : RadialAction
    {
        Assert.True(result.IsOk, string.Join(" | ", result.Issues.Select(i => $"{i.Code} {i.Path} {i.Message}")));
        return Assert.IsType<T>(Assert.Single(result.Configuration!.Menus[0].Items).Action);
    }

    private static JsonObject WrittenAction(RadialConfiguration configuration, string itemId)
    {
        var written = JsonNode.Parse(ConfigurationFile.Serialize(configuration))!;
        var items = written["menus"]![0]!["items"]!.AsArray();
        var item = items.Single(i => i!["id"]!.GetValue<string>() == itemId)!;
        if (item["action"] is JsonObject inline)
        {
            return inline;
        }

        var definitionId = item["actionId"]!.GetValue<string>();
        return written["actions"]!.AsArray().Single(a => a!["id"]!.GetValue<string>() == definitionId)!["action"]!.AsObject();
    }

    // Insertar texto

    [Fact]
    public void TypeText_IsReadAndWrittenBackWithItsLineBreaksAndUnicode()
    {
        var result = Strict(Doc("{\"type\":\"typeText\",\"text\":\"Hola,\\r\\nmundo \\u00f1 \\ud83d\\ude00\\t.\"}"));

        var action = OnlyAction<TypeTextAction>(result);
        Assert.Equal("Hola,\r\nmundo ñ 😀\t.", action.Text);

        var written = WrittenAction(result.Configuration!, "i0");
        Assert.Equal("typeText", written["type"]!.GetValue<string>());
        Assert.Equal(action.Text, written["text"]!.GetValue<string>());
        Assert.Equal(action, OnlyAction<TypeTextAction>(Strict(ConfigurationFile.Serialize(result.Configuration!))));
    }

    // Volumen

    [Theory]
    [InlineData("increase", VolumeOperation.Increase, 10)]
    [InlineData("decrease", VolumeOperation.Decrease, 3)]
    [InlineData("setLevel", VolumeOperation.SetLevel, 0)]
    [InlineData("setLevel", VolumeOperation.SetLevel, 100)]
    public void Volume_WithAPercent_IsReadAndWrittenBack(string name, VolumeOperation operation, int percent)
    {
        var result = Strict(Doc($"{{\"type\":\"volume\",\"operation\":\"{name}\",\"percent\":{percent}}}"));

        Assert.Equal(new VolumeAction(operation, percent), OnlyAction<VolumeAction>(result));

        var written = WrittenAction(result.Configuration!, "i0");
        Assert.Equal("volume", written["type"]!.GetValue<string>());
        Assert.Equal(name, written["operation"]!.GetValue<string>());
        Assert.Equal(percent, written["percent"]!.GetValue<int>());
    }

    [Fact]
    public void Volume_ToggleMute_NeedsNoPercent_AndDoesNotWriteOne()
    {
        var result = Strict(Doc("{\"type\":\"volume\",\"operation\":\"toggleMute\"}"));

        Assert.Equal(new VolumeAction(VolumeOperation.ToggleMute), OnlyAction<VolumeAction>(result));
        Assert.Null(WrittenAction(result.Configuration!, "i0")["percent"]);
    }

    // Ventana

    [Theory]
    [InlineData("minimize", WindowOperation.Minimize)]
    [InlineData("maximize", WindowOperation.Maximize)]
    [InlineData("restore", WindowOperation.Restore)]
    [InlineData("close", WindowOperation.Close)]
    public void WindowControl_IsReadAndWrittenBack(string name, WindowOperation operation)
    {
        var result = Strict(Doc($"{{\"type\":\"windowControl\",\"operation\":\"{name}\"}}"));

        Assert.Equal(new WindowControlAction(operation), OnlyAction<WindowControlAction>(result));
        var written = WrittenAction(result.Configuration!, "i0");
        Assert.Equal("windowControl", written["type"]!.GetValue<string>());
        Assert.Equal(name, written["operation"]!.GetValue<string>());
    }

    // Argumentos

    [Fact]
    public void LaunchArguments_AsAnArray_AreReadWithoutAnyParsingAndWrittenBackAsAnArray()
    {
        var result = Strict(Doc("{\"type\":\"launchApplication\",\"path\":\"C:\\\\Apps\\\\a.exe\",\"arguments\":[\"--abrir\",\"mi doc.txt\",\"a & b\",\"\\\"q\\\"\"],\"workingDirectory\":\"C:\\\\Trabajo\"}"));

        var action = OnlyAction<LaunchApplicationAction>(result);
        Assert.Equal<string>(new[] { "--abrir", "mi doc.txt", "a & b", "\"q\"" }, action.Arguments);
        Assert.Equal(@"C:\Trabajo", action.WorkingDirectory);

        var written = WrittenAction(result.Configuration!, "i0");
        var array = Assert.IsType<JsonArray>(written["arguments"]);
        Assert.Equal<string>(new[] { "--abrir", "mi doc.txt", "a & b", "\"q\"" }, array.Select(a => a!.GetValue<string>()));
        Assert.Equal(@"C:\Trabajo", written["workingDirectory"]!.GetValue<string>());
    }

    [Fact]
    public void LaunchArguments_AsTheLegacyLine_AreSplitAndRewrittenAsAnArray()
    {
        var result = Strict(Doc("{\"type\":\"launchApplication\",\"path\":\"C:\\\\Apps\\\\a.exe\",\"arguments\":\"--abrir \\\"mi doc.txt\\\" --rapido\"}"));

        var action = OnlyAction<LaunchApplicationAction>(result);
        Assert.Equal<string>(new[] { "--abrir", "mi doc.txt", "--rapido" }, action.Arguments);
        Assert.IsType<JsonArray>(WrittenAction(result.Configuration!, "i0")["arguments"]);
    }

    [Theory]
    [InlineData("\"arguments\":[]")]
    [InlineData("\"arguments\":\"\"")]
    [InlineData("\"arguments\":\"   \"")]
    [InlineData("\"arguments\":null")]
    [InlineData("\"other\":1")]
    public void LaunchArguments_EmptyOrAbsent_MeanNoneAndAreNotWritten(string property)
    {
        var result = Strict(Doc("{\"type\":\"launchApplication\",\"path\":\"C:\\\\Apps\\\\a.exe\"," + property + "}"));

        Assert.Empty(OnlyAction<LaunchApplicationAction>(result).Arguments ?? []);
        Assert.Null(WrittenAction(result.Configuration!, "i0")["arguments"]);
    }

    // Lo inválido: rechazo estricto y neutralización tolerante

    public static TheoryData<string, string> InvalidButWellFormed => new()
    {
        { "{\"type\":\"volume\",\"operation\":\"explode\",\"percent\":5}", "volume" },
        { "{\"type\":\"volume\",\"percent\":5}", "volume" },
        { "{\"type\":\"volume\",\"operation\":\"increase\"}", "volume" },
        { "{\"type\":\"volume\",\"operation\":\"increase\",\"percent\":0}", "volume" },
        { "{\"type\":\"volume\",\"operation\":\"decrease\",\"percent\":101}", "volume" },
        { "{\"type\":\"volume\",\"operation\":\"setLevel\",\"percent\":-3}", "volume" },
        { "{\"type\":\"windowControl\",\"operation\":\"teleport\"}", "windowControl" },
        { "{\"type\":\"windowControl\"}", "windowControl" },
        { "{\"type\":\"typeText\",\"text\":\"\"}", "typeText" },
        { "{\"type\":\"typeText\"}", "typeText" },
        { "{\"type\":\"typeText\",\"text\":\"campana\\u0007\"}", "typeText" },
        { "{\"type\":\"launchApplication\",\"path\":\"\",\"arguments\":[\"--x\"]}", "launchApplication" },
        { "{\"type\":\"launchApplication\",\"path\":\"C:\\\\a.exe\",\"arguments\":[\"a\\u0000b\"]}", "launchApplication" },
    };

    [Theory]
    [MemberData(nameof(InvalidButWellFormed))]
    public void TheStrictReading_RejectsAnInvalidNewAction(string action, string type)
    {
        Assert.NotEmpty(type);
        Assert.Equal(ConfigurationParseStatus.Invalid, Strict(Doc(action)).Status);
    }

    [Theory]
    [MemberData(nameof(InvalidButWellFormed))]
    public void TheTolerantLoad_NeutralizesTheInvalidNewAction_KeepingItsJson_AndTheRestLoads(string action, string type)
    {
        var result = Tolerant(Doc(action, GoodFolder));

        Assert.True(result.IsOk, string.Join(" | ", result.Issues.Select(i => i.Message)));
        var items = result.Configuration!.Menus[0].Items;
        var neutralized = Assert.IsType<UnknownAction>(items[0].Action);
        Assert.Equal(type, neutralized.Type);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(action), JsonNode.Parse(neutralized.RawJson)), neutralized.RawJson);
        Assert.Equal(new OpenFolderAction(@"C:\Docs"), items[1].Action);
        Assert.Contains(result.Issues, i => i.Code == ConfigurationCodes.ActionNeutralized && i.Severity == ValidationSeverity.Warning);
        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Error);

        // Reescribir y volver a leer conserva el JSON original tal cual.
        var again = Assert.IsType<UnknownAction>(Tolerant(ConfigurationFile.Serialize(result.Configuration!)).Configuration!.Menus[0].Items[0].Action);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(action), JsonNode.Parse(again.RawJson)));
    }

    public static TheoryData<string> StructurallyWrong => new()
    {
        { "{\"type\":\"volume\",\"operation\":\"increase\",\"percent\":\"5\"}" },
        { "{\"type\":\"volume\",\"operation\":\"increase\",\"percent\":5.5}" },
        { "{\"type\":\"volume\",\"operation\":7,\"percent\":5}" },
        { "{\"type\":\"windowControl\",\"operation\":true}" },
        { "{\"type\":\"typeText\",\"text\":5}" },
        { "{\"type\":\"launchApplication\",\"path\":\"C:\\\\a.exe\",\"arguments\":5}" },
        { "{\"type\":\"launchApplication\",\"path\":\"C:\\\\a.exe\",\"arguments\":[\"ok\",3]}" },
        { "{\"type\":\"launchApplication\",\"path\":\"C:\\\\a.exe\",\"arguments\":{\"a\":1}}" },
    };

    [Theory]
    [MemberData(nameof(StructurallyWrong))]
    public void ATypeMismatchInTheJson_IsStructural_AndRejectsTheFileEvenInTheTolerantLoad(string action)
    {
        Assert.Equal(ConfigurationParseStatus.Invalid, Strict(Doc(action)).Status);
        Assert.Equal(ConfigurationParseStatus.Invalid, Tolerant(Doc(action)).Status);
    }

    // Una definición de biblioteca inválida conserva sus colocaciones enlazadas (misma política que el resto de tipos)

    [Fact]
    public void AnInvalidNewActionInTheLibrary_StaysLinkedToItsPlacements()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "rootMenuId": "main",
              "actions": [
                { "id": "vol", "name": "Volumen", "category": "multimedia", "icon": { "source": "glyph", "value": "VolumeUp" },
                  "action": { "type": "volume", "operation": "increase", "percent": 500 } }
              ],
              "menus": [
                { "id": "main", "name": "M", "items": [
                  { "id": "a", "position": "North", "actionId": "vol" }
                ] }
              ]
            }
            """;

        var result = Tolerant(json);

        Assert.True(result.IsOk, string.Join(" | ", result.Issues.Select(i => i.Message)));
        var item = Assert.Single(result.Configuration!.Menus[0].Items);
        Assert.Equal("vol", item.DefinitionId);
        Assert.IsType<UnknownAction>(item.Action);

        var written = JsonNode.Parse(ConfigurationFile.Serialize(result.Configuration!))!;
        Assert.Equal("vol", written["menus"]![0]!["items"]![0]!["actionId"]!.GetValue<string>());
        Assert.Equal(500, written["actions"]![0]!["action"]!["percent"]!.GetValue<int>());
    }
}
