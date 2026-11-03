namespace Radial.Core.Model;

/// <summary>
/// ÚNICA definición de qué nombres de tecla admite un atajo (<see cref="KeyChord.Key"/>). Es una regla del dominio: la usa
/// la validación de <see cref="SendKeysAction"/> y la capa de Windows la recorre para asignar a cada nombre su código de
/// tecla virtual, sin llevar una lista propia. Los códigos de Windows NO viven aquí.
///
/// Vocabulario CERRADO (66 nombres), sin alias ni traducciones:
/// - Letras <c>A</c>–<c>Z</c> y dígitos <c>0</c>–<c>9</c> (la fila superior).
/// - <c>F1</c>–<c>F24</c>.
/// - <c>Enter</c>, <c>Tab</c>, <c>ArrowLeft</c>, <c>ArrowUp</c>, <c>ArrowRight</c>, <c>ArrowDown</c>.
/// Quedan fuera, entre otras, Ñ, símbolos y puntuación (dependen del teclado), el teclado numérico, Escape, Space, Backspace y
/// Delete. Ampliarlo es una decisión de diseño, no un detalle de este archivo.
///
/// Comparación: sin distinguir mayúsculas de minúsculas, solo ASCII, sin recortar espacios (<c>" A"</c> no es <c>A</c>) y sin
/// depender de la cultura. El nombre canónico (el de <see cref="Names"/>) es estable.
/// </summary>
public static class KeyVocabulary
{
    private const int LastFunctionKey = 24;

    // Orden de inicialización: lo que usan los demás miembros va antes.
    private static readonly string[] NamedKeys = ["Enter", "Tab", "ArrowLeft", "ArrowUp", "ArrowRight", "ArrowDown"];

    private static readonly Dictionary<string, string> ByName = BuildIndex();

    /// <summary>Los nombres canónicos admitidos: letras, dígitos, F1–F24 y las teclas con nombre. Solo lectura.</summary>
    public static IReadOnlyList<string> Names { get; } = BuildNames().AsReadOnly();

    /// <summary>Resumen legible del vocabulario para los mensajes de validación; se construye con la misma definición.</summary>
    public static string Description { get; } = $"A–Z, 0–9, F1–F{LastFunctionKey}, {string.Join(", ", NamedKeys)}";

    /// <summary>
    /// Devuelve el nombre canónico de <paramref name="name"/> si pertenece al vocabulario. Falso, sin lanzar, si es nulo, está
    /// vacío, lleva espacios, no es ASCII o no está en la lista.
    /// </summary>
    public static bool TryGetCanonicalName(string? name, out string canonicalName)
    {
        // Solo ASCII, por construcción. Medido en .NET 10: sin este filtro, OrdinalIgnoreCase ya rechaza el signo Kelvin, la 's'
        // larga, la 'i' sin punto y los caracteres de ancho completo, así que hoy no cambia ningún resultado. Se mantiene para
        // que "solo ASCII, sin depender de las tablas de mayúsculas del runtime ni de la cultura" sea una garantía y no una
        // coincidencia; las pruebas fijan esos casos.
        if (!string.IsNullOrEmpty(name) && System.Text.Ascii.IsValid(name) && ByName.TryGetValue(name, out var canonical))
        {
            canonicalName = canonical;
            return true;
        }

        canonicalName = string.Empty;
        return false;
    }

    private static List<string> BuildNames()
    {
        var names = new List<string>();

        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            names.Add(letter.ToString());
        }

        for (var digit = '0'; digit <= '9'; digit++)
        {
            names.Add(digit.ToString());
        }

        for (var number = 1; number <= LastFunctionKey; number++)
        {
            names.Add($"F{number}");
        }

        names.AddRange(NamedKeys);
        return names;
    }

    private static Dictionary<string, string> BuildIndex() =>
        BuildNames().ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
}
