using System.Threading.Channels;
using Radial.Windows.Input;

namespace Radial.Windows.Tests.Input;

/// <summary>El lector único del canal de eventos: sobrevive al fallo de un evento, no reintenta sin fin y distingue cómo termina.</summary>
public class GestureEventReaderTests
{
    private readonly Channel<GestureEvent> _channel = Channel.CreateUnbounded<GestureEvent>();
    private long _sequence;

    private GestureEvent Event(GestureEventKind kind = GestureEventKind.Activated) =>
        new(++_sequence, 1_000 + _sequence, kind, new ScreenPoint(10, 20), CancelCause.None);

    private void Write(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _channel.Writer.TryWrite(Event());
        }
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static async Task<GestureReaderEnd> Await(Task<GestureReaderEnd> run) => await run.WaitAsync(Timeout);

    // Continuar tras un fallo

    [Fact]
    public async Task AFailingEvent_DoesNotStopTheReader_TheNextOneIsProcessed()
    {
        var processed = new List<long>();
        var reader = new GestureEventReader(_channel.Reader, e =>
        {
            if (e.Sequence == 2)
            {
                throw new InvalidOperationException("boom");
            }

            processed.Add(e.Sequence);
        });
        Write(4);
        _channel.Writer.Complete();

        var end = await Await(reader.RunAsync(CancellationToken.None));

        Assert.Equal([1L, 3L, 4L], processed);
        Assert.Equal(GestureReaderEndReason.ChannelCompleted, end.Reason);
        Assert.Equal(1, reader.TotalFailures);
    }

    [Fact]
    public async Task AFailure_IsReportedWithTheEventAndTheException()
    {
        var failures = new List<(long Sequence, Exception Exception)>();
        var boom = new InvalidOperationException("boom");
        var reader = new GestureEventReader(_channel.Reader, e => throw boom, (e, ex) => failures.Add((e.Sequence, ex)), maxConsecutiveFailures: 2);
        Write(1);
        _channel.Writer.Complete();

        await Await(reader.RunAsync(CancellationToken.None));

        var (sequence, exception) = Assert.Single(failures);
        Assert.Equal(1, sequence);
        Assert.Same(boom, exception);
    }

    [Fact]
    public async Task EventsAreProcessedInOrder()
    {
        var processed = new List<long>();
        var reader = new GestureEventReader(_channel.Reader, e => processed.Add(e.Sequence));
        Write(5);
        _channel.Writer.Complete();

        await Await(reader.RunAsync(CancellationToken.None));

        Assert.Equal([1L, 2L, 3L, 4L, 5L], processed);
    }

    [Fact]
    public async Task AFailingFailureNotification_DoesNotStopTheReader()
    {
        var processed = new List<long>();
        var reader = new GestureEventReader(
            _channel.Reader,
            e =>
            {
                if (e.Sequence == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                processed.Add(e.Sequence);
            },
            (_, _) => throw new InvalidOperationException("el registro también falla"));
        Write(3);
        _channel.Writer.Complete();

        var end = await Await(reader.RunAsync(CancellationToken.None));

        Assert.Equal([2L, 3L], processed);
        Assert.Equal(GestureReaderEndReason.ChannelCompleted, end.Reason);
        Assert.Equal(1, reader.TotalFailures);
    }

    // Sin bucles de fallos

    [Fact]
    public async Task ConsecutiveFailures_StopTheReader_WhichSaysSo()
    {
        var attempts = 0;
        var reader = new GestureEventReader(_channel.Reader, _ => { attempts++; throw new InvalidOperationException("siempre"); }, maxConsecutiveFailures: 3);
        Write(10);

        var end = await Await(reader.RunAsync(CancellationToken.None));

        Assert.Equal(GestureReaderEndReason.TooManyFailures, end.Reason);
        Assert.Equal(3, end.ConsecutiveFailures);
        Assert.IsType<InvalidOperationException>(end.Exception);
        Assert.Equal(3, attempts); // cada evento se intenta una sola vez y el lector se detiene
        Assert.Equal(7, _channel.Reader.Count); // el resto queda sin leer: el lector ya no es quien lo vacía
    }

    [Fact]
    public async Task AnEventProcessedSuccessfully_ResetsTheConsecutiveCount()
    {
        var reader = new GestureEventReader(_channel.Reader, e =>
        {
            if (e.Sequence % 2 == 1)
            {
                throw new InvalidOperationException("impares");
            }
        }, maxConsecutiveFailures: 2);
        Write(7); // falla, ok, falla, ok, falla, ok, falla: nunca dos seguidos
        _channel.Writer.Complete();

        var end = await Await(reader.RunAsync(CancellationToken.None));

        Assert.Equal(GestureReaderEndReason.ChannelCompleted, end.Reason);
        Assert.Equal(4, reader.TotalFailures);
    }

    [Fact]
    public void TheConsecutiveLimit_MustBeAtLeastOne() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GestureEventReader(_channel.Reader, _ => { }, maxConsecutiveFailures: 0));

    // Cómo termina

    [Fact]
    public async Task Cancellation_IsTheExpectedEnd_AndIsNotAFailure()
    {
        var failures = 0;
        var reader = new GestureEventReader(_channel.Reader, _ => { }, (_, _) => failures++);
        using var cancel = new CancellationTokenSource();
        var run = reader.RunAsync(cancel.Token);

        cancel.Cancel();
        var end = await Await(run);

        Assert.Equal(GestureReaderEndReason.Cancelled, end.Reason);
        Assert.Null(end.Exception);
        Assert.Equal(0, failures);
        Assert.Equal(0, reader.TotalFailures);
    }

    [Fact]
    public async Task CancellingWhileTheHandlerRuns_EndsAsCancelled_NotAsAFailure()
    {
        using var cancel = new CancellationTokenSource();
        var handled = 0;
        var reader = new GestureEventReader(_channel.Reader, _ =>
        {
            handled++;
            cancel.Cancel(); // el cierre llega mientras el manejador trabaja
            cancel.Token.ThrowIfCancellationRequested();
        });
        Write(1);

        var end = await Await(reader.RunAsync(cancel.Token));

        Assert.Equal(1, handled);
        Assert.Equal(GestureReaderEndReason.Cancelled, end.Reason);
        Assert.Equal(0, reader.TotalFailures);
    }

    [Fact]
    public async Task ACancellationNotRequestedByTheToken_IsARealFailure()
    {
        var reader = new GestureEventReader(_channel.Reader, _ => throw new OperationCanceledException(), maxConsecutiveFailures: 1);
        Write(1);

        var end = await Await(reader.RunAsync(CancellationToken.None));

        Assert.Equal(GestureReaderEndReason.TooManyFailures, end.Reason);
        Assert.Equal(1, reader.TotalFailures);
    }

    [Fact]
    public async Task CancelledAndCompletedAtOnce_IsReportedAsCancelled()
    {
        // El cierre cancela el lector y libera el servicio (que cierra el canal): manda la cancelación.
        using var cancel = new CancellationTokenSource();
        var reader = new GestureEventReader(_channel.Reader, _ => { });
        _channel.Writer.Complete();
        cancel.Cancel();

        var end = await Await(reader.RunAsync(cancel.Token));

        Assert.Equal(GestureReaderEndReason.Cancelled, end.Reason);
    }

    [Fact]
    public async Task AChannelThatCloses_IsReportedAsCompleted()
    {
        var reader = new GestureEventReader(_channel.Reader, _ => { });
        _channel.Writer.Complete();

        var end = await Await(reader.RunAsync(CancellationToken.None));

        Assert.Equal(GestureReaderEndReason.ChannelCompleted, end.Reason);
        Assert.Null(end.Exception);
    }

    [Fact]
    public async Task AChannelThatFaults_IsReportedWithItsException()
    {
        var reader = new GestureEventReader(_channel.Reader, _ => { });
        var cause = new InvalidOperationException("canal roto");
        _channel.Writer.Complete(cause);

        var end = await Await(reader.RunAsync(CancellationToken.None));

        Assert.Equal(GestureReaderEndReason.ChannelFaulted, end.Reason);
        Assert.Same(cause, end.Exception);
    }

    [Fact]
    public async Task TheHandlerAndTheFailureNotification_RunOnTheContextThatStartedTheReader()
    {
        // En la aplicación ese contexto es el hilo de interfaz: el manejador toca controles de WPF. Los eventos los escribe OTRO
        // hilo (el de los hooks).
        using var context = new SingleThreadContext();
        var handledOn = new List<int>();
        var failedOn = new List<int>();
        var processed = new SemaphoreSlim(0);
        var reader = new GestureEventReader(
            _channel.Reader,
            e =>
            {
                handledOn.Add(Environment.CurrentManagedThreadId);
                processed.Release();
                if (e.Sequence == 2)
                {
                    throw new InvalidOperationException("boom");
                }
            },
            (_, _) => failedOn.Add(Environment.CurrentManagedThreadId));
        using var cancel = new CancellationTokenSource();

        var started = await context.InvokeAsync(() => reader.RunAsync(cancel.Token));
        Write(3); // desde el hilo de la prueba, con el lector ya esperando
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await processed.WaitAsync(Timeout), "El lector no procesó todos los eventos.");
        }

        cancel.Cancel();
        var end = await Await(started);

        Assert.Equal(GestureReaderEndReason.Cancelled, end.Reason);
        Assert.Equal(3, handledOn.Count);
        Assert.All(handledOn, id => Assert.Equal(context.ThreadId, id));
        Assert.Equal([context.ThreadId], failedOn); // el aviso del evento que falló, también en el contexto
        Assert.Empty(context.Faults);
    }

    [Fact]
    public async Task EventsWrittenWhileRunning_AreProcessed()
    {
        var signal = new SemaphoreSlim(0);
        var reader = new GestureEventReader(_channel.Reader, _ => signal.Release());
        using var cancel = new CancellationTokenSource();
        var run = reader.RunAsync(cancel.Token);

        Write(1);
        Assert.True(await signal.WaitAsync(Timeout));

        cancel.Cancel();
        Assert.Equal(GestureReaderEndReason.Cancelled, (await Await(run)).Reason);
    }
}
