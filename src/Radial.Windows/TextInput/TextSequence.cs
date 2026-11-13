using Radial.Windows.Shortcuts;

namespace Radial.Windows.TextInput;

/// <summary>
/// Traducción pura de un texto a la secuencia de eventos de teclado que lo escribe, sin usar el portapapeles: cada unidad UTF-16 va
/// como carácter Unicode (<c>KEYEVENTF_UNICODE</c>, pulsar y soltar), de modo que no depende de la distribución del teclado ni de
/// los modificadores; un salto de línea (<c>\r\n</c>, <c>\n</c> o <c>\r</c> solos) es UNA pulsación de Intro y un tabulador, una de
/// Tab (muchas aplicaciones ignoran esas teclas si llegan como carácter Unicode). Los pares sustitutos se envían unidad a unidad,
/// como documenta Windows. No envía nada ni valida el texto (eso es de <c>TypeTextAction.GetProblems()</c>).
/// </summary>
internal static class TextSequence
{
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;

    public static IReadOnlyList<KeyboardEvent> Build(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var events = new List<KeyboardEvent>(text.Length * 2);
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

                    AddKey(events, VkReturn);
                    break;

                case '\n':
                    AddKey(events, VkReturn);
                    break;

                case '\t':
                    AddKey(events, VkTab);
                    break;

                default:
                    events.Add(new KeyboardEvent(c, false, false, Unicode: true));
                    events.Add(new KeyboardEvent(c, false, true, Unicode: true));
                    break;
            }
        }

        return events;
    }

    private static void AddKey(List<KeyboardEvent> events, ushort virtualKey)
    {
        events.Add(new KeyboardEvent(virtualKey, false, false));
        events.Add(new KeyboardEvent(virtualKey, false, true));
    }
}
