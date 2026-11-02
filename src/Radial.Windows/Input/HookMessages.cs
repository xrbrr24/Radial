namespace Radial.Windows.Input;

/// <summary>
/// Traducción de los mensajes y flags de los hooks de bajo nivel a <see cref="RawInputEvent"/>. Pura y sin asignaciones,
/// para poder comprobarla sin instalar hooks.
/// </summary>
internal static class HookMessages
{
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_MOUSEHWHEEL = 0x020E;

    public const int VK_ESCAPE = 0x1B;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;

    // KBDLLHOOKSTRUCT.flags
    private const int LLKHF_INJECTED = 0x10;
    private const int LLKHF_LOWER_IL_INJECTED = 0x02;

    // MSLLHOOKSTRUCT.flags
    private const int LLMHF_INJECTED = 0x01;
    private const int LLMHF_LOWER_IL_INJECTED = 0x02;

    /// <summary>Solo Ctrl izquierdo, Ctrl derecho y Escape interesan; el resto de teclas no entra en la lógica.</summary>
    public static bool TryMapKeyboard(int message, int vkCode, int flags, long extraInfo, long timestamp, out RawInputEvent result)
    {
        result = default;

        var key = vkCode switch
        {
            VK_LCONTROL => InputKey.LeftControl,
            VK_RCONTROL => InputKey.RightControl,
            VK_ESCAPE => InputKey.Escape,
            _ => InputKey.None,
        };

        if (key == InputKey.None)
        {
            return false;
        }

        var kind = message switch
        {
            WM_KEYDOWN or WM_SYSKEYDOWN => InputEventKind.KeyDown,
            WM_KEYUP or WM_SYSKEYUP => InputEventKind.KeyUp,
            _ => (InputEventKind?)null,
        };

        if (kind is null)
        {
            return false;
        }

        var injected = (flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0;
        result = new RawInputEvent(kind.Value, key, default, injected, timestamp, IsOwn(injected, extraInfo));
        return true;
    }

    /// <summary>Movimiento, botón central y rueda. Los demás botones no participan.</summary>
    public static bool TryMapMouse(int message, int x, int y, int flags, long extraInfo, long timestamp, out RawInputEvent result)
    {
        var kind = message switch
        {
            WM_MOUSEMOVE => InputEventKind.Move,
            WM_MBUTTONDOWN => InputEventKind.MiddleDown,
            WM_MBUTTONUP => InputEventKind.MiddleUp,
            WM_MOUSEWHEEL or WM_MOUSEHWHEEL => InputEventKind.Wheel,
            _ => (InputEventKind?)null,
        };

        if (kind is null)
        {
            result = default;
            return false;
        }

        var injected = (flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0;
        result = new RawInputEvent(kind.Value, InputKey.None, new ScreenPoint(x, y), injected, timestamp, IsOwn(injected, extraInfo));
        return true;
    }

    // Propio = inyectado Y con la marca de este proceso. Un evento físico nunca lo es aunque su dwExtraInfo coincidiera.
    private static bool IsOwn(bool injected, long extraInfo) => injected && SyntheticMarker.Matches(extraInfo);
}
