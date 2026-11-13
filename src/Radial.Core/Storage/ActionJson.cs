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
    public const string TypeText = "typeText";
    public const string Volume = "volume";
    public const string WindowControl = "windowControl";

    private static readonly (KeyModifiers Flag, string Name)[] ModifierNames =
    [
        (KeyModifiers.Control, "Control"),
        (KeyModifiers.Shift, "Shift"),
        (KeyModifiers.Alt, "Alt"),
        (KeyModifiers.Windows, "Windows"),
    ];

    private static readonly (VolumeOperation Operation, string Name)[] VolumeNames =
    [
        (VolumeOperation.Increase, "increase"),
        (VolumeOperation.Decrease, "decrease"),
        (VolumeOperation.SetLevel, "setLevel"),
        (VolumeOperation.ToggleMute, "toggleMute"),
    ];

    private static readonly (WindowOperation Operation, string Name)[] WindowNames =
    [
        (WindowOperation.Minimize, "minimize"),
        (WindowOperation.Maximize, "maximize"),
        (WindowOperation.Restore, "restore"),
        (WindowOperation.Close, "close"),
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
                if (launch.Arguments is { Count: > 0 } arguments)
                {
                    json["arguments"] = new JsonArray(arguments.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());
                }

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

            case TypeTextAction text:
            {
                var json = Typed(TypeText);
                json["text"] = text.Text;
                return json;
            }

            case VolumeAction volume:
            {
                var json = Typed(Volume);
                json["operation"] = VolumeNames.FirstOrDefault(v => v.Operation == volume.Operation).Name
                    ?? throw new NotSupportedException("La operación de volumen no tiene nombre de serialización definido.");
                if (volume.Operation != VolumeOperation.ToggleMute)
                {
                    json["percent"] = volume.Percent;
                }

                return json;
            }

            case WindowControlAction window:
            {
                var json = Typed(WindowControl);
                json["operation"] = WindowNames.FirstOrDefault(w => w.Operation == window.Operation).Name
                    ?? throw new NotSupportedException("La operación de ventana no tiene nombre de serialización definido.");
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
    public static RadialAction? FromJson(JsonObject json, string path, List<ValidationIssue> issues, bool neutralizeInvalid = false)
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
                    ReadArguments(json, path, issues),
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

            case TypeText:
                action = new TypeTextAction(ReadString(json, "text", path, issues) ?? string.Empty);
                break;

            case Volume:
                action = new VolumeAction(
                    ReadOperation(json, path, issues, VolumeNames, (VolumeOperation)(-1)),
                    ReadInt(json, "percent", path, issues));
                break;

            case WindowControl:
                action = new WindowControlAction(ReadOperation(json, path, issues, WindowNames, (WindowOperation)(-1)));
                break;

            default:
                // Tipo desconocido: se conserva el objeto íntegro. Nunca se interpreta ni se ejecuta.
                return new UnknownAction(type, json.ToJsonString());
        }

        if (issues.Count != before)
        {
            return null;
        }

        // Carga tolerante: una acción de tipo conocido con problemas propios (esquema de URL no admitido, tecla fuera del vocabulario,
        // ruta vacía...) no debe tumbar toda la configuración. Se neutraliza ELLA sola: se conserva su JSON íntegro (se reescribe
        // tal cual al guardar), no se ejecuta y se avisa con su ruta. La estructura (tipos JSON equivocados, menús) sigue siendo estricta.
        if (neutralizeInvalid && action is not OpenMenuAction && action.GetProblems().ToList() is { Count: > 0 } problems)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                ConfigurationCodes.ActionNeutralized,
                $"La acción de tipo «{type}» no es válida y no se ejecutará (se conserva sin cambios en la configuración): {string.Join(" ", problems)}",
                path));
            return new UnknownAction(type, json.ToJsonString());
        }

        return action;
    }

    /// <summary>El discriminador es de un tipo de acción que esta versión conoce (distinto de «desconocido»: una acción neutralizada lo conserva).</summary>
    public static bool IsKnownType(string? type) =>
        type is LaunchApplication or OpenFile or OpenFolder or OpenUrl or SendKeys or OpenMenu or TypeText or Volume or WindowControl;

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

    // Una lista de textos (formato actual) o una línea de texto (formato antiguo, que se divide con las reglas de Windows).
    // Ausente o null = sin argumentos. Cualquier otro tipo JSON, o un elemento que no sea texto, es un problema de lectura.
    private static IReadOnlyList<string>? ReadArguments(JsonObject json, string path, List<ValidationIssue> issues)
    {
        if (!json.TryGetPropertyValue("arguments", out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var line))
        {
            var split = CommandLineArguments.Split(line);
            return split.Count == 0 ? null : split;
        }

        if (node is not JsonArray array)
        {
            issues.Add(Error(ConfigurationCodes.ActionPropertyInvalid, $"{path}.arguments", "El campo «arguments» debe ser una lista de textos."));
            return null;
        }

        var result = new List<string>(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is JsonValue element && element.TryGetValue<string>(out var text))
            {
                result.Add(text);
            }
            else
            {
                issues.Add(Error(ConfigurationCodes.ActionPropertyInvalid, $"{path}.arguments[{index}]", "Cada argumento debe ser un texto."));
            }
        }

        return result.Count == 0 ? null : result;
    }

    // Ausente o null = 0. Presente pero no un número entero = problema de lectura.
    private static int ReadInt(JsonObject json, string name, string path, List<ValidationIssue> issues)
    {
        if (!json.TryGetPropertyValue(name, out var node) || node is null)
        {
            return 0;
        }

        if (node is JsonValue value && value.TryGetValue<int>(out var number))
        {
            return number;
        }

        issues.Add(Error(ConfigurationCodes.ActionPropertyInvalid, $"{path}.{name}", $"El campo «{name}» debe ser un número entero."));
        return 0;
    }

    // Un nombre de operación desconocido o ausente NO es un problema de lectura: devuelve un valor fuera del enum y la validación del
    // modelo lo señala (la carga tolerante neutraliza la acción y conserva su JSON). Un campo que no es texto sí es estructura.
    private static T ReadOperation<T>(JsonObject json, string path, List<ValidationIssue> issues, (T Operation, string Name)[] names, T unknown)
    {
        var text = ReadString(json, "operation", path, issues);
        foreach (var (operation, name) in names)
        {
            if (string.Equals(name, text, StringComparison.Ordinal))
            {
                return operation;
            }
        }

        return unknown;
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
