using System.Globalization;
using Radial.Core.Model;

namespace Radial.Windows.Shortcuts;

/// <summary>
/// Traduce la tecla de un <see cref="KeyChord"/> a un código de tecla virtual. Puro: no envía teclas, no usa P/Invoke y no
/// consulta el teclado, el idioma ni la distribución, así que el mismo nombre da siempre el mismo resultado.
///
/// QUÉ nombres de tecla existen lo decide <see cref="KeyVocabulary"/>, en Radial.Core: es la única lista, y es la misma que usa
/// <see cref="SendKeysAction.GetProblems"/> para validar. Este archivo solo asigna a cada nombre de esa lista su código VK y su
/// indicador de tecla extendida; no hay aquí una segunda lista de nombres permitidos. Si el vocabulario de Core ganara un nombre
/// sin código aquí, la inicialización fallaría (y una prueba lo comprueba) en lugar de aceptarlo a medias.
///
/// Tabla de códigos: letras y dígitos = su código ASCII (<c>VK_A</c> = 0x41, <c>VK_0</c> = 0x30); <c>F1</c>–<c>F24</c> =
/// 0x70–0x87; teclado numérico 0x60–0x6F; el resto, en <see cref="Special"/>. Las teclas de signos se identifican por su tecla
/// virtual <c>VK_OEM_*</c>, que es estable aunque el carácter que dan dependa de la distribución (<c>VK_OEM_3</c> es la Ñ en un
/// teclado español). «Extendida» (<c>KEYEVENTF_EXTENDEDKEY</c>) distingue p. ej. las flechas de las del teclado numérico.
/// </summary>
internal static class ShortcutParser
{
    private const ushort VkF1 = 0x70; // F24 = 0x87
    private const int MaxFunctionKey = 24;
    private const ushort VkNumpad0 = 0x60;

    // Nombre → (código VK, extendida) de lo que no es letra, dígito ni tecla de función.
    private static readonly (string Name, ushort Code, bool Extended)[] Special =
    [
        ("Enter", 0x0D, false), ("Tab", 0x09, false), ("Space", 0x20, false), ("Escape", 0x1B, false), ("Backspace", 0x08, false),
        ("Delete", 0x2E, true), ("Insert", 0x2D, true), ("Home", 0x24, true), ("End", 0x23, true), ("PageUp", 0x21, true), ("PageDown", 0x22, true),
        ("ArrowLeft", 0x25, true), ("ArrowUp", 0x26, true), ("ArrowRight", 0x27, true), ("ArrowDown", 0x28, true),
        ("NumpadMultiply", 0x6A, false), ("NumpadAdd", 0x6B, false), ("NumpadSubtract", 0x6D, false), ("NumpadDecimal", 0x6E, false),
        ("NumpadDivide", 0x6F, true), ("NumpadEnter", 0x0D, true),
        ("PrintScreen", 0x2C, true), ("Pause", 0x13, false), ("ContextMenu", 0x5D, true),
        ("Oem1", 0xBA, false), ("OemPlus", 0xBB, false), ("OemComma", 0xBC, false), ("OemMinus", 0xBD, false), ("OemPeriod", 0xBE, false),
        ("Oem2", 0xBF, false), ("Oem3", 0xC0, false), ("Oem4", 0xDB, false), ("Oem5", 0xDC, false), ("Oem6", 0xDD, false),
        ("Oem7", 0xDE, false), ("Oem8", 0xDF, false), ("Oem102", 0xE2, false),
        ("MediaPlayPause", 0xB3, false), ("MediaNextTrack", 0xB0, false), ("MediaPreviousTrack", 0xB1, false), ("MediaStop", 0xB2, false),
        ("VolumeUp", 0xAF, false), ("VolumeDown", 0xAE, false), ("VolumeMute", 0xAD, false),
        ("BrowserBack", 0xA6, false), ("BrowserForward", 0xA7, false), ("BrowserRefresh", 0xA8, false), ("BrowserHome", 0xAC, false),
    ];

    private static readonly Dictionary<string, (ushort Code, bool Extended)> SpecialByName = Special.ToDictionary(s => s.Name, s => (s.Code, s.Extended), StringComparer.Ordinal);

    // Indexada por el nombre canónico de Core.
    private static readonly Dictionary<string, VirtualKey> Translations = BuildTranslations();

    // Del código al nombre (para capturar una tecla pulsada). Con varios nombres para un código (Enter / NumpadEnter) manda el no extendido.
    private static readonly Dictionary<ushort, string> NamesByCode = BuildNamesByCode();

    /// <summary>Todas las teclas admitidas, con su nombre canónico (las de <see cref="KeyVocabulary.Names"/>).</summary>
    public static IReadOnlyCollection<VirtualKey> SupportedKeys => Translations.Values;

    /// <summary>
    /// Traduce el atajo completo. Los modificadores se conservan tal cual; solo se juzga la tecla. Falso si el atajo es nulo o
    /// la tecla no pertenece al vocabulario (<paramref name="shortcut"/> queda con su valor predeterminado).
    /// </summary>
    public static bool TryParse(KeyChord? chord, out ParsedShortcut shortcut)
    {
        if (chord is not null && TryParseKey(chord.Key, out var key))
        {
            shortcut = new ParsedShortcut(chord.Modifiers, key);
            return true;
        }

        shortcut = default;
        return false;
    }

    /// <summary>Traduce solo el nombre de la tecla. Falso si es nulo, está vacío o no pertenece al vocabulario de Core.</summary>
    public static bool TryParseKey(string? name, out VirtualKey key)
    {
        if (KeyVocabulary.TryGetCanonicalName(name, out var canonical) && Translations.TryGetValue(canonical, out key))
        {
            return true;
        }

        key = default;
        return false;
    }

    /// <summary>
    /// El nombre canónico de la tecla con este código de tecla virtual. <paramref name="extended"/> desambigua las que comparten
    /// código (<c>Enter</c> y <c>NumpadEnter</c>); si es <c>null</c> se elige la normal. Falso si no hay tecla del vocabulario con ese código.
    /// </summary>
    public static bool TryGetName(int virtualKey, bool? extended, out string name)
    {
        name = string.Empty;
        if (virtualKey is < 1 or > 0xFE)
        {
            return false;
        }

        var code = (ushort)virtualKey;
        if (extended == true && code == 0x0D)
        {
            name = "NumpadEnter";
            return true;
        }

        if (NamesByCode.TryGetValue(code, out var found))
        {
            name = found;
            return true;
        }

        return false;
    }

    private static Dictionary<string, VirtualKey> BuildTranslations()
    {
        var translations = new Dictionary<string, VirtualKey>(StringComparer.Ordinal);
        foreach (var name in KeyVocabulary.Names)
        {
            translations.Add(name, Translate(name));
        }

        return translations;
    }

    private static Dictionary<ushort, string> BuildNamesByCode()
    {
        var byCode = new Dictionary<ushort, string>();
        foreach (var key in Translations.Values.OrderBy(k => k.Extended)) // las no extendidas primero
        {
            byCode.TryAdd(key.Code, key.Name);
        }

        return byCode;
    }

    private static VirtualKey Translate(string name)
    {
        // Una letra o un dígito: VK_A..VK_Z y VK_0..VK_9 coinciden con el código ASCII del carácter en mayúscula.
        if (name.Length == 1 && (char.IsAsciiLetterUpper(name[0]) || char.IsAsciiDigit(name[0])))
        {
            return new VirtualKey(name, name[0], Extended: false);
        }

        if (name[0] == 'F'
            && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number is >= 1 and <= MaxFunctionKey)
        {
            return new VirtualKey(name, (ushort)(VkF1 + number - 1), Extended: false);
        }

        // Numpad0..Numpad9.
        if (name.Length == 7 && name.StartsWith("Numpad", StringComparison.Ordinal) && char.IsAsciiDigit(name[6]))
        {
            return new VirtualKey(name, (ushort)(VkNumpad0 + (name[6] - '0')), Extended: false);
        }

        if (SpecialByName.TryGetValue(name, out var special))
        {
            return new VirtualKey(name, special.Code, special.Extended);
        }

        throw new InvalidOperationException($"El nombre de tecla '{name}' del vocabulario de Radial.Core no tiene código de tecla virtual en Radial.Windows.");
    }
}
