using System.Runtime.InteropServices;
using Radial.Windows.Input;
using Radial.Windows.Native;

namespace Radial.Windows.Diagnostics;

/// <summary>
/// SOLO DIAGNÓSTICO. Inyecta entrada con <c>SendInput</c> para la autoprueba de la captura global. Es interno a propósito:
/// la autoprueba debe usar <see cref="GuardedSyntheticInput"/>, que comprueba el destino antes de cada envío y recuerda qué
/// ha pulsado. Cada evento lleva la marca de Radial en <c>dwExtraInfo</c> (<see cref="SyntheticMarker"/>) además del flag de
/// inyección de Windows: en uso normal Radial ignora todo lo inyectado, y durante la autoprueba solo acepta lo marcado.
/// Es entrada sintética: valida la tubería real de hooks, pero NO equivale a una prueba con el ratón y el teclado físicos.
/// No es el servicio de envío de teclas de la fase 4.
/// </summary>
internal static class SyntheticInput
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint MouseMove = 0x0001;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseAbsolute = 0x8000;

    private const uint KeyEventExtended = 0x0001;
    private const uint KeyEventUp = 0x0002;

    private const ushort VkEscape = 0x1B;
    private const ushort VkLeftControl = 0xA2;
    private const ushort VkRightControl = 0xA3;

    public static void KeyDown(InputKey key) => SendKey(key, up: false);

    public static void KeyUp(InputKey key) => SendKey(key, up: true);

    public static void MiddleDown() => SendMouse(MouseMiddleDown, 0, 0, 0);

    public static void MiddleUp() => SendMouse(MouseMiddleUp, 0, 0, 0);

    public static void Wheel(int delta = 120) => SendMouse(MouseWheel, 0, 0, (uint)delta);

    /// <summary>Mueve el cursor a una posición de pantalla en píxeles físicos, generando un evento de movimiento real.</summary>
    public static void MoveTo(int x, int y)
    {
        const int SmXVirtualScreen = 76, SmYVirtualScreen = 77, SmCxVirtualScreen = 78, SmCyVirtualScreen = 79;
        var left = NativeMethods.GetSystemMetrics(SmXVirtualScreen);
        var top = NativeMethods.GetSystemMetrics(SmYVirtualScreen);
        var width = Math.Max(NativeMethods.GetSystemMetrics(SmCxVirtualScreen), 2);
        var height = Math.Max(NativeMethods.GetSystemMetrics(SmCyVirtualScreen), 2);

        // Coordenadas absolutas normalizadas a 0..65535 sobre el escritorio virtual.
        var normalizedX = (int)Math.Round((x - left) * 65535.0 / (width - 1));
        var normalizedY = (int)Math.Round((y - top) * 65535.0 / (height - 1));
        SendMouse(MouseMove | MouseAbsolute | MouseVirtualDesk, normalizedX, normalizedY, 0);
    }

    /// <summary>Proceso propietario de la ventana que hay bajo un punto de pantalla (0 si no hay ninguna).</summary>
    public static int GetProcessIdAt(ScreenPoint point)
    {
        var window = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = point.X, Y = point.Y });
        return ProcessIdOf(window);
    }

    /// <summary>Proceso propietario de la ventana en primer plano, la que recibe el teclado (0 si no hay ninguna).</summary>
    public static int GetForegroundProcessId() => ProcessIdOf(NativeMethods.GetForegroundWindow());

    public static ScreenPoint GetCursor() =>
        NativeMethods.GetCursorPos(out var point) ? new ScreenPoint(point.X, point.Y) : default;

    private static int ProcessIdOf(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return 0;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return (int)processId;
    }

    private static void SendKey(InputKey key, bool up)
    {
        var (vk, extended) = key switch
        {
            InputKey.LeftControl => (VkLeftControl, false),
            InputKey.RightControl => (VkRightControl, true),
            InputKey.Escape => (VkEscape, false),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Tecla no soportada por la autoprueba."),
        };

        var input = new NativeMethods.INPUT { type = InputKeyboard };
        input.ki = new NativeMethods.KEYBDINPUT
        {
            wVk = vk,
            dwFlags = (up ? KeyEventUp : 0) | (extended ? KeyEventExtended : 0),
            dwExtraInfo = (IntPtr)SyntheticMarker.Value,
        };
        Send(input);
    }

    private static void SendMouse(uint flags, int dx, int dy, uint data)
    {
        var input = new NativeMethods.INPUT { type = InputMouse };
        input.mi = new NativeMethods.MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags, dwExtraInfo = (IntPtr)SyntheticMarker.Value };
        Send(input);
    }

    private static void Send(NativeMethods.INPUT input)
    {
        var sent = NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != 1)
        {
            throw new InvalidOperationException($"SendInput no inyectó el evento (error {Marshal.GetLastWin32Error()}).");
        }
    }
}
