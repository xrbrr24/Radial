using Radial.Windows.Input;

namespace Radial.Windows.Tests.Input;

/// <summary>Host de hooks falso: los eventos los inyecta la prueba llamando a <see cref="Send"/>. No instala nada en Windows.</summary>
internal sealed class FakeHookHost : IHookHost
{
    private RawInputSink? _sink;

    public HookInstallStatus KeyboardResult { get; set; } = new(true, true, 0);

    public HookInstallStatus MouseResult { get; set; } = new(true, true, 0);

    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    public long MouseCalls { get; set; }

    public bool IsRunning => _sink is not null;

    public HookHostStartResult Start(RawInputSink sink)
    {
        StartCalls++;
        var result = new HookHostStartResult(KeyboardResult, MouseResult);
        _sink = result.BothInstalled ? sink : null;
        return result;
    }

    public void Stop()
    {
        StopCalls++;
        _sink = null;
    }

    public HookHostStatistics GetStatistics() => new(default, new HookCounters(MouseCalls, 0, 0, 0, null));

    /// <summary>Entrega un evento al servicio como lo haría un hook. Devuelve si el servicio lo consume.</summary>
    public bool Send(RawInputEvent e)
    {
        var sink = _sink ?? throw new InvalidOperationException("Los hooks falsos no están instalados.");
        if (e.Kind is InputEventKind.Move or InputEventKind.MiddleDown or InputEventKind.MiddleUp)
        {
            MouseCalls++;
        }

        return sink(in e);
    }
}

/// <summary>Plaza única de "hooks activos en el proceso", como el <c>static</c> del host real: solo un host puede ocuparla.</summary>
internal sealed class ExclusiveHookSlot
{
    public bool Held { get; set; }

    /// <summary>Cuántas veces se ocupó la plaza en total (si fuera más de una a la vez habría hooks duplicados).</summary>
    public int TotalAcquisitions { get; set; }
}

/// <summary>Host falso con la misma regla que <c>LowLevelHookHost</c>: un segundo host activo lanza InvalidOperationException.</summary>
internal sealed class ExclusiveFakeHookHost : IHookHost
{
    private readonly ExclusiveHookSlot _slot;
    private bool _owner;

    public ExclusiveFakeHookHost(ExclusiveHookSlot slot)
    {
        _slot = slot;
    }

    public FakeHookHost Inner { get; } = new();

    public HookHostStartResult Start(RawInputSink sink)
    {
        if (_slot.Held)
        {
            throw new InvalidOperationException("Ya hay otra captura global activa en este proceso.");
        }

        _slot.Held = true;
        _slot.TotalAcquisitions++;
        _owner = true;
        return Inner.Start(sink);
    }

    public void Stop()
    {
        Inner.Stop();
        if (_owner)
        {
            _owner = false;
            _slot.Held = false;
        }
    }

    public HookHostStatistics GetStatistics() => Inner.GetStatistics();
}

internal sealed class FakePhysicalReader : IPhysicalInputReader
{
    public PhysicalInputState Keys { get; set; }

    public ScreenPoint Cursor { get; set; }

    public PhysicalInputState ReadKeys() => Keys;

    public ScreenPoint ReadCursor() => Cursor;
}

internal sealed class FakeTimerFactory : ITimerFactory
{
    public List<FakeTimer> Timers { get; } = [];

    public IPeriodicTimer Create(Action callback)
    {
        var timer = new FakeTimer(callback);
        Timers.Add(timer);
        return timer;
    }

    /// <summary>El primer temporizador que crea el servicio es el watchdog.</summary>
    public FakeTimer Watchdog => Timers[0];

    public FakeTimer Health => Timers[1];
}

internal sealed class FakeTimer : IPeriodicTimer
{
    private readonly Action _callback;

    public FakeTimer(Action callback)
    {
        _callback = callback;
    }

    public bool Running { get; private set; }

    public int StartCount { get; private set; }

    public void Start(TimeSpan period)
    {
        Running = true;
        StartCount++;
    }

    public void Stop() => Running = false;

    public void Dispose() => Running = false;

    /// <summary>Simula un disparo del temporizador (sin esperas reales).</summary>
    public void Tick() => _callback();
}

internal sealed class FakeSystemEvents : ISystemStateEvents
{
    public event Action? SessionLocked;

    public event Action? SessionUnlocked;

    public event Action? Suspending;

    public event Action? Resumed;

    public int AttachCalls { get; private set; }

    public int DetachCalls { get; private set; }

    public void Attach() => AttachCalls++;

    public void Detach() => DetachCalls++;

    public void RaiseLocked() => SessionLocked?.Invoke();

    public void RaiseUnlocked() => SessionUnlocked?.Invoke();

    public void RaiseSuspending() => Suspending?.Invoke();

    public void RaiseResumed() => Resumed?.Invoke();
}
