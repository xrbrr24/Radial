using Radial.Windows.Shortcuts;

namespace Radial.Windows.TextInput;

/// <summary>
/// Traducción pura de un texto a la secuencia de eventos de teclado que lo escribe, sin usar el portapapeles: cada unidad UTF-16 va
/// como carácter Unicode (<c>KEYEVENTF_UNICODE</c>, pulsar y soltar), de modo que no depende de la distribución del teclado ni de
/// los modificadores; un salto de línea (<c>\r\n</c>, <c>\n</c> o <c>\r</c> solos) es UNA pulsación de Intro y un tabulador, una de
/// Tab (muchas aplicaciones ignoran esas teclas si llegan como carácter Unicode). Los pares sustitutos se envían unidad a unidad
/// (pulsar y soltar la alta, pulsar y soltar la baja), como documenta Windows, y SIEMPRE juntos: nunca se reparten entre dos envíos.
/// No envía nada ni valida el texto (eso es de <c>TypeTextAction.GetProblems()</c>).
///
/// <see cref="BuildBatches"/> reparte esa misma secuencia en lotes de pocos caracteres completos: el ejecutor los envía con una
/// pausa entre ellos para que la aplicación que recibe el texto procese la entrada a su ritmo (ver <c>TypeTextActionExecutor</c>).
/// </summary>
internal static class TextSequence
{
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;

    /// <summary>Toda la secuencia, en orden, en una sola lista (equivale a unir los lotes de <see cref="BuildBatches"/>).</summary>
    public static IReadOnlyList<KeyboardEvent> Build(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var events = new List<KeyboardEvent>(text.Length * 2);
        foreach (var unit in Units(text))
        {
            events.AddRange(unit);
        }

        return events;
    }

    /// <summary>
    /// La misma secuencia repartida en lotes de, como mucho, <paramref name="charactersPerBatch"/> caracteres (un par sustituto, un
    /// salto de línea o un tabulador cuentan como UN carácter). Un lote nunca empieza ni acaba en mitad de un carácter, así que
    /// ninguna tecla queda pulsada entre dos lotes. El orden global no cambia.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<KeyboardEvent>> BuildBatches(string text, int charactersPerBatch)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(charactersPerBatch, 1);

        var batches = new List<IReadOnlyList<KeyboardEvent>>();
        var current = new List<KeyboardEvent>();
        var inCurrent = 0;
        foreach (var unit in Units(text))
        {
            current.AddRange(unit);
            if (++inCurrent == charactersPerBatch)
            {
                batches.Add(current);
                current = [];
                inCurrent = 0;
            }
        }

        if (current.Count > 0)
        {
            batches.Add(current);
        }

        return batches;
    }

    /// <summary>Los eventos de cada carácter del texto (pulsar y soltar), en orden.</summary>
    private static IEnumerable<List<KeyboardEvent>> Units(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            switch (c)
            {
                case '\r':
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    yield return Key(VkReturn);
                    break;

                case '\n':
                    yield return Key(VkReturn);
                    break;

                case '\t':
                    yield return Key(VkTab);
                    break;

                default:
                    var unit = new List<KeyboardEvent>(4) { Character(c, keyUp: false), Character(c, keyUp: true) };
                    if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                    {
                        // Un carácter fuera del plano básico (p. ej. un emoji): sus dos unidades forman UN carácter.
                        var low = text[++index];
                        unit.Add(Character(low, keyUp: false));
                        unit.Add(Character(low, keyUp: true));
                    }

                    yield return unit;
                    break;
            }
        }
    }

    private static KeyboardEvent Character(char c, bool keyUp) => new(c, false, keyUp, Unicode: true);

    private static List<KeyboardEvent> Key(ushort virtualKey) =>
        [new KeyboardEvent(virtualKey, false, false), new KeyboardEvent(virtualKey, false, true)];
}
