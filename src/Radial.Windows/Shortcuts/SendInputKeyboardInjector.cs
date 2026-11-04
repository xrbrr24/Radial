using System.Runtime.InteropServices;
using Radial.Windows.Native;

namespace Radial.Windows.Shortcuts;

/// <summary>
/// <see cref="IKeyboardInjector"/> real sobre <c>SendInput</c> (la API documentada de entrada de Windows). Envía toda la secuencia
/// en UNA llamada, así que Windows no intercala entrada de otro origen dentro de ella. Los eventos van sin <c>dwExtraInfo</c>:
/// NO llevan la marca de la autoprueba (<c>SyntheticMarker</c>), por lo que los hooks de captura los descartan por inyectados
/// incluso con <c>AcceptOwnSyntheticInput</c> activo. No usa <c>cmd</c>, PowerShell ni procesos externos.
///
/// <c>SendInput</c> devuelve cuántos eventos insertó, siempre contando desde el primero. Si inserta menos de los pedidos (no ocurre
/// salvo condiciones anómalas, p. ej. otro hilo bloqueó la entrada), el injector suelta lo que hubiera quedado pulsado con otra
/// llamada, comprueba cuántas liberaciones entraron y reintenta UNA vez las que faltaran. Si tras eso alguna sigue sin entrar, lo
/// comunica (<see cref="KeyInjectionResult.ReleaseFailed"/>): es el único caso en que Radial no puede garantizar el teclado limpio.
/// Si la propia llamada lanza una excepción NO se sabe cuántos eventos entraron (la API solo lo dice al devolver): se intenta soltar
/// toda tecla que la secuencia pulsa (soltar una tecla que no está pulsada es inocuo, dejar un modificador pulsado no) y se lanza
/// <see cref="KeyInjectionException"/>, que lleva la excepción original y si Windows aceptó esas liberaciones.
///
/// Límites que este tipo no puede resolver: (1) no se puede cancelar una vez enviado; (2) si la aplicación que recibe el teclado
/// se ejecuta elevada, Windows (UIPI) descarta la entrada sin que <c>SendInput</c> lo indique —la documentación de Windows lo
/// dice expresamente—, así que «aceptado por el sistema» no equivale a «procesado por la aplicación».
/// </summary>
public sealed class SendInputKeyboardInjector : IKeyboardInjector
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtended = 0x0001;
    private const uint KeyEventUp = 0x0002;

    private readonly Func<NativeMethods.INPUT[], (uint Inserted, int Win32Error)> _send;

    public SendInputKeyboardInjector()
        : this(SendToSystem)
    {
    }

    /// <summary>Permite probar los fallos y la liberación sin enviar entrada real.</summary>
    internal SendInputKeyboardInjector(Func<NativeMethods.INPUT[], (uint Inserted, int Win32Error)> send)
    {
        _send = send;
    }

    public KeyInjectionResult Send(IReadOnlyList<KeyboardEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            throw new ArgumentException("No hay ningún evento que enviar.", nameof(events));
        }

        uint inserted;
        int error;
        try
        {
            (inserted, error) = _send(ToInputs(events));
        }
        catch (Exception exception)
        {
            // No se sabe cuántos eventos entraron: se suelta todo lo que la secuencia pulsa y se informa, sin tragar el fallo y sin
            // darlo nunca por éxito. Si la liberación tampoco se confirma, quien llama debe poder avisar al usuario.
            var confirmed = TryRelease(ShortcutSequence.ReleasesFor(events.Where(e => !e.KeyUp).ToList()));
            throw new KeyInjectionException($"Falló el envío del teclado ({exception.GetType().Name}).", exception, confirmed);
        }

        if (inserted == events.Count)
        {
            return new KeyInjectionResult(true, events.Count);
        }

        // Aceptó menos de lo pedido: se suelta lo pulsado de lo que sí entró y se informa del fallo.
        var accepted = events.Take((int)Math.Min(inserted, (uint)events.Count)).ToList();
        var released = TryRelease(ShortcutSequence.ReleasesFor(accepted));
        return new KeyInjectionResult(false, accepted.Count, error, ReleaseFailed: !released);
    }

    /// <summary>
    /// Envía los eventos de soltar y comprueba cuántos entraron; reintenta UNA vez los que falten (nunca en bucle).
    /// </summary>
    /// <returns><c>true</c> si ya no queda ninguna liberación pendiente.</returns>
    private bool TryRelease(IReadOnlyList<KeyboardEvent> releases)
    {
        var remaining = releases;
        for (var attempt = 0; attempt < 2 && remaining.Count > 0; attempt++)
        {
            uint inserted;
            try
            {
                (inserted, _) = _send(ToInputs(remaining));
            }
            catch
            {
                inserted = 0; // no se sabe: se da por no enviada y se reintenta (soltar dos veces es inocuo)
            }

            remaining = remaining.Skip((int)Math.Min(inserted, (uint)remaining.Count)).ToList();
        }

        return remaining.Count == 0;
    }

    private static NativeMethods.INPUT[] ToInputs(IReadOnlyList<KeyboardEvent> events)
    {
        var inputs = new NativeMethods.INPUT[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            inputs[i] = new NativeMethods.INPUT
            {
                type = InputKeyboard,
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = e.Code,
                    dwFlags = (e.KeyUp ? KeyEventUp : 0) | (e.Extended ? KeyEventExtended : 0),
                    dwExtraInfo = IntPtr.Zero,
                },
            };
        }

        return inputs;
    }

    private static (uint Inserted, int Win32Error) SendToSystem(NativeMethods.INPUT[] inputs)
    {
        var inserted = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        return (inserted, inserted == inputs.Length ? 0 : Marshal.GetLastWin32Error());
    }
}
