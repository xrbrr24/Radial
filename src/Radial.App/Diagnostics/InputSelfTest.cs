using System.Diagnostics;
using Radial.Windows.Diagnostics;
using Radial.Windows.Input;

namespace Radial.App.Diagnostics;

internal sealed record SelfTestStep(string Name, bool Passed, string Detail);

/// <summary>
/// Autoprueba de la captura global con entrada SINTÉTICA (SendInput) sobre los hooks REALES.
/// Valida que la tubería real funciona (hooks, consumo, orden, watchdog real) sin necesidad de pulsar nada,
/// pero NO sustituye a la prueba manual con el teclado y el ratón físicos.
///
/// Los eventos que inyecta llevan el flag de inyección de Windows Y la marca propia de Radial; solo esos se aceptan
/// (<see cref="GlobalInputService.AcceptOwnSyntheticInput"/>), no los inyectados por otras aplicaciones.
///
/// Seguridad: toda inyección pasa por <see cref="GuardedSyntheticInput"/>, que antes de CADA envío comprueba que el cursor
/// sigue donde se dejó y que la ventana bajo el cursor y la ventana en primer plano son de este proceso. Si algo falla, la
/// prueba se aborta. Reduce el riesgo; no es atómico (ver GuardedSyntheticInput).
///
/// Cancelación y limpieza: <see cref="RunAsync"/> acepta un token. Sea cual sea la salida (fin normal, comprobación fallida,
/// cancelación o excepción) el bloque final libera de forma SÍNCRONA solo lo que la propia prueba pulsó, sin depender de que el
/// dispatcher siga vivo.
///
/// Mueve el cursor y pulsa Ctrl, el botón central y Escape durante unos 10 s sobre la zona de prueba de la ventana.
/// Con el gesto activo, el clic central y Escape se consumen: no llegan a la aplicación.
/// </summary>
internal sealed class InputSelfTest
{
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromMilliseconds(500);
    private const int SafetyRetries = 10;

    private readonly GlobalInputService _service;
    private readonly GestureLog _log;
    private readonly AppObservations _observed;
    private readonly Func<ScreenPoint> _zoneCenter;
    private readonly GuardedSyntheticInput _input = new();
    private CancellationToken _cancel;
    private bool _restartPending;

    public InputSelfTest(GlobalInputService service, GestureLog log, AppObservations observed, Func<ScreenPoint> zoneCenter)
    {
        _service = service;
        _log = log;
        _observed = observed;
        _zoneCenter = zoneCenter;
    }

    public async Task<IReadOnlyList<SelfTestStep>> RunAsync(CancellationToken cancel)
    {
        _cancel = cancel;
        var steps = new List<SelfTestStep>();
        if (_service.State != CaptureState.Running)
        {
            steps.Add(new SelfTestStep("Precondición", false, "La captura debe estar iniciada."));
            return steps;
        }

        var originalCursor = _input.GetCursor();
        (string Name, string Note)? abort = null;
        try
        {
            // Solo mueve el cursor (no pulsa nada). Después, ninguna tecla ni botón se envía sin pasar EnsureSafe.
            _input.MoveCursor(_zoneCenter());
            await WaitUntilSafeAsync();

            _service.AcceptOwnSyntheticInput = true;
            var center = _zoneCenter();

            steps.Add(await CtrlFirstAsync("Ctrl izquierdo → botón central → mover → soltar", InputKey.LeftControl, center));
            steps.Add(await CtrlFirstAsync("Ctrl derecho → botón central → mover → soltar", InputKey.RightControl, center));
            steps.Add(await MiddleFirstAsync());
            steps.Add(await PlainMiddleAsync());
            steps.Add(await WheelAsync());
            steps.Add(await EscapeAsync());
            steps.Add(await CtrlReleaseAsync());
            steps.Add(await ManualCancelAsync());
            steps.Add(CallbackDurations());
            steps.Add(await StopReleasesHooksAsync(center));
        }
        catch (SyntheticInputUnsafeException unsafeInput)
        {
            abort = ("Seguridad: autoprueba abortada", $"Se dejó de inyectar. Motivo: {unsafeInput.Message}.");
        }
        catch (OperationCanceledException)
        {
            abort = ("Autoprueba cancelada", "Cancelada por el usuario (cierre de la ventana).");
        }
        catch (InvalidOperationException error)
        {
            abort = ("Autoprueba interrumpida", "Error: " + error.Message);
        }
        finally
        {
            var held = _input.DescribeHeld();
            var cleanup = Cleanup(originalCursor);
            if (abort is { } aborted)
            {
                steps.Add(new SelfTestStep(
                    aborted.Name,
                    false,
                    $"{aborted.Note} Pulsado por la autoprueba al abortar: {(held.Length == 0 ? "nada" : held)}. {cleanup.Message}"));
            }
            else if (!cleanup.Ok)
            {
                steps.Add(new SelfTestStep("Limpieza", false, cleanup.Message));
            }
        }

        return steps;
    }

    // Limpieza SÍNCRONA y que no lanza: se ejecuta también cuando el dispatcher está a punto de detenerse.
    private (bool Ok, string Message) Cleanup(ScreenPoint originalCursor)
    {
        var released = _input.ReleaseAll();

        // Si la prueba paró la captura y se abortó antes de reiniciarla, se deja como estaba.
        if (_restartPending)
        {
            _restartPending = false;
            if (_service.State != CaptureState.Running)
            {
                _service.Start();
            }
        }

        WaitForTrackerToSettle();
        _service.AcceptOwnSyntheticInput = false;

        var restored = false;
        try
        {
            restored = _input.RestoreCursorIfUntouched(originalCursor);
        }
        catch (InvalidOperationException)
        {
            // Sin más recursos: el cursor se queda donde está.
        }

        return (released, released
            ? $"Limpieza: todo liberado; cursor {(restored ? "restaurado" : "no restaurado (alguien lo movió)")}."
            : "Limpieza INCOMPLETA: SendInput falló al liberar algo; puede quedar una tecla o botón pulsados.");
    }

    // Tras liberar, el servicio aún tiene que ver esas liberaciones (el hook corre en otro hilo): hasta entonces no se
    // desactiva la aceptación de lo propio, o la liberación del botón central dejaría el gesto a medias. Espera acotada.
    private void WaitForTrackerToSettle()
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < SettleTimeout && _service.State == CaptureState.Running)
        {
            var d = _service.GetDiagnostics();
            if (d.Phase == ChordPhase.Idle && !d.MiddleButton && !d.LeftControl && !d.RightControl)
            {
                return;
            }

            Thread.Sleep(10);
        }
    }

    // La ventana puede tardar un instante en estar en primer plano; si no lo consigue, EnsureSafe lanza y se aborta.
    private async Task WaitUntilSafeAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _input.EnsureSafe();
                return;
            }
            catch (SyntheticInputUnsafeException) when (attempt < SafetyRetries)
            {
                await PauseAsync();
            }
        }
    }

    private Task PauseAsync() => Task.Delay(Pause, _cancel);

    private async Task<SelfTestStep> CtrlFirstAsync(string name, InputKey ctrl, ScreenPoint center)
    {
        var mark = _log.Count;
        _observed.Reset();

        _input.KeyDown(ctrl);
        await PauseAsync();
        _input.MiddleDown();
        await PauseAsync();
        var afterDown = _log.Since(mark);
        var activated = afterDown.Count == 1 && afterDown[0].Kind == GestureEventKind.Activated;
        var downConsumed = _observed.MiddleDowns == 0;

        // El watchdog real (cada 50 ms, dos muestras) no debe cancelar un gesto sano: esto comprueba que
        // el watchdog no depende del botón central, que GetAsyncKeyState no ve cuando el hook lo consumió.
        await Task.Delay(500, _cancel);
        var stillActive = _service.GetDiagnostics().Phase == ChordPhase.Active && _log.Since(mark).Count == 1;
        var physical = _service.ReadPhysicalState();

        var target = new ScreenPoint(center.X + 60, center.Y + 20);
        var sequence = _service.PointerSequence;
        _input.MoveCursor(target);
        await PauseAsync();
        var pointerPublished = Near(_service.LatestPointer, target) && _service.PointerSequence > sequence;
        var cursorMoved = Near(_input.GetCursor(), target);

        _input.MiddleUp();
        await PauseAsync();
        var events = _log.Since(mark);
        var confirmed = events.Count == 2 && events[1].Kind == GestureEventKind.Confirmed && Near(events[1].Point, target);
        var upConsumed = _observed.MiddleUps == 0;

        _input.KeyUp(ctrl);
        await PauseAsync();
        var noExtraEvents = _log.Since(mark).Count == 2;

        var step = Result(
            name,
            ("activa el gesto", activated),
            ("el clic central NO llega a la aplicación", downConsumed),
            ("el watchdog no cancela un gesto sano (500 ms)", stillActive),
            ("el movimiento se publica y el cursor se mueve de verdad", pointerPublished && cursorMoved),
            ("soltar el botón confirma en la posición correcta", confirmed),
            ("la liberación del botón NO llega a la aplicación", upConsumed),
            ("soltar Ctrl después no genera eventos", noExtraEvents));

        // Dato para el informe: lo que Windows ve (GetAsyncKeyState) con el gesto activo y el clic central ya consumido.
        return step with
        {
            Detail = step.Detail + $" | GetAsyncKeyState con el gesto activo: Ctrl izq={physical.LeftControl}, Ctrl der={physical.RightControl}, botón central={physical.Middle}",
        };
    }

    private async Task<SelfTestStep> MiddleFirstAsync()
    {
        var mark = _log.Count;
        _observed.Reset();

        _input.MiddleDown();
        await PauseAsync();
        _input.KeyDown(InputKey.LeftControl);
        await PauseAsync();
        _input.MiddleUp();
        await PauseAsync();
        _input.KeyUp(InputKey.LeftControl);
        await PauseAsync();

        return Result(
            "Botón central primero, Ctrl después: no activa",
            ("no se publica ningún evento", _log.Count == mark),
            ("pulsación y liberación llegan a la aplicación", _observed.MiddleDowns == 1 && _observed.MiddleUps == 1));
    }

    private async Task<SelfTestStep> PlainMiddleAsync()
    {
        var mark = _log.Count;
        _observed.Reset();

        _input.MiddleDown();
        await PauseAsync();
        _input.MiddleUp();
        await PauseAsync();

        return Result(
            "Clic central normal (sin Ctrl): llega a la aplicación",
            ("no se publica ningún evento", _log.Count == mark),
            ("pulsación y liberación llegan a la aplicación", _observed.MiddleDowns == 1 && _observed.MiddleUps == 1));
    }

    private async Task<SelfTestStep> WheelAsync()
    {
        var mark = _log.Count;

        _input.KeyDown(InputKey.LeftControl);
        await PauseAsync();
        _input.Wheel();
        await PauseAsync();
        _input.KeyUp(InputKey.LeftControl);
        await PauseAsync();

        return Result("Rueda con Ctrl pulsado: no activa", ("no se publica ningún evento", _log.Count == mark));
    }

    private async Task<SelfTestStep> EscapeAsync()
    {
        var mark = _log.Count;
        _observed.Reset();

        _input.KeyDown(InputKey.LeftControl);
        await PauseAsync();
        _input.MiddleDown();
        await PauseAsync();
        _input.KeyDown(InputKey.Escape);
        await PauseAsync();
        _input.KeyUp(InputKey.Escape);
        await PauseAsync();
        _input.MiddleUp();
        await PauseAsync();
        _input.KeyUp(InputKey.LeftControl);
        await PauseAsync();

        var events = _log.Since(mark);
        return Result(
            "Escape cancela",
            ("activa y cancela con causa Escape", events.Count == 2 && events[1].Kind == GestureEventKind.Cancelled && events[1].Cause == CancelCause.Escape),
            ("Escape NO llega a la aplicación", _observed.EscapeDowns == 0),
            ("la liberación del botón se consume y no confirma", _observed.MiddleUps == 0 && events.All(e => e.Kind != GestureEventKind.Confirmed)));
    }

    private async Task<SelfTestStep> CtrlReleaseAsync()
    {
        var mark = _log.Count;
        _observed.Reset();

        _input.KeyDown(InputKey.LeftControl);
        await PauseAsync();
        _input.MiddleDown();
        await PauseAsync();
        _input.KeyUp(InputKey.LeftControl);
        await PauseAsync();
        _input.MiddleUp();
        await PauseAsync();

        var events = _log.Since(mark);
        return Result(
            "Soltar Ctrl antes de confirmar cancela",
            ("activa y cancela con causa CtrlReleased", events.Count == 2 && events[1].Kind == GestureEventKind.Cancelled && events[1].Cause == CancelCause.CtrlReleased),
            ("la liberación del botón se consume y no confirma", _observed.MiddleUps == 0 && events.All(e => e.Kind != GestureEventKind.Confirmed)));
    }

    private async Task<SelfTestStep> ManualCancelAsync()
    {
        var mark = _log.Count;
        _observed.Reset();

        _input.KeyDown(InputKey.LeftControl);
        await PauseAsync();
        _input.MiddleDown();
        await PauseAsync();
        var cancelled = _service.RequestCancel();
        await PauseAsync();
        _input.MiddleUp();
        await PauseAsync();
        _input.KeyUp(InputKey.LeftControl);
        await PauseAsync();

        var events = _log.Since(mark);
        return Result(
            "Cancelación manual (botón de diagnóstico)",
            ("había un gesto que cancelar", cancelled),
            ("se publica Cancelled con causa Manual", events.Count == 2 && events[1].Kind == GestureEventKind.Cancelled && events[1].Cause == CancelCause.Manual),
            ("la liberación del botón se consume y no confirma", _observed.MiddleUps == 0));
    }

    private SelfTestStep CallbackDurations()
    {
        var diagnostics = _service.GetDiagnostics();
        var worst = Math.Max(diagnostics.MaxKeyboardCallbackMs, diagnostics.MaxMouseCallbackMs);
        return new SelfTestStep(
            "Duración de los callbacks (informativo)",
            worst < 50 && diagnostics.SlowCallbacks == 0,
            $"máximo teclado {diagnostics.MaxKeyboardCallbackMs:F3} ms, ratón {diagnostics.MaxMouseCallbackMs:F3} ms, callbacks ≥ 50 ms: {diagnostics.SlowCallbacks}. " +
            "El primer callback incluye el arranque en frío (JIT).");
    }

    private async Task<SelfTestStep> StopReleasesHooksAsync(ScreenPoint center)
    {
        // El clic de este paso debe caer sobre la zona de prueba y no donde estuviera el cursor del usuario.
        _input.MoveCursor(center);
        await PauseAsync();
        _input.EnsureSafe(); // aborta aquí si la zona de prueba ya no es visible: el clic de este paso no lo consume ningún hook

        _observed.Reset();
        var mark = _log.Count;

        _service.Stop();
        _restartPending = true;
        _input.MiddleDown();
        await PauseAsync();
        _input.MiddleUp();
        await PauseAsync();
        var reached = _observed.MiddleDowns == 1 && _observed.MiddleUps == 1;
        var stopped = _service.State == CaptureState.Stopped && _log.Count == mark;

        var restarted = _service.Start().Success;
        _restartPending = false;
        return Result(
            "Parar la captura libera los hooks (se vuelve a iniciar al terminar)",
            ("el estado es Detenido y no hay eventos", stopped),
            ("un clic central llega a la aplicación", reached),
            ("la captura se reinició correctamente", restarted));
    }

    private static bool Near(ScreenPoint a, ScreenPoint b) => Math.Abs(a.X - b.X) <= 2 && Math.Abs(a.Y - b.Y) <= 2;

    private SelfTestStep Result(string name, params (string Check, bool Ok)[] checks)
    {
        var failed = checks.Where(c => !c.Ok).Select(c => c.Check).ToList();
        if (failed.Count == 0)
        {
            return new SelfTestStep(name, true, "correcto");
        }

        var d = _service.GetDiagnostics();
        var context = $"[fase={d.Phase}, Ctrl={d.LeftControl}/{d.RightControl}, central={d.MiddleButton}, último evento={d.LastEventKind} {d.LastEventKey} inyectado={d.LastEventInjected}, marca propia={d.LastEventOwnSynthetic}, " +
                      $"callbacks={d.KeyboardCallbacks}/{d.MouseCallbacks}, activ/conf/cancel={d.Activations}/{d.Confirmations}/{d.Cancellations}, última cancelación={d.LastCancelCause}, " +
                      $"app: central↓{_observed.MiddleDowns} ↑{_observed.MiddleUps} esc{_observed.EscapeDowns}]";
        return new SelfTestStep(name, false, "FALLA: " + string.Join("; ", failed) + " " + context);
    }
}
