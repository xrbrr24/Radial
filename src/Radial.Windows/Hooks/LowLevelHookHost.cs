using Radial.Windows.Input;
using Radial.Windows.Native;

namespace Radial.Windows.Hooks;

/// <summary>
/// Mantiene ambos hooks en un hilo DEDICADO con su propio bucle de mensajes (los hooks de bajo nivel solo reciben
/// eventos si el hilo que los instaló procesa mensajes). Nunca se instalan desde el hilo de interfaz.
/// Solo puede haber una instancia activa por proceso.
/// </summary>
internal sealed class LowLevelHookHost : IHookHost
{
    private static int s_active;

    private readonly object _gate = new();
    private Thread? _thread;
    private uint _threadId;
    private LowLevelKeyboardHook? _keyboard;
    private LowLevelMouseHook? _mouse;
    private TaskCompletionSource<HookHostStartResult>? _ready;

    public HookHostStartResult Start(RawInputSink sink)
    {
        lock (_gate)
        {
            if (_thread is not null)
            {
                throw new InvalidOperationException("Este host de hooks ya está en marcha.");
            }

            if (Interlocked.CompareExchange(ref s_active, 1, 0) != 0)
            {
                throw new InvalidOperationException("Ya hay otra captura global activa en este proceso.");
            }

            _ready = new TaskCompletionSource<HookHostStartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(() => Run(sink, _ready)) { IsBackground = true, Name = "Radial.GlobalInput.Hooks" };
            _thread.Start();
        }

        if (!_ready.Task.Wait(TimeSpan.FromSeconds(5)))
        {
            var timeout = new HookInstallStatus(true, false, -1);
            return new HookHostStartResult(timeout, timeout);
        }

        return _ready.Task.Result;
    }

    public void Stop()
    {
        Thread? thread;
        uint threadId;
        lock (_gate)
        {
            thread = _thread;
            threadId = _threadId;
        }

        if (thread is null)
        {
            return;
        }

        if (thread.IsAlive && threadId != 0)
        {
            NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }

        if (thread.Join(TimeSpan.FromSeconds(3)))
        {
            lock (_gate)
            {
                _thread = null;
                _threadId = 0;
            }
        }
        else
        {
            throw new TimeoutException("El hilo de los hooks no terminó en 3 s; los hooks podrían seguir instalados.");
        }
    }

    public HookHostStatistics GetStatistics() =>
        new(_keyboard?.Counters ?? default, _mouse?.Counters ?? default);

    private void Run(RawInputSink sink, TaskCompletionSource<HookHostStartResult> ready)
    {
        LowLevelKeyboardHook? keyboard = null;
        LowLevelMouseHook? mouse = null;
        try
        {
            // Fuerza la creación de la cola de mensajes del hilo antes de publicar su identificador.
            NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);
            _threadId = NativeMethods.GetCurrentThreadId();

            keyboard = new LowLevelKeyboardHook(sink);
            mouse = new LowLevelMouseHook(sink);
            var keyboardStatus = keyboard.Install();
            var mouseStatus = mouse.Install();
            _keyboard = keyboard;
            _mouse = mouse;

            ready.TrySetResult(new HookHostStartResult(keyboardStatus, mouseStatus));

            if (keyboardStatus.Installed && mouseStatus.Installed)
            {
                while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
                {
                    // No hay ventanas ni mensajes que despachar: el bucle existe para que Windows pueda llamar a los hooks.
                }
            }
        }
        catch (Exception exception)
        {
            var failed = new HookInstallStatus(true, false, exception.HResult);
            ready.TrySetResult(new HookHostStartResult(failed, failed));
        }
        finally
        {
            // Los hooks se quitan en el mismo hilo que los instaló, también cuando solo se instaló uno.
            keyboard?.Uninstall();
            mouse?.Uninstall();
            Interlocked.Exchange(ref s_active, 0);
        }
    }
}
