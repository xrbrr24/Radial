using System.Text.Json;
using System.Text.Json.Nodes;
using Radial.Core.Model;
using Radial.Core.Validation;

namespace Radial.Core.Storage;

/// <summary>
/// Conversión entre <see cref="RadialAction"/> y el objeto JSON de la acción, con el discriminador <c>type</c> (sección 7.1 del
/// documento técnico). Se hace a mano sobre <see cref="JsonObject"/> porque el polimorfismo de <c>System.Text.Json</c> no conserva
/// los datos de un discriminador desconocido. Un tipo desconocido se lee como <see cref="UnknownAction"/> con el objeto original
/// íntegro y se reescribe tal cual. Un objeto sin <c>type</c>, o con un <c>type</c> que no es texto, no se interpreta: se informa
/// y quien lee rechaza la configuración (sin lanzar).
/// </summary>
internal static class ActionJson
{
    public const string LaunchApplication = "launchApplication";
    public const string OpenFile = "openFile";
    public const string OpenFolder = "openFolder";
    public const string OpenUrl = "openUrl";
    public const string SendKeys = "sendKeys";
    public const string OpenMenu = "openMenu";

    private static readonly (KeyModifiers Flag, string Name)[] ModifierNames =
    [
        (KeyModifiers.Control, "Control"),
        (KeyModifiers.Shift, "Shift"),
        (KeyModifiers.Alt, "Alt"),
        (KeyModifiers.Windows, "Windows"),
    ];

    /// <exception cref="NotSupportedException">Un tipo de acción nuevo del modelo todavía no tiene serialización.</exception>
    /// <exception cref="InvalidOperationException">Una <see cref="UnknownAction"/> cuyo JSON original no es un objeto.</exception>
    public static JsonObject ToJson(RadialAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        switch (action)
        {
            case LaunchApplicationAction launch:
            {
                var json = Typed(LaunchApplication);
                json["path"] = launch.Path;
                SetIfNotNull(json, "arguments", launch.Arguments);
                SetIfNotNull(json, "workingDirectory", launch.WorkingDirectory);
                return json;
            }

            case OpenFileAction file:
            {
                var json = Typed(OpenFile);
                json["path"] = file.Path;
                return json;
            }

            case OpenFolderAction folder:
            {
                var json = Typed(OpenFolder);
                json["path"] = folder.Path;
                return json;
            }

            case OpenUrlAction url:
            {
                var json = Typed(OpenUrl);
                json["url"] = url.Url;
                return json;
            }

            case SendKeysAction keys:
            {
                var json = Typed(SendKeys);
                var modifiers = new JsonArray();
                foreach (var (flag, name) in ModifierNames)
                {
                    if ((keys.Chord.Modifiers & flag) != 0)
                    {
                        modifiers.Add(name);
                    }
                }

                json["modifiers"] = modifiers;
                json["key"] = keys.Chord.Key;
                return json;
            }

            case OpenMenuAction menu:
            {
                var json = Typed(OpenMenu);
                json["targetMenuId"] = menu.TargetMenuId.Value;
                return json;
            }

            case UnknownAction unknown:
                return ParseOriginal(unknown);

            default:
                throw new NotSupportedException($"El tipo de acción {action.GetType().Name} no tiene serialización definida.");
        }
    }

    /// <summary>
    /// Lee una acción. Devuelve <c>null</c> (con el problema en <paramref name="issues"/>) si el discriminador falta o no es
    /// texto, o si una propiedad tiene un tipo JSON equivocado. Una propiedad obligatoria AUSENTE no es un problema de lectura: la
    /// acción se crea con texto vacío y la validación del modelo la señala con su ruta.
    /// </summary>
    public static RadialAction? FromJson(JsonObject json, string path, List<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (!json.TryGetPropertyValue("type", out var typeNode) || typeNode is null)
        {
            issues.Add(Error(ConfigurationCodes.ActionTypeMissing, path, "La acción no tiene el campo «type»."));
            return null;
        }

        if (typeNode is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type) || string.IsNullOrWhiteSpace(type))
        {
            issues.Add(Error(ConfigurationCodes.ActionTypeInvalid, path, "El campo «type» de la acción debe ser un texto no vacío."));
            return null;
        }

        var before = issues.Count;
        RadialAction? action;

        switch (type)
        {
            case LaunchApplication:
                action = new LaunchApplicationAction(
                    ReadString(json, "path", path, issues) ?? string.Empty,
                    ReadString(json, "arguments", path, issues),
                    ReadString(json, "workingDirectory", path, issues));
                break;

            case OpenFile:
                action = new OpenFileAction(ReadString(json, "path", path, issues) ?? string.Empty);
                break;

            case OpenFolder:
                action = new OpenFolderAction(ReadString(json, "path", path, issues) ?? string.Empty);
                break;

            case OpenUrl:
                action = new OpenUrlAction(ReadString(json, "url", path, issues) ?? string.Empty);
                break;

            case SendKeys:
                action = new SendKeysAction(new KeyChord(
                    ReadModifiers(json, path, issues),
                    ReadString(json, "key", path, issues) ?? string.Empty));
                break;

            case OpenMenu:
                action = new OpenMenuAction(new MenuId(ReadString(json, "targetMenuId", path, issues) ?? string.Empty));
                break;

            default:
                // Tipo desconocido: se conserva el objeto íntegro. Nunca se interpreta ni se ejecuta.
                return new UnknownAction(type, json.ToJsonString());
        }

        return issues.Count == before ? action : null;
    }

    private static JsonObject Typed(string type) => new() { ["type"] = type };

    private static void SetIfNotNull(JsonObject json, string name, string? value)
    {
        if (value is not null)
        {
            json[name] = value;
        }
    }

    private static JsonObject ParseOriginal(UnknownAction unknown)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(unknown.RawJson);
        }
        catch (JsonException)
        {
            node = null;
        }

        return node as JsonObject
            ?? throw new InvalidOperationException("El JSON original de una acción de tipo desconocido no es un objeto JSON.");
    }

    // Ausente o null = null (sin problema). Presente pero no texto = problema.
    private static string? ReadString(JsonObject json, string name, string path, List<ValidationIssue> issues)
    {
        if (!json.TryGetPropertyValue(name, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        issues.Add(Error(ConfigurationCodes.ActionPropertyInvalid, $"{path}.{name}", $"El campo «{name}» debe ser un texto."));
        return null;
    }

    private static KeyModifiers ReadModifiers(JsonObject json, string path, List<ValidationIssue> issues)
    {
        if (!json.TryGetPropertyValue("modifiers", out var node) || node is null)
        {
            return KeyModifiers.None;
        }

        if (node is not JsonArray array)
        {
            issues.Add(Error(ConfigurationCodes.ActionPropertyInvalid, $"{path}.modifiers", "El campo «modifiers» debe ser una lista de nombres."));
            return KeyModifiers.None;
        }

        var result = KeyModifiers.None;
        for (var index = 0; index < array.Count; index++)
        {
            var modifier = KeyModifiers.None;
            if (array[index] is JsonValue value && value.TryGetValue<string>(out var name))
            {
                foreach (var (flag, known) in ModifierNames)
                {
                    if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase))
                    {
                        modifier = flag;
                        break;
                    }
                }
            }

            if (modifier == KeyModifiers.None)
            {
                issues.Add(Error(ConfigurationCodes.ActionPropertyInvalid, $"{path}.modifiers[{index}]",
                    "Cada modificador debe ser uno de: Control, Shift, Alt, Windows."));
            }

            result |= modifier;
        }

        return result;
    }

    private static ValidationIssue Error(string code, string path, string message) =>
        new(ValidationSeverity.Error, code, message, path);
}
