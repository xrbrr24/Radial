using Radial.Core.Model;

namespace Radial.Windows.Shortcuts;

/// <summary>
/// Un evento de teclado que se va a enviar: tecla virtual (<c>VK_*</c>), si es «extendida» y si es de soltar. Solo datos: no envía nada.
/// </summary>
public readonly record struct KeyboardEvent(ushort Code, bool Extended, bool KeyUp);

/// <param name="EventsInserted">Cuántos eventos aceptó el sistema (de los pedidos, contando desde el primero).</param>
/// <param name="Win32Error">Código de error de Windows si <see cref="Success"/> es falso (puede ser 0 si el sistema no lo dio).</param>
/// <param name="ReleaseFailed">
/// Solo con <see cref="Success"/> falso: tras el fallo no se pudo confirmar la liberación de alguna tecla que había quedado
/// pulsada (incluso tras reintentarla), así que Windows podría seguir viéndola pulsada.
/// </param>
public sealed record KeyInjectionResult(bool Success, int EventsInserted, int Win32Error = 0, bool ReleaseFailed = false);

/// <summary>
/// La llamada al sistema que envía el teclado lanzó una excepción. No se sabe cuántos eventos llegaron a insertarse (la API solo lo
/// informa al devolver, y aquí no devolvió), así que se intentó soltar toda tecla que la secuencia pulsa.
/// <see cref="ReleaseConfirmed"/> dice si Windows aceptó esas liberaciones: si es falso, alguna tecla podría seguir pulsada.
/// La excepción original queda en <see cref="Exception.InnerException"/>; nunca se interpreta como éxito.
/// </summary>
public sealed class KeyInjectionException(string message, Exception inner, bool releaseConfirmed) : Exception(message, inner)
{
    public bool ReleaseConfirmed { get; } = releaseConfirmed;
}

/// <summary>
/// Envía eventos de teclado al sistema. Es la ÚNICA entrada de teclado sintética que usa Radial fuera de la autoprueba y está
/// separada de ella y de la captura global: no usa <c>SyntheticInput</c> ni <c>GuardedSyntheticInput</c> y no lleva la marca de
/// la autoprueba, de modo que los hooks de captura ignoran estos eventos por inyectados (también durante la autoprueba).
/// </summary>
public interface IKeyboardInjector
{
    /// <summary>
    /// Envía la secuencia completa de una sola vez (sin que se intercale entrada de otro origen). Si el sistema no la acepta
    /// entera, libera lo que hubiera quedado pulsado y devuelve el fallo; no lanza por fallos del sistema. Si la propia llamada
    /// lanza, intenta soltar toda tecla que la secuencia pulsa y lanza <see cref="KeyInjectionException"/>.
    /// </summary>
    /// <exception cref="KeyInjectionException">La llamada al sistema lanzó una excepción.</exception>
    KeyInjectionResult Send(IReadOnlyList<KeyboardEvent> events);
}

/// <summary>Qué modificadores están pulsados FÍSICAMENTE ahora mismo (los cuatro, de cualquier lado).</summary>
public interface IModifierStateReader
{
    KeyModifiers ReadPressed();
}

/// <summary>
/// Traducción pura de un atajo a la secuencia de eventos que lo produce: modificadores pulsados en orden fijo (Ctrl, Mayús, Alt,
/// Win), la tecla pulsada y soltada, y los modificadores soltados en orden inverso. Así Ctrl+Mayús+S o Win+E son datos distintos
/// de la misma función, no clases distintas.
/// </summary>
internal static class ShortcutSequence
{
    private const ushort VkLeftShift = 0xA0;
    private const ushort VkLeftControl = 0xA2;
    private const ushort VkLeftMenu = 0xA4; // Alt
    private const ushort VkLeftWindows = 0x5B;

    // Orden en que se pulsan.
    private static readonly (KeyModifiers Modifier, KeyboardEvent Down)[] Order =
    [
        (KeyModifiers.Control, new KeyboardEvent(VkLeftControl, false, false)),
        (KeyModifiers.Shift, new KeyboardEvent(VkLeftShift, false, false)),
        (KeyModifiers.Alt, new KeyboardEvent(VkLeftMenu, false, false)),
        (KeyModifiers.Windows, new KeyboardEvent(VkLeftWindows, true, false)),
    ];

    public static IReadOnlyList<KeyboardEvent> Build(ParsedShortcut shortcut)
    {
        var events = new List<KeyboardEvent>();
        var held = new List<KeyboardEvent>();

        foreach (var (modifier, down) in Order)
        {
            if ((shortcut.Modifiers & modifier) != 0)
            {
                events.Add(down);
                held.Add(down);
            }
        }

        events.Add(new KeyboardEvent(shortcut.Key.Code, shortcut.Key.Extended, KeyUp: false));
        events.Add(new KeyboardEvent(shortcut.Key.Code, shortcut.Key.Extended, KeyUp: true));

        for (var i = held.Count - 1; i >= 0; i--)
        {
            events.Add(held[i] with { KeyUp = true });
        }

        return events;
    }

    /// <summary>
    /// Los eventos de soltar que faltan para dejar el teclado como estaba, dado lo que ya se envió: cada tecla pulsada y no
    /// soltada, de la última a la primera. Vacío si no quedó nada pulsado.
    /// </summary>
    public static IReadOnlyList<KeyboardEvent> ReleasesFor(IReadOnlyList<KeyboardEvent> sent)
    {
        var down = new List<KeyboardEvent>();
        foreach (var e in sent)
        {
            if (!e.KeyUp)
            {
                down.Add(e);
            }
            else
            {
                var index = down.FindLastIndex(d => d.Code == e.Code);
                if (index >= 0)
                {
                    down.RemoveAt(index);
                }
            }
        }

        var releases = new List<KeyboardEvent>(down.Count);
        for (var i = down.Count - 1; i >= 0; i--)
        {
            releases.Add(down[i] with { KeyUp = true });
        }

        return releases;
    }
}
