using System.Threading.Channels;

namespace Radial.Windows.Input;

public enum GestureReaderEndReason
{
    /// <summary>Se pidió parar (cierre de la aplicación). Es el final esperado.</summary>
    Cancelled,

    /// <summary>El canal se cerró (el servicio se liberó) y no quedan eventos.</summary>
    ChannelCompleted,

    /// <summary>El canal terminó con una excepción: el lector no puede seguir.</summary>
    ChannelFaulted,

    /// <summary>El manejador falló en demasiados eventos seguidos: se detiene en lugar de seguir fallando indefinidamente.</summary>
    TooManyFailures,
}

/// <param name="Exception">La excepción que causó el final, si la hubo.</param>
/// <param name="ConsecutiveFailures">Fallos seguidos del manejador al terminar (solo significativo con <see cref="GestureReaderEndReason.TooManyFailures"/>).</param>
public sealed record GestureReaderEnd(GestureReaderEndReason Reason, Exception? Exception = null, int ConsecutiveFailures = 0);

/// <summary>
/// Lector único del canal de eventos del gesto. El canal no tiene límite: si el lector muere, nada lo vacía y el hook sigue
/// consumiendo botón central y Escape. Por eso un fallo del manejador sobre UN evento se notifica y el bucle sigue con el
/// siguiente (cada evento se consume una vez; no hay reintentos). Si el manejador falla en
/// <see cref="MaxConsecutiveFailures"/> eventos seguidos, el lector se detiene y lo comunica en su resultado, en lugar de
/// seguir fallando sin fin. Un evento procesado con éxito pone el contador a cero.
///
/// <see cref="RunAsync"/> no lanza nunca: todo final, incluida la cancelación, se devuelve como <see cref="GestureReaderEnd"/>.
/// El manejador y el aviso de fallo se ejecutan en el contexto de sincronización de quien llama a <see cref="RunAsync"/> (el hilo
/// de la interfaz en la aplicación): aquí no se usa <c>ConfigureAwait(false)</c> a propósito.
/// </summary>
public sealed class GestureEventReader
{
    public const int DefaultMaxConsecutiveFailures = 10;

    private readonly ChannelReader<GestureEvent> _events;
    private readonly Action<GestureEvent> _handler;
    private readonly Action<GestureEvent, Exception>? _onFailure;
    private int _totalFailures;

    /// <param name="handler">Procesa un evento. Si lanza, el evento se da por perdido y el lector continúa.</param>
    /// <param name="onFailure">Aviso de un fallo del manejador (evento y excepción). Si este aviso lanza, se ignora.</param>
    /// <param name="maxConsecutiveFailures">Fallos seguidos tras los que el lector se detiene; al menos 1.</param>
    public GestureEventReader(
        ChannelReader<GestureEvent> events,
        Action<GestureEvent> handler,
        Action<GestureEvent, Exception>? onFailure = null,
        int maxConsecutiveFailures = DefaultMaxConsecutiveFailures)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConsecutiveFailures, 1);

        _events = events;
        _handler = handler;
        _onFailure = onFailure;
        MaxConsecutiveFailures = maxConsecutiveFailures;
    }

    public int MaxConsecutiveFailures { get; }

    /// <summary>Fallos del manejador desde que empezó (recuperados o no).</summary>
    public int TotalFailures => Volatile.Read(ref _totalFailures);

    public async Task<GestureReaderEnd> RunAsync(CancellationToken cancellationToken)
    {
        var consecutive = 0;
        try
        {
            await foreach (var gesture in _events.ReadAllAsync(cancellationToken))
            {
                try
                {
                    _handler(gesture);
                    consecutive = 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new GestureReaderEnd(GestureReaderEndReason.Cancelled);
                }
                catch (Exception exception)
                {
                    consecutive++;
                    Interlocked.Increment(ref _totalFailures);
                    NotifyFailure(gesture, exception);
                    if (consecutive >= MaxConsecutiveFailures)
                    {
                        return new GestureReaderEnd(GestureReaderEndReason.TooManyFailures, exception, consecutive);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new GestureReaderEnd(GestureReaderEndReason.Cancelled);
        }
        catch (Exception exception)
        {
            return new GestureReaderEnd(GestureReaderEndReason.ChannelFaulted, exception);
        }

        // Cierre normal de la aplicación: cancelar y liberar el servicio pueden coincidir; manda la cancelación.
        return new GestureReaderEnd(cancellationToken.IsCancellationRequested ? GestureReaderEndReason.Cancelled : GestureReaderEndReason.ChannelCompleted);
    }

    private void NotifyFailure(GestureEvent gesture, Exception exception)
    {
        try
        {
            _onFailure?.Invoke(gesture, exception);
        }
        catch
        {
            // Avisar es lo último que se puede hacer: si el registro también falla, el fallo ya está contado en TotalFailures.
        }
    }
}
