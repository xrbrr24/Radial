using System.Diagnostics;
using System.Runtime.InteropServices;
using Radial.Windows.Input;
using Radial.Windows.Native;

namespace Radial.Windows.Hooks;

/// <summary>WH_KEYBOARD_LL. Solo traduce Ctrl izquierdo/derecho y Escape; el resto de teclas pasa sin tocar la lógica.</summary>
internal sealed class LowLevelKeyboardHook : LowLevelHookBase
{
    private readonly RawInputSink _sink;

    public LowLevelKeyboardHook(RawInputSink sink)
        : base(NativeMethods.WH_KEYBOARD_LL)
    {
        _sink = sink;
    }

    protected override bool Dispatch(IntPtr wParam, IntPtr lParam)
    {
        // KBDLLHOOKSTRUCT (x64): vkCode @0, scanCode @4, flags @8, time @12, dwExtraInfo @16. Se lee sin asignar memoria.
        var vkCode = Marshal.ReadInt32(lParam, 0);
        var flags = Marshal.ReadInt32(lParam, 8);
        var extraInfo = Marshal.ReadInt64(lParam, 16);

        return HookMessages.TryMapKeyboard((int)wParam, vkCode, flags, extraInfo, Stopwatch.GetTimestamp(), out var input) && _sink(in input);
    }
}
