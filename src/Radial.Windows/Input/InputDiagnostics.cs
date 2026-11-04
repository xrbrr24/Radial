namespace Radial.Windows.Input;

public enum CaptureState
{
    Stopped,
    Starting,
    Running,

    /// <summary>La captura no se pudo instalar o dejó de ser fiable. Los datos del diagnóstico describen el motivo.</summary>
    Faulted,
    Stopping,
}

/// <summary>
/// Lo único que se puede saber sobre la actividad del hook de ratón. Windows puede quitar un hook sin avisar;
/// la sonda solo detecta el síntoma "el cursor se mueve y el hook no recibe eventos".
/// </summary>
public enum MouseActivity
{
    NotMonitored,
    Quiet,
    Observed,
    SilentWhileCursorMoves,
}

public enum GestureEventKind
{
    Activated,
    Confirmed,
    Cancelled,
}

/// <summary>
/// Evento crítico del gesto. Nunca se descarta ni se reordena. <see cref="Sequence"/> es creciente;
/// <see cref="Timestamp"/> es el <c>Stopwatch</c> del evento de entrada que lo originó. Los movimientos NO viajan aquí:
/// se leen con <see cref="GlobalInputService.LatestPointer"/>.
/// <see cref="SyntheticWindow"/>: el servicio publicó el evento mientras aceptaba la entrada sintética propia
/// (<see cref="GlobalInputService.AcceptOwnSyntheticInput"/>, es decir, durante la autoprueba). Se decide al publicar, dentro del
/// mismo cerrojo que el estado del gesto, y viaja con el evento: aunque se lea mucho después, cuando la autoprueba ya terminó,
/// quien lo consuma sigue sabiendo que no puede actuar sobre él. Los eventos que el servicio publica con la aceptación apagada
/// (todo uso normal) nunca la llevan.
/// </summary>
public readonly record struct GestureEvent(long Sequence, long Timestamp, GestureEventKind Kind, ScreenPoint Point, CancelCause Cause, bool SyntheticWindow = false);

public sealed record HookStatusInfo(bool Attempted, bool Installed, int Win32Error);

public sealed record StartResult(bool Success, string Message);

public sealed record InputDiagnostics(
    CaptureState State,
    HookStatusInfo KeyboardHook,
    HookStatusInfo MouseHook,
    bool LeftControl,
    bool RightControl,
    bool MiddleButton,
    ChordPhase Phase,
    bool ForeignMiddle,
    bool HasLastEvent,
    InputEventKind LastEventKind,
    InputKey LastEventKey,
    bool LastEventInjected,
    bool LastEventOwnSynthetic,
    TimeSpan LastEventAge,
    bool HasPointer,
    ScreenPoint Pointer,
    long KeyboardCallbacks,
    long MouseCallbacks,
    long Activations,
    long Confirmations,
    long Cancellations,
    CancelCause LastCancelCause,
    double MaxKeyboardCallbackMs,
    double MaxMouseCallbackMs,
    long SlowCallbacks,
    MouseActivity MouseActivity,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Notices);
