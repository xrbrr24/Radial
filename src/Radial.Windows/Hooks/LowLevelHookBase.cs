using System.Diagnostics;
using System.Runtime.InteropServices;
using Radial.Windows.Input;
using Radial.Windows.Native;

namespace Radial.Windows.Hooks;

/// <summary>
/// Hook de bajo nivel. Debe instalarse y quitarse en el hilo con bucle de mensajes que lo mantiene (LowLevelHookHost).
/// El delegado del callback es un campo: mientras el hook exista, el GC no puede recogerlo.
/// El callback es mínimo: traduce, entrega al sink, mide y devuelve. Sin disco, sin interfaz, sin esperas.
/// </summary>
internal abstract class LowLevelHookBase
{
    // Un callback que tarda más de esto se cuenta como "lento" en el diagnóstico (Windows elimina el hook mucho más tarde).
    private static readonly long SlowThresholdTicks = Stopwatch.Frequency / 20; // 50 ms

    private readonly NativeMethods.HookProc _proc; // referencia viva mientras exista el hook
    private readonly int _hookId;
    private IntPtr _handle;
    private long _calls;
    private long _maxTicks;
    private long _slowCalls;
    private long _errors;
    private string? _lastError;

    protected LowLevelHookBase(int hookId)
    {
        _hookId = hookId;
        _proc = Callback;
    }

    public bool IsInstalled => _handle != IntPtr.Zero;

    public HookCounters Counters => new(
        Interlocked.Read(ref _calls),
        Interlocked.Read(ref _maxTicks),
        Interlocked.Read(ref _slowCalls),
        Interlocked.Read(ref _errors),
        Volatile.Read(ref _lastError));

    public HookInstallStatus Install()
    {
        if (_handle != IntPtr.Zero)
        {
            throw new InvalidOperationException("El hook ya está instalado.");
        }

        _handle = NativeMethods.SetWindowsHookEx(_hookId, _proc, NativeMethods.GetModuleHandle(null), 0);
        return _handle == IntPtr.Zero
            ? new HookInstallStatus(true, false, Marshal.GetLastWin32Error())
            : new HookInstallStatus(true, true, 0);
    }

    public void Uninstall()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_handle);
        _handle = IntPtr.Zero;
    }

    /// <summary>Procesa un evento. Devuelve verdadero si hay que consumirlo.</summary>
    protected abstract bool Dispatch(IntPtr wParam, IntPtr lParam);

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != NativeMethods.HC_ACTION)
        {
            return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        var started = Stopwatch.GetTimestamp();
        var consume = false;
        try
        {
            consume = Dispatch(wParam, lParam);
        }
        catch (Exception exception)
        {
            // Se anota para el diagnóstico; el evento pasa a la aplicación, que es el estado menos peligroso.
            Interlocked.Increment(ref _errors);
            Volatile.Write(ref _lastError, exception.GetType().Name + ": " + exception.Message);
        }

        RecordDuration(Stopwatch.GetTimestamp() - started);
        return consume ? (IntPtr)1 : NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    private void RecordDuration(long ticks)
    {
        Interlocked.Increment(ref _calls);
        if (ticks >= SlowThresholdTicks)
        {
            Interlocked.Increment(ref _slowCalls);
        }

        long current;
        while (ticks > (current = Interlocked.Read(ref _maxTicks)))
        {
            if (Interlocked.CompareExchange(ref _maxTicks, ticks, current) == current)
            {
                break;
            }
        }
    }
}
