namespace Radial.Windows.Input;

/// <summary>Destino de los eventos que producen los hooks. Se invoca en el hilo de los hooks; devuelve si hay que consumir el evento.</summary>
internal delegate bool RawInputSink(in RawInputEvent e);

internal readonly record struct HookInstallStatus(bool Attempted, bool Installed, int Win32Error);

internal readonly record struct HookHostStartResult(HookInstallStatus Keyboard, HookInstallStatus Mouse)
{
    public bool BothInstalled => Keyboard.Installed && Mouse.Installed;
}

/// <summary>Contadores de un hook. Se leen desde otro hilo para el diagnóstico.</summary>
internal readonly record struct HookCounters(long Calls, long MaxTicks, long SlowCalls, long Errors, string? LastError);

internal readonly record struct HookHostStatistics(HookCounters Keyboard, HookCounters Mouse);

/// <summary>Instala y mantiene los hooks de bajo nivel. La implementación real usa un hilo dedicado; los tests usan una falsa.</summary>
internal interface IHookHost
{
    /// <summary>Instala ambos hooks. Si alguno falla, no queda ninguno instalado.</summary>
    HookHostStartResult Start(RawInputSink sink);

    /// <summary>Quita los hooks y espera a que termine el hilo. Tras volver no se producen más llamadas al sink.</summary>
    void Stop();

    HookHostStatistics GetStatistics();
}

/// <summary>Lectura del estado físico y del cursor. Solo se usa fuera de los callbacks (watchdog, arranque, sonda de salud).</summary>
internal interface IPhysicalInputReader
{
    PhysicalInputState ReadKeys();

    ScreenPoint ReadCursor();
}
