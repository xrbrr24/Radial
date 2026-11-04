using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Radial.Windows.Hooks;

namespace Radial.Windows.Input;

/// <summary>
/// Captura global de teclado y ratón, independiente de la interfaz.
///
/// Hilos:
/// - Hilo de hooks (dedicado, ver LowLevelHookHost): llama a <see cref="OnInput"/> desde los callbacks.
/// - Hilo del pool: watchdog (solo mientras hay un gesto) y sonda de salud (cada pocos segundos mientras corre).
/// - Hilo de aplicación: Start/Stop, lectura de <see cref="Events"/>, <see cref="LatestPointer"/> y diagnóstico.
/// Todo el estado de <see cref="ChordTracker"/> se toca bajo <c>_gate</c>, y los eventos críticos se publican bajo el mismo
/// cerrojo, así que su orden es el de las transiciones.
///
/// Comunicación: los eventos críticos (activación, confirmación, cancelación) van por un canal SIN límite que nunca descarta.
/// Los movimientos se coalescen: solo se guarda la última posición.
///
/// Limitación: Windows puede eliminar un hook sin avisar. Este servicio no puede detectarlo con certeza ni lo recupera;
/// <see cref="InputDiagnostics.MouseActivity"/> solo señala el síntoma observable.
/// </summary>
public sealed class GlobalInputService : IDisposable
{
    private static readonly TimeSpan WatchdogPeriod = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan HealthPeriod = TimeSpan.FromSeconds(2);
    private const int WatchdogStrikes = 2;
    private const int MouseSilenceThresholdPixels = 40;
    private const int MaxMessages = 20;

    private readonly IHookHost _host;
    private readonly IPhysicalInputReader _reader;
    private readonly ISystemStateEvents _systemEvents;
    private readonly IPeriodicTimer _watchdog;
    private readonly IPeriodicTimer _health;
    private readonly ChordTracker _tracker = new();
    private readonly object _gate = new();
    private readonly object _lifecycle = new();
    private readonly Channel<GestureEvent> _channel = Channel.CreateUnbounded<GestureEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentQueue<string> _errors = new();
    private readonly ConcurrentQueue<string> _notices = new();

    private CaptureState _state = CaptureState.Stopped;
    private bool _disposed;
    private HookInstallStatus _keyboardStatus;
    private HookInstallStatus _mouseStatus;

    private bool _active;
    private bool _watchdogRunning;
    private int _badSamples;
    private long _sequence;
    private long _activations;
    private long _confirmations;
    private long _cancellations;
    private CancelCause _lastCancelCause;

    private long _pointerPacked;
    private long _pointerSequence;
    private int _hasPointer;
    private int _lastKind = -1;
    private int _lastKey;
    private int _lastInjected;
    private int _lastOwn;
    private long _lastTimestamp;

    private bool _probeBaselineSet;
    private long _probeLastCount;
    private ScreenPoint _probeLastCursor;
    private MouseActivity _mouseActivity;

    /// <summary>
    /// Servicio real: hooks de bajo nivel en un hilo dedicado y <c>GetAsyncKeyState</c> para el watchdog.
    /// <paramref name="systemEvents"/> aporta bloqueo de sesión y suspensión; si es nulo no se reacciona a ellos.
    /// </summary>
    public GlobalInputService(ISystemStateEvents? systemEvents = null)
        : this(new LowLevelHookHost(), new Win32PhysicalInputReader(), new ThreadingTimerFactory(), systemEvents ?? new NullSystemStateEvents())
    {
    }

    internal GlobalInputService(IHookHost host, IPhysicalInputReader reader, ITimerFactory timers, ISystemStateEvents systemEvents)
    {
        _host = host;
        _reader = reader;
        _systemEvents = systemEvents;
        _watchdog = timers.Create(RunWatchdogCheck);
        _health = timers.Create(RunHealthProbe);

        _systemEvents.SessionLocked += () => OnSystemEvent(CancelCause.SessionLocked, "Sesión bloqueada: se canceló el gesto activo, si lo había.");
        _systemEvents.Suspending += () => OnSystemEvent(CancelCause.Suspending, "El equipo se suspende: se canceló el gesto activo, si lo había.");
        _systemEvents.SessionUnlocked += () => OnResume("Sesión desbloqueada");
        _systemEvents.Resumed += () => OnResume("El equipo se reanudó");
    }

    /// <summary>Eventos críticos, en orden. Un único lector.</summary>
    public ChannelReader<GestureEvent> Events => _channel.Reader;

    public CaptureState State
    {
        get
        {
            lock (_lifecycle)
            {
                return _state;
            }
        }
    }

    /// <summary>Última posición del cursor vista por el hook de ratón (coalescida; no pasa por el canal).</summary>
    public ScreenPoint LatestPointer
    {
        get
        {
            var packed = Volatile.Read(ref _pointerPacked);
            return new ScreenPoint((int)(packed >> 32), (int)(packed & 0xFFFFFFFF));
        }
    }

    /// <summary>Se incrementa con cada movimiento observado; permite saber si hay una posición nueva.</summary>
    public long PointerSequence => Volatile.Read(ref _pointerSequence);

    /// <summary>
    /// SOLO DIAGNÓSTICO. Hace que el servicio trate como reales los eventos que Radial inyectó con su propia marca
    /// (<see cref="Radial.Windows.Diagnostics.GuardedSyntheticInput"/>), para la autoprueba. Los eventos inyectados por
    /// OTRAS aplicaciones se ignoran también con esto activo. En uso normal debe estar a falso: se ignora todo lo inyectado.
    /// </summary>
    public bool AcceptOwnSyntheticInput
    {
        get
        {
            lock (_gate)
            {
                return _tracker.AcceptOwnSyntheticInput;
            }
        }
        set
        {
            lock (_gate)
            {
                _tracker.AcceptOwnSyntheticInput = value;
            }
        }
    }

    /// <summary>Instala los hooks. Lanza <see cref="InvalidOperationException"/> si ya está en marcha; un fallo de instalación se devuelve en el resultado.</summary>
    public StartResult Start()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state is CaptureState.Running or CaptureState.Starting or CaptureState.Stopping)
            {
                throw new InvalidOperationException("La captura ya está en marcha.");
            }

            _state = CaptureState.Starting;
            try
            {
                lock (_gate)
                {
                    _tracker.Seed(_reader.ReadKeys());
                    _active = false;
                    _badSamples = 0;
                    _probeBaselineSet = false;
                    _mouseActivity = MouseActivity.NotMonitored;
                }

                var result = _host.Start(OnInput);
                _keyboardStatus = result.Keyboard;
                _mouseStatus = result.Mouse;

                if (!result.BothInstalled)
                {
                    _state = CaptureState.Faulted;
                    SafeStopHost();
                    var message = $"No se pudieron instalar los hooks (teclado: {Describe(result.Keyboard)}; ratón: {Describe(result.Mouse)}).";
                    AddMessage(_errors, message);
                    return new StartResult(false, message);
                }

                _systemEvents.Attach();
                _health.Start(HealthPeriod);
                _state = CaptureState.Running;
                return new StartResult(true, "Captura en marcha.");
            }
            catch (Exception exception)
            {
                // Incluye la InvalidOperationException del host cuando ya hay otra captura activa en el proceso: la propia
                // ("ya está en marcha") se lanza antes de este bloque y no pasa por aquí. Nunca se deja el estado en Starting.
                _state = CaptureState.Faulted;
                SafeStopHost();
                AddMessage(_errors, "Error al iniciar la captura: " + exception.Message);
                return new StartResult(false, exception.Message);
            }
        }
    }

    /// <summary>Quita los hooks y cancela cualquier gesto activo. Se puede volver a llamar a <see cref="Start"/>.</summary>
    public void Stop()
    {
        lock (_lifecycle)
        {
            if (_state is CaptureState.Stopped)
            {
                return;
            }

            _state = CaptureState.Stopping;
            _systemEvents.Detach();
            _health.Stop();

            var stopped = true;
            try
            {
                _host.Stop();
            }
            catch (Exception exception)
            {
                stopped = false;
                AddMessage(_errors, "Error al detener los hooks: " + exception.Message);
            }

            lock (_gate)
            {
                Publish(_tracker.Cancel(CancelCause.ServiceStopped), Stopwatch.GetTimestamp());
                _tracker.Reset();
                _active = false;
                SyncWatchdog();
                _mouseActivity = MouseActivity.NotMonitored;
            }

            _state = stopped ? CaptureState.Stopped : CaptureState.Faulted;
        }
    }

    /// <summary>Cancela el gesto activo (prueba manual, bloqueo de sesión...). Devuelve si había uno.</summary>
    public bool RequestCancel(CancelCause cause = CancelCause.Manual)
    {
        lock (_gate)
        {
            var result = _tracker.Cancel(cause);
            Publish(result, Stopwatch.GetTimestamp());
            return result.Signal == GestureSignal.Cancelled;
        }
    }

    public InputDiagnostics GetDiagnostics()
    {
        ChordSnapshot chord;
        lock (_gate)
        {
            chord = _tracker.Snapshot;
        }

        var stats = _host.GetStatistics();
        var lastKind = Volatile.Read(ref _lastKind);
        var lastTimestamp = Interlocked.Read(ref _lastTimestamp);
        CaptureState state;
        lock (_lifecycle)
        {
            state = _state;
        }

        var running = state == CaptureState.Running;
        var degraded = running && _mouseActivity == MouseActivity.SilentWhileCursorMoves;

        return new InputDiagnostics(
            degraded ? CaptureState.Faulted : state,
            new HookStatusInfo(_keyboardStatus.Attempted, _keyboardStatus.Installed && state is CaptureState.Running or CaptureState.Stopping, _keyboardStatus.Win32Error),
            new HookStatusInfo(_mouseStatus.Attempted, _mouseStatus.Installed && state is CaptureState.Running or CaptureState.Stopping, _mouseStatus.Win32Error),
            chord.LeftControl,
            chord.RightControl,
            chord.Middle,
            chord.Phase,
            chord.ForeignMiddle,
            lastKind >= 0,
            lastKind >= 0 ? (InputEventKind)lastKind : default,
            (InputKey)Volatile.Read(ref _lastKey),
            Volatile.Read(ref _lastInjected) != 0,
            Volatile.Read(ref _lastOwn) != 0,
            lastKind >= 0 ? Stopwatch.GetElapsedTime(lastTimestamp) : TimeSpan.Zero,
            Volatile.Read(ref _hasPointer) != 0,
            LatestPointer,
            stats.Keyboard.Calls,
            stats.Mouse.Calls,
            Interlocked.Read(ref _activations),
            Interlocked.Read(ref _confirmations),
            Interlocked.Read(ref _cancellations),
            _lastCancelCause,
            TicksToMilliseconds(stats.Keyboard.MaxTicks),
            TicksToMilliseconds(stats.Mouse.MaxTicks),
            stats.Keyboard.SlowCalls + stats.Mouse.SlowCalls,
            running ? _mouseActivity : MouseActivity.NotMonitored,
            CollectErrors(stats),
            _notices.ToArray());
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed)
            {
                return;
            }
        }

        Stop();
        lock (_lifecycle)
        {
            _disposed = true;
        }

        _watchdog.Dispose();
        _health.Dispose();
        _channel.Writer.TryComplete();
    }

    /// <summary>Se ejecuta en el hilo de los hooks, dentro del callback. Debe ser mínimo.</summary>
    private bool OnInput(in RawInputEvent e)
    {
        Volatile.Write(ref _lastKey, (int)e.Key);
        Volatile.Write(ref _lastInjected, e.Injected ? 1 : 0);
        Volatile.Write(ref _lastOwn, e.OwnSynthetic ? 1 : 0);
        Interlocked.Exchange(ref _lastTimestamp, e.Timestamp);
        Volatile.Write(ref _lastKind, (int)e.Kind);

        if (e.Kind == InputEventKind.Move)
        {
            StorePointer(e.Point);
        }

        // Sin gesto activo, ni los movimientos ni la rueda necesitan el cerrojo ni el tracker.
        if (!Volatile.Read(ref _active) && (e.Kind == InputEventKind.Move || e.Kind == InputEventKind.Wheel))
        {
            return false;
        }

        try
        {
            lock (_gate)
            {
                var result = _tracker.Process(in e);
                Publish(result, e.Timestamp);
                return result.Consume;
            }
        }
        catch (Exception exception)
        {
            AddMessage(_errors, "Fallo procesando un evento: " + exception.Message);
            RecoverFromFault();
            return false;
        }
    }

    // Debe llamarse con _gate tomado.
    private void Publish(in ChordResult result, long timestamp)
    {
        switch (result.Signal)
        {
            case GestureSignal.Activated:
                _active = true;
                Interlocked.Increment(ref _activations);
                Write(GestureEventKind.Activated, result, timestamp);
                break;
            case GestureSignal.Moved:
                StorePointer(result.Point);
                break;
            case GestureSignal.Confirmed:
                _active = false;
                Interlocked.Increment(ref _confirmations);
                Write(GestureEventKind.Confirmed, result, timestamp);
                break;
            case GestureSignal.Cancelled:
                _active = false;
                Interlocked.Increment(ref _cancellations);
                _lastCancelCause = result.Cause;
                Write(GestureEventKind.Cancelled, result, timestamp);
                break;
        }

        SyncWatchdog();
    }

    // Con _gate tomado (Publish): el estado de la aceptación sintética es el mismo con el que el tracker procesó el evento.
    private void Write(GestureEventKind kind, in ChordResult result, long timestamp) =>
        _channel.Writer.TryWrite(new GestureEvent(++_sequence, timestamp, kind, result.Point, result.Cause, _tracker.AcceptOwnSyntheticInput));

    // Debe llamarse con _gate tomado. El watchdog solo corre mientras el gesto está ACTIVO: sin gesto no hay sondeo.
    private void SyncWatchdog()
    {
        var needed = _tracker.Phase == ChordPhase.Active;
        if (needed == _watchdogRunning)
        {
            return;
        }

        _watchdogRunning = needed;
        _badSamples = 0;
        if (needed)
        {
            _watchdog.Start(WatchdogPeriod);
        }
        else
        {
            _watchdog.Stop();
        }
    }

    /// <summary>
    /// Supervisión independiente de los hooks, solo mientras el gesto está activo. Comprueba dos cosas fuera del callback:
    /// (1) que Ctrl sigue pulsado de verdad (Ctrl nunca se consume, así que <c>GetAsyncKeyState</c> es fiable para él), y
    /// (2) que el hook de ratón sigue entregando el movimiento: si el cursor real se aleja de la última posición del hook,
    /// el hook dejó de funcionar. NO comprueba el botón central: un evento consumido por un hook no actualiza el estado
    /// asíncrono de Windows, así que el botón que activó el gesto "parece" suelto aunque esté pulsado.
    /// Necesita <see cref="WatchdogStrikes"/> muestras malas seguidas y solo cancela, nunca confirma. No puede detectar
    /// que Windows quitó el hook de teclado ni recuperar un hook eliminado.
    /// </summary>
    internal void RunWatchdogCheck()
    {
        long activationId;
        lock (_gate)
        {
            if (_tracker.Phase != ChordPhase.Active)
            {
                return;
            }

            activationId = _tracker.ActivationId;
        }

        var physical = _reader.ReadKeys(); // fuera del cerrojo
        var cursor = _reader.ReadCursor();

        lock (_gate)
        {
            if (_tracker.Phase != ChordPhase.Active || _tracker.ActivationId != activationId)
            {
                return; // la muestra es de un gesto que ya terminó
            }

            var pointer = LatestPointer;
            var ctrlLost = !physical.LeftControl && !physical.RightControl;
            var mouseSilent = Math.Max(Math.Abs(cursor.X - pointer.X), Math.Abs(cursor.Y - pointer.Y)) > MouseSilenceThresholdPixels;
            if (!ctrlLost && !mouseSilent)
            {
                _badSamples = 0;
                return;
            }

            if (++_badSamples < WatchdogStrikes)
            {
                return;
            }

            _badSamples = 0;
            var result = ctrlLost ? _tracker.CancelBecauseCtrlLost() : _tracker.Cancel(CancelCause.WatchdogMouseHookSilent);
            Publish(result, Stopwatch.GetTimestamp());
        }
    }

    /// <summary>Estado físico actual (Ctrl y botón central). Para diagnóstico; nunca se usa dentro de un callback.</summary>
    public PhysicalInputState ReadPhysicalState() => _reader.ReadKeys();

    /// <summary>Sonda de salud del hook de ratón: ¿se mueve el cursor sin que el hook reciba nada?</summary>
    internal void RunHealthProbe()
    {
        var cursor = _reader.ReadCursor();
        var calls = _host.GetStatistics().Mouse.Calls;
        _mouseActivity = _probeBaselineSet
            ? HookHealthEvaluator.Evaluate(_probeLastCount, calls, _probeLastCursor, cursor)
            : MouseActivity.Quiet;

        if (_mouseActivity == MouseActivity.SilentWhileCursorMoves)
        {
            AddMessage(_errors, "El cursor se mueve pero el hook de ratón no recibe eventos: puede haber sido eliminado por Windows. Reinicia la captura.");
        }

        _probeBaselineSet = true;
        _probeLastCount = calls;
        _probeLastCursor = cursor;
    }

    private void OnSystemEvent(CancelCause cause, string notice)
    {
        RequestCancel(cause);
        AddMessage(_notices, notice);
    }

    private void OnResume(string what)
    {
        _probeBaselineSet = false; // la sonda vuelve a tomar referencia
        AddMessage(_notices, what + ". La captura no se reinstala sola: si el gesto no responde, detén y vuelve a iniciar la captura.");
    }

    private void RecoverFromFault()
    {
        try
        {
            lock (_gate)
            {
                Publish(_tracker.Cancel(CancelCause.HookError), Stopwatch.GetTimestamp());
            }
        }
        catch (Exception)
        {
            // Sin más recursos: el error original ya quedó anotado.
        }
    }

    private void StorePointer(ScreenPoint point)
    {
        Volatile.Write(ref _pointerPacked, ((long)point.X << 32) | (uint)point.Y);
        Interlocked.Increment(ref _pointerSequence);
        Volatile.Write(ref _hasPointer, 1);
    }

    private void SafeStopHost()
    {
        try
        {
            _host.Stop();
        }
        catch (Exception exception)
        {
            AddMessage(_errors, "Error al liberar los hooks: " + exception.Message);
        }
    }

    private IReadOnlyList<string> CollectErrors(HookHostStatistics stats)
    {
        var list = _errors.ToList();
        if (stats.Keyboard.LastError is { } keyboardError)
        {
            list.Add($"Hook de teclado ({stats.Keyboard.Errors}): {keyboardError}");
        }

        if (stats.Mouse.LastError is { } mouseError)
        {
            list.Add($"Hook de ratón ({stats.Mouse.Errors}): {mouseError}");
        }

        return list;
    }

    private static void AddMessage(ConcurrentQueue<string> queue, string message)
    {
        queue.Enqueue($"{DateTime.Now:HH:mm:ss} {message}");
        while (queue.Count > MaxMessages && queue.TryDequeue(out _))
        {
        }
    }

    private static string Describe(HookInstallStatus status) =>
        status.Installed ? "instalado" : $"error {status.Win32Error}";

    private static double TicksToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}

/// <summary>Evaluación pura de la sonda de salud, para poder comprobarla sin hooks.</summary>
internal static class HookHealthEvaluator
{
    public static MouseActivity Evaluate(long previousCalls, long currentCalls, ScreenPoint previousCursor, ScreenPoint currentCursor)
    {
        var cursorMoved = previousCursor != currentCursor;
        if (cursorMoved && currentCalls == previousCalls)
        {
            return MouseActivity.SilentWhileCursorMoves;
        }

        return currentCalls > previousCalls ? MouseActivity.Observed : MouseActivity.Quiet;
    }
}
