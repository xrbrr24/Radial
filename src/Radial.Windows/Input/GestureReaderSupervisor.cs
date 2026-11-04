namespace Radial.Windows.Input;

/// <summary>
/// Un fallo del lector de eventos que obligó a detener la captura (<see cref="GestureReaderSupervisor.Failed"/>).
/// </summary>
/// <param name="ReaderEnd">Cómo terminó el lector.</param>
/// <param name="Reason">Texto para el diagnóstico (el mismo que se le dio a la captura).</param>
/// <param name="CleanupProblems">Pasos de la limpieza que fallaron (detener los hooks, ocultar el overlay); vacío si todo fue bien.</param>
public sealed record CaptureFailure(GestureReaderEnd ReaderEnd, string Reason, IReadOnlyList<string> CleanupProblems);

/// <summary>
/// Ciclo de vida del lector de eventos frente a los hooks. El canal de eventos del servicio no tiene límite y los hooks siguen
/// consumiendo el botón central y Esc mientras estén instalados; si el lector termina SIN que se haya pedido (canal cerrado o con
/// error, o demasiados fallos seguidos, <see cref="GestureReaderEnd"/>), el sistema seguiría capturando entrada que nadie procesa.
/// Este supervisor ejecuta el lector y, ante un final inesperado, y UNA sola vez:
/// (1) detiene la captura como fallo (<paramref name="failCapture"/>: quita los hooks, cancela el gesto activo y deja el estado
/// en <see cref="CaptureState.Faulted"/>), (2) oculta el overlay y abandona el gesto (<paramref name="dismissOverlay"/>) y
/// (3) lo comunica con <see cref="Failed"/>. Cada paso se intenta aunque falle el anterior; lo que falla se anota en
/// <see cref="CaptureFailure.CleanupProblems"/>. La aplicación sigue abierta, sin captura.
///
/// No es un fallo: el final por cancelación (cierre de la aplicación), ni cualquier final cuando ya se pidió el cierre
/// (cancelar el lector y liberar el servicio pueden coincidir). No reinicia el lector ni la captura: es el diseño del fallo
/// seguro, no una recuperación automática. Se usa desde el hilo de la interfaz: <see cref="GestureEventReader.RunAsync"/> y los
/// pasos de la limpieza se ejecutan en el contexto de sincronización de quien llama a <see cref="RunAsync"/>.
/// </summary>
public sealed class GestureReaderSupervisor
{
    private readonly GestureEventReader _reader;
    private readonly Action<string> _failCapture;
    private readonly Action _dismissOverlay;
    private int _failureHandled;
    private GestureReaderEnd? _end;
    private CaptureFailure? _failure;

    /// <param name="failCapture">Detiene la captura como fallo; normalmente <c>GlobalInputService.Fail</c>.</param>
    /// <param name="dismissOverlay">Oculta el overlay y abandona el gesto; normalmente <c>RadialOverlayCoordinator.Dismiss</c>.</param>
    public GestureReaderSupervisor(GestureEventReader reader, Action<string> failCapture, Action dismissOverlay)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(failCapture);
        ArgumentNullException.ThrowIfNull(dismissOverlay);

        _reader = reader;
        _failCapture = failCapture;
        _dismissOverlay = dismissOverlay;
    }

    /// <summary>Se produce una sola vez, si el lector termina de forma inesperada y después de la limpieza.</summary>
    public event Action<CaptureFailure>? Failed;

    /// <summary>Cómo terminó el lector (de forma normal o no); <c>null</c> mientras sigue en marcha.</summary>
    public GestureReaderEnd? End => Volatile.Read(ref _end);

    /// <summary>El fallo que se gestionó, o <c>null</c> si no hubo ninguno.</summary>
    public CaptureFailure? Failure => Volatile.Read(ref _failure);

    /// <summary>
    /// Ejecuta el lector hasta que termina y gestiona su final. No lanza nunca: todo final se devuelve como
    /// <see cref="GestureReaderEnd"/>. <paramref name="closing"/> es la señal de cierre de la aplicación.
    /// </summary>
    public async Task<GestureReaderEnd> RunAsync(CancellationToken closing)
    {
        GestureReaderEnd end;
        try
        {
            end = await _reader.RunAsync(closing);
        }
        catch (Exception exception)
        {
            // RunAsync no debería lanzar (contrato); si lo hiciera, es un final inesperado más.
            end = new GestureReaderEnd(GestureReaderEndReason.ChannelFaulted, exception);
        }

        Volatile.Write(ref _end, end);

        var expected = end.Reason == GestureReaderEndReason.Cancelled || closing.IsCancellationRequested;
        if (!expected)
        {
            HandleUnexpectedEnd(end);
        }

        return end;
    }

    /// <summary>Texto para el diagnóstico de un final del lector.</summary>
    public static string Describe(GestureReaderEnd end) => end.Reason switch
    {
        GestureReaderEndReason.ChannelCompleted => "el canal de eventos se cerró; los gestos ya no se procesan.",
        GestureReaderEndReason.ChannelFaulted => $"el canal de eventos terminó con un error ({end.Exception?.GetType().Name}); los gestos ya no se procesan.",
        GestureReaderEndReason.TooManyFailures => $"{end.ConsecutiveFailures} eventos seguidos fallaron ({end.Exception?.GetType().Name}); el lector se detuvo y los gestos ya no se procesan.",
        _ => "detenido.",
    };

    private void HandleUnexpectedEnd(GestureReaderEnd end)
    {
        // Una sola vez aunque RunAsync se llame más de una vez o se solape con otro final.
        if (Interlocked.Exchange(ref _failureHandled, 1) != 0)
        {
            return;
        }

        var reason = "el lector de eventos terminó de forma inesperada: " + Describe(end);
        var problems = new List<string>();

        // Primero lo que impide que se siga consumiendo entrada; después el overlay. Cada paso se intenta pase lo que pase.
        Attempt(() => _failCapture(reason), "detener la captura (hooks)", problems);
        Attempt(_dismissOverlay, "ocultar el overlay", problems);

        var failure = new CaptureFailure(end, reason, problems);
        Volatile.Write(ref _failure, failure);
        Notify(failure);
    }

    private static void Attempt(Action step, string what, List<string> problems)
    {
        try
        {
            step();
        }
        catch (Exception exception)
        {
            problems.Add($"No se pudo {what}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    // Un suscriptor defectuoso no puede impedir que los demás se enteren ni hacer fallar al supervisor.
    private void Notify(CaptureFailure failure)
    {
        var handlers = Failed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<CaptureFailure>>())
        {
            try
            {
                handler(failure);
            }
            catch
            {
                // Avisar es lo último que se puede hacer: no hay otro sitio donde decir que el aviso falló.
            }
        }
    }
}
