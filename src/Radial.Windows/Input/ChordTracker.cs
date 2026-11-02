namespace Radial.Windows.Input;

/// <summary>
/// Máquina de estados del gesto "Ctrl pulsado → botón central → soltar". Es la fuente de verdad de las transiciones:
/// se alimenta únicamente con los eventos de los hooks (nunca consulta el estado físico) y decide si cada evento se
/// consume. No tiene dependencias nativas ni hilos propios y NO es segura entre hilos: quien la use serializa las llamadas.
///
/// Reglas:
/// - Ctrl nunca se consume. El orden es obligatorio: el botón central solo activa si Ctrl ya estaba pulsado.
/// - Un botón central pulsado sin Ctrl es un "pulso foráneo": pasa a la aplicación, y Ctrl pulsado después no activa nada.
/// - El clic que activa y su liberación se consumen siempre, aunque el gesto se cancele entre medias.
/// - Escape se consume solo si cancela un gesto activo (y su liberación y repeticiones).
/// - Los movimientos y la rueda nunca se consumen.
/// </summary>
public sealed class ChordTracker
{
    private bool _left;
    private bool _right;
    private bool _middle;
    private bool _foreignMiddle;
    private bool _middleConsumed;
    private bool _escapeConsumed;
    private ChordPhase _phase;
    private long _activationId;
    private ScreenPoint _lastPoint;

    /// <summary>
    /// Por defecto TODOS los eventos inyectados (<c>SendInput</c>) se ignoran. Con esto activo (solo la autoprueba de
    /// diagnóstico) se aceptan únicamente los que llevan la marca de Radial (<see cref="RawInputEvent.OwnSynthetic"/>);
    /// los inyectados por otras aplicaciones se siguen ignorando.
    /// </summary>
    public bool AcceptOwnSyntheticInput { get; set; }

    public ChordPhase Phase => _phase;

    public long ActivationId => _activationId;

    public bool IsCtrlDown => _left || _right;

    public ChordSnapshot Snapshot => new(_left, _right, _middle, _phase, _foreignMiddle, _activationId);

    public ChordResult Process(in RawInputEvent e)
    {
        if (e.Injected && !(AcceptOwnSyntheticInput && e.OwnSynthetic))
        {
            return ChordResult.Pass;
        }

        return e.Kind switch
        {
            InputEventKind.KeyDown => OnKeyDown(e),
            InputEventKind.KeyUp => OnKeyUp(e),
            InputEventKind.MiddleDown => OnMiddleDown(e),
            InputEventKind.MiddleUp => OnMiddleUp(e),
            InputEventKind.Move => OnMove(e),
            _ => ChordResult.Pass, // Wheel: nunca activa ni se consume
        };
    }

    /// <summary>
    /// El watchdog detectó que el estado físico de Ctrl ya no coincide con el del tracker: se olvida el Ctrl "pulsado"
    /// y se cancela. Solo Ctrl: el botón central cuyo evento consumió el hook NO es visible para GetAsyncKeyState.
    /// </summary>
    public ChordResult CancelBecauseCtrlLost()
    {
        _left = _right = false;
        return Cancel(CancelCause.WatchdogCtrlLost);
    }

    /// <summary>Cancelación externa (parada, bloqueo de sesión, prueba manual). Si hay gesto activo, queda esperando la liberación.</summary>
    public ChordResult Cancel(CancelCause cause)
    {
        if (_phase != ChordPhase.Active)
        {
            return ChordResult.Pass;
        }

        _phase = ChordPhase.AwaitingMiddleRelease;
        return new ChordResult(false, GestureSignal.Cancelled, cause, _lastPoint);
    }

    /// <summary>Olvida todo (al iniciar o detener la captura).</summary>
    public void Reset()
    {
        _left = _right = _middle = _foreignMiddle = _middleConsumed = _escapeConsumed = false;
        _phase = ChordPhase.Idle;
    }

    /// <summary>Inicializa desde el estado físico al arrancar: un botón central ya pulsado es foráneo.</summary>
    public void Seed(PhysicalInputState physical)
    {
        Reset();
        _left = physical.LeftControl;
        _right = physical.RightControl;
        _middle = physical.Middle;
        _foreignMiddle = physical.Middle;
    }

    private ChordResult OnKeyDown(in RawInputEvent e)
    {
        switch (e.Key)
        {
            case InputKey.LeftControl:
                _left = true;
                return ChordResult.Pass;
            case InputKey.RightControl:
                _right = true;
                return ChordResult.Pass;
            case InputKey.Escape:
                if (_phase == ChordPhase.Active)
                {
                    var cancelled = Cancel(CancelCause.Escape);
                    _escapeConsumed = true;
                    return cancelled with { Consume = true };
                }

                // Repeticiones automáticas de la tecla que ya se consumió.
                return _escapeConsumed ? ChordResult.Swallow : ChordResult.Pass;
            default:
                return ChordResult.Pass;
        }
    }

    private ChordResult OnKeyUp(in RawInputEvent e)
    {
        switch (e.Key)
        {
            case InputKey.LeftControl:
                _left = false;
                return AfterCtrlRelease();
            case InputKey.RightControl:
                _right = false;
                return AfterCtrlRelease();
            case InputKey.Escape:
                if (_escapeConsumed)
                {
                    _escapeConsumed = false;
                    return ChordResult.Swallow;
                }

                return ChordResult.Pass;
            default:
                return ChordResult.Pass;
        }
    }

    // Ctrl nunca se consume; si ya no queda ningún Ctrl pulsado, el gesto activo se cancela.
    private ChordResult AfterCtrlRelease() =>
        _phase == ChordPhase.Active && !IsCtrlDown ? Cancel(CancelCause.CtrlReleased) : ChordResult.Pass;

    private ChordResult OnMiddleDown(in RawInputEvent e)
    {
        if (_middle)
        {
            // Pulsación repetida sin liberación: se trata igual que la original.
            return _middleConsumed ? ChordResult.Swallow : ChordResult.Pass;
        }

        _middle = true;
        if (_phase == ChordPhase.Idle && IsCtrlDown)
        {
            _phase = ChordPhase.Active;
            _middleConsumed = true;
            _activationId++;
            _lastPoint = e.Point;
            return new ChordResult(true, GestureSignal.Activated, CancelCause.None, e.Point);
        }

        // Sin Ctrl previo: pulso foráneo. No se intercepta, ni ahora ni retroactivamente.
        _middleConsumed = false;
        _foreignMiddle = true;
        return ChordResult.Pass;
    }

    private ChordResult OnMiddleUp(in RawInputEvent e)
    {
        if (!_middle)
        {
            return ChordResult.Pass; // liberación sin pulsación conocida
        }

        _middle = false;
        switch (_phase)
        {
            case ChordPhase.Active:
                _lastPoint = e.Point;
                return Finish(GestureSignal.Confirmed, CancelCause.None, e.Point, consume: true);
            case ChordPhase.AwaitingMiddleRelease:
                _phase = ChordPhase.Idle;
                _middleConsumed = false;
                _escapeConsumed = false;
                return ChordResult.Swallow;
            default:
                _foreignMiddle = false;
                return ChordResult.Pass;
        }
    }

    private ChordResult OnMove(in RawInputEvent e)
    {
        _lastPoint = e.Point;
        return _phase == ChordPhase.Active
            ? new ChordResult(false, GestureSignal.Moved, CancelCause.None, e.Point)
            : ChordResult.Pass;
    }

    private ChordResult Finish(GestureSignal signal, CancelCause cause, ScreenPoint? point = null, bool consume = false)
    {
        _phase = ChordPhase.Idle;
        _middleConsumed = false;
        _escapeConsumed = false;
        return new ChordResult(consume, signal, cause, point ?? _lastPoint);
    }
}
