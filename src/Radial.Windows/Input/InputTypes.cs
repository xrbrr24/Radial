namespace Radial.Windows.Input;

/// <summary>Posición en píxeles físicos de pantalla (coordenadas del hook; ver "por validar" en el documento técnico).</summary>
public readonly record struct ScreenPoint(int X, int Y);

public enum InputEventKind
{
    KeyDown,
    KeyUp,
    MiddleDown,
    MiddleUp,
    Move,
    Wheel,
}

/// <summary>Solo las teclas que Radial necesita distinguir. El resto no entra en la lógica.</summary>
public enum InputKey
{
    None,
    LeftControl,
    RightControl,
    Escape,
}

/// <summary>
/// Evento de entrada ya normalizado, sin tipos nativos. <see cref="Timestamp"/> es monotónico (<c>Stopwatch</c>).
/// <see cref="Injected"/>: Windows lo marcó como inyectado (de cualquier origen). <see cref="OwnSynthetic"/>: además lleva la
/// marca de Radial (<c>SyntheticMarker</c>), es decir, lo inyectó la propia autoprueba; implica <see cref="Injected"/>.
/// </summary>
public readonly record struct RawInputEvent(InputEventKind Kind, InputKey Key, ScreenPoint Point, bool Injected, long Timestamp, bool OwnSynthetic = false)
{
    public static RawInputEvent KeyDown(InputKey key, bool injected = false, bool ownSynthetic = false) =>
        new(InputEventKind.KeyDown, key, default, injected || ownSynthetic, 0, ownSynthetic);

    public static RawInputEvent KeyUp(InputKey key, bool injected = false, bool ownSynthetic = false) =>
        new(InputEventKind.KeyUp, key, default, injected || ownSynthetic, 0, ownSynthetic);

    public static RawInputEvent MiddleDown(int x = 0, int y = 0, bool injected = false, bool ownSynthetic = false) =>
        new(InputEventKind.MiddleDown, InputKey.None, new ScreenPoint(x, y), injected || ownSynthetic, 0, ownSynthetic);

    public static RawInputEvent MiddleUp(int x = 0, int y = 0, bool injected = false, bool ownSynthetic = false) =>
        new(InputEventKind.MiddleUp, InputKey.None, new ScreenPoint(x, y), injected || ownSynthetic, 0, ownSynthetic);

    public static RawInputEvent Move(int x, int y, bool injected = false, bool ownSynthetic = false) =>
        new(InputEventKind.Move, InputKey.None, new ScreenPoint(x, y), injected || ownSynthetic, 0, ownSynthetic);

    public static RawInputEvent Wheel(int x = 0, int y = 0, bool injected = false, bool ownSynthetic = false) =>
        new(InputEventKind.Wheel, InputKey.None, new ScreenPoint(x, y), injected || ownSynthetic, 0, ownSynthetic);
}

/// <summary>Estado físico leído fuera de los callbacks (<c>GetAsyncKeyState</c>). Solo lo usa el watchdog.</summary>
public readonly record struct PhysicalInputState(bool LeftControl, bool RightControl, bool Middle);

public enum CancelCause
{
    None,
    Escape,
    CtrlReleased,
    WatchdogCtrlLost,

    /// <summary>El cursor se mueve de verdad pero el hook de ratón dejó de entregar movimientos durante el gesto.</summary>
    WatchdogMouseHookSilent,
    Manual,
    ServiceStopped,
    SessionLocked,
    Suspending,
    HookError,
}

public enum GestureSignal
{
    None,
    Activated,
    Moved,
    Confirmed,
    Cancelled,
}

public enum ChordPhase
{
    Idle,

    /// <summary>El gesto está en curso: Ctrl estaba pulsado y el clic central se consumió.</summary>
    Active,

    /// <summary>El gesto se canceló con el botón central aún pulsado: hay que consumir su liberación.</summary>
    AwaitingMiddleRelease,
}

/// <param name="Consume">Verdadero si el hook debe tragarse este evento.</param>
/// <param name="Signal">Señal de gesto que el servicio debe publicar (o <see cref="GestureSignal.None"/>).</param>
public readonly record struct ChordResult(bool Consume, GestureSignal Signal, CancelCause Cause, ScreenPoint Point)
{
    public static ChordResult Pass => default;

    public static ChordResult Swallow => new(true, GestureSignal.None, CancelCause.None, default);
}

public readonly record struct ChordSnapshot(
    bool LeftControl,
    bool RightControl,
    bool Middle,
    ChordPhase Phase,
    bool ForeignMiddle,
    long ActivationId);
