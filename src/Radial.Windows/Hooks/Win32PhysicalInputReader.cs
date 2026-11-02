using Radial.Windows.Input;
using Radial.Windows.Native;

namespace Radial.Windows.Hooks;

/// <summary>Estado físico con <c>GetAsyncKeyState</c> y <c>GetCursorPos</c>. Nunca se llama desde un callback de hook.</summary>
internal sealed class Win32PhysicalInputReader : IPhysicalInputReader
{
    public PhysicalInputState ReadKeys() => new(
        IsDown(NativeMethods.VK_LCONTROL),
        IsDown(NativeMethods.VK_RCONTROL),
        IsDown(NativeMethods.VK_MBUTTON));

    public ScreenPoint ReadCursor() =>
        NativeMethods.GetCursorPos(out var point) ? new ScreenPoint(point.X, point.Y) : default;

    private static bool IsDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
