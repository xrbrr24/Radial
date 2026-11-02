using System.Diagnostics;
using System.Runtime.InteropServices;
using Radial.Windows.Input;
using Radial.Windows.Native;

namespace Radial.Windows.Hooks;

/// <summary>WH_MOUSE_LL. Detecta el botón central (pulsación y liberación), el movimiento y la rueda.</summary>
internal sealed class LowLevelMouseHook : LowLevelHookBase
{
    private readonly RawInputSink _sink;

    public LowLevelMouseHook(RawInputSink sink)
        : base(NativeMethods.WH_MOUSE_LL)
    {
        _sink = sink;
    }

    protected override bool Dispatch(IntPtr wParam, IntPtr lParam)
    {
        // MSLLHOOKSTRUCT (x64): pt.x @0, pt.y @4, mouseData @8, flags @12, time @16, dwExtraInfo @24. Se lee sin asignar memoria.
        var x = Marshal.ReadInt32(lParam, 0);
        var y = Marshal.ReadInt32(lParam, 4);
        var flags = Marshal.ReadInt32(lParam, 12);
        var extraInfo = Marshal.ReadInt64(lParam, 24);

        return HookMessages.TryMapMouse((int)wParam, x, y, flags, extraInfo, Stopwatch.GetTimestamp(), out var input) && _sink(in input);
    }
}
