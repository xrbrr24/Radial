using System.IO.Pipes;

namespace Radial.Windows.Runtime;

/// <summary>
/// Mantiene una única instancia de Radial por usuario y sesión de Windows. Una ejecución posterior no crea hooks ni abre una segunda
/// configuración: avisa a la primera por una tubería local para que muestre su centro de control y termina. No conoce WPF; quien la
/// use decide en qué hilo atiende <see cref="Activated"/>.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string ActivationMessage = "activate";
    private readonly Mutex? _mutex;
    private readonly CancellationTokenSource? _stopping;
    private readonly string? _pipeName;
    private Task? _listener;
    private Action? _activationHandler;
    private int _pendingActivations;
    private int _disposed;

    private SingleInstance(Mutex? mutex, string? pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        if (mutex is not null)
        {
            _stopping = new CancellationTokenSource();
        }
    }

    /// <summary>Verdadero solo para la instancia que posee el mutex.</summary>
    public bool IsPrimary => _mutex is not null;

    /// <summary>
    /// Intenta adquirir la instancia. <paramref name="scope"/> debe ser estable para el producto y no contener datos del usuario;
    /// se usa como parte de los nombres del mutex y de la tubería.
    /// </summary>
    public static SingleInstance TryAcquire(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var safeScope = string.Concat(scope.Where(char.IsAsciiLetterOrDigit));
        if (safeScope.Length == 0)
        {
            throw new ArgumentException("El ámbito de instancia no contiene caracteres utilizables.", nameof(scope));
        }

        var mutex = new Mutex(initiallyOwned: true, "Local\\Radial." + safeScope, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return new SingleInstance(null, "Radial." + safeScope);
        }

        return new SingleInstance(mutex, "Radial." + safeScope);
    }

    /// <summary>
    /// Empieza a recibir peticiones de activación. Se puede llamar una sola vez y solo en la instancia principal. Si una segunda
    /// instancia llegó durante el arranque, la petición se entrega en cuanto se instala el controlador.
    /// </summary>
    public void Start(Action activated)
    {
        ArgumentNullException.ThrowIfNull(activated);
        ThrowIfNotPrimary();
        if (Interlocked.CompareExchange(ref _activationHandler, activated, null) is not null)
        {
            throw new InvalidOperationException("La escucha de activación ya está en marcha.");
        }

        _listener = ListenAsync(_stopping!.Token);
        while (Interlocked.Decrement(ref _pendingActivations) >= 0)
        {
            InvokeHandler();
        }
    }

    /// <summary>Intenta pedir a la instancia principal que se muestre. Nunca lanza: una segunda instancia simplemente termina.</summary>
    public async Task<bool> ActivatePrimaryAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (IsPrimary || _pipeName is null || timeout <= TimeSpan.Zero)
        {
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue), cancellationToken).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, leaveOpen: false);
            await writer.WriteAsync(ActivationMessage.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_stopping is not null)
        {
            _stopping.Cancel();
            try
            {
                _listener?.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
                // El bucle de escucha contiene sus propios fallos; el cierre no se bloquea por una tubería ya cerrada.
            }

            _stopping.Dispose();
        }

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }

    private async Task ListenAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName!, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(stopping).ConfigureAwait(false);
                using var reader = new StreamReader(server, leaveOpen: true);
                var message = await reader.ReadToEndAsync(stopping).ConfigureAwait(false);
                if (string.Equals(message, ActivationMessage, StringComparison.Ordinal))
                {
                    if (Volatile.Read(ref _activationHandler) is null)
                    {
                        Interlocked.Increment(ref _pendingActivations);
                    }
                    else
                    {
                        InvokeHandler();
                    }
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // El cliente puede desaparecer durante el arranque; la siguiente iteración vuelve a escuchar.
            }
            catch (UnauthorizedAccessException)
            {
                // Una ACL local anómala no debe tumbar la instancia que ya funciona. No se reintenta de forma ocupada.
                return;
            }
        }
    }

    private void InvokeHandler()
    {
        try
        {
            Volatile.Read(ref _activationHandler)?.Invoke();
        }
        catch
        {
            // La activación entre procesos nunca puede terminar el bucle que evita segundas instancias. Quien integre la UI debe
            // contener y registrar su propio fallo en el dispatcher.
        }
    }

    private void ThrowIfNotPrimary()
    {
        if (!IsPrimary)
        {
            throw new InvalidOperationException("Solo la instancia principal puede escuchar activaciones.");
        }

        ObjectDisposedException.ThrowIf(_disposed != 0, this);
    }
}
