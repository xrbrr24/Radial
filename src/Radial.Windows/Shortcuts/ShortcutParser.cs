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
/// 0x70–0x87; <c>Enter</c> = 0x0D; <c>Tab</c> = 0x09; flechas = 0x25–0x28, las cuatro extendidas (sin ese indicador pueden
/// confundirse con las del teclado numérico).
/// </summary>
internal static class ShortcutParser
{
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;
    private const ushort VkLeft = 0x25;
    private const ushort VkUp = 0x26;
    private const ushort VkRight = 0x27;
    private const ushort VkDown = 0x28;
    private const ushort VkF1 = 0x70; // F24 = 0x87
    private const int MaxFunctionKey = 24;

    // Indexada por el nombre canónico de Core.
    private static readonly Dictionary<string, VirtualKey> Translations = BuildTranslations();

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

    private static Dictionary<string, VirtualKey> BuildTranslations()
    {
        var translations = new Dictionary<string, VirtualKey>(StringComparer.Ordinal);
        foreach (var name in KeyVocabulary.Names)
        {
            translations.Add(name, Translate(name));
        }

        return translations;
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

        return name switch
        {
            "Enter" => new VirtualKey(name, VkReturn, Extended: false),
            "Tab" => new VirtualKey(name, VkTab, Extended: false),
            "ArrowLeft" => new VirtualKey(name, VkLeft, Extended: true),
            "ArrowUp" => new VirtualKey(name, VkUp, Extended: true),
            "ArrowRight" => new VirtualKey(name, VkRight, Extended: true),
            "ArrowDown" => new VirtualKey(name, VkDown, Extended: true),
            _ => throw new InvalidOperationException($"El nombre de tecla '{name}' del vocabulario de Radial.Core no tiene código de tecla virtual en Radial.Windows."),
        };
    }
}
