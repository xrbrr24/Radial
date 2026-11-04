using System.Collections.Concurrent;

namespace Radial.Windows.Tests;

/// <summary>
/// Un contexto de sincronización de un solo hilo, como el del hilo de interfaz de WPF pero sin sesión gráfica: un hilo propio con
/// una cola de mensajes. Sirve para comprobar que el código que captura el contexto (<c>await</c> sin <c>ConfigureAwait(false)</c>)
/// vuelve a él, que es lo que el coordinador y el lector del canal necesitan para tocar la interfaz.
/// </summary>
internal sealed class SingleThreadContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Exception> _faults = new();

    public SingleThreadContext()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "SingleThreadContext" };
        _thread.Start();
    }

    public int ThreadId => _thread.ManagedThreadId;

    /// <summary>Excepciones que escaparon de un mensaje (no deberían ocurrir).</summary>
    public IReadOnlyCollection<Exception> Faults => _faults;

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Environment.CurrentManagedThreadId == ThreadId)
        {
            d(state);
            return;
        }

        using var done = new ManualResetEventSlim();
        Post(s =>
        {
            try
            {
                d(s);
            }
            finally
            {
                done.Set();
            }
        }, state);
        done.Wait();
    }

    /// <summary>Ejecuta <paramref name="action"/> en el hilo del contexto (con este contexto instalado) y devuelve su resultado.</summary>
    public Task<T> InvokeAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, null);
        return completion.Task;
    }

    public Task InvokeAsync(Action action) => InvokeAsync(() =>
    {
        action();
        return true;
    });

    private void Run()
    {
        SetSynchronizationContext(this);
        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
        {
            try
            {
                callback(state);
            }
            catch (Exception exception)
            {
                _faults.Enqueue(exception);
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
        _queue.Dispose();
    }
}
