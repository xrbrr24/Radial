namespace Radial.Windows.Input;

/// <summary>Temporizador periódico que solo corre cuando hace falta (watchdog durante un gesto, sonda de salud mientras la captura está en marcha).</summary>
internal interface IPeriodicTimer : IDisposable
{
    void Start(TimeSpan period);

    void Stop();
}

internal interface ITimerFactory
{
    IPeriodicTimer Create(Action callback);
}

internal sealed class ThreadingTimerFactory : ITimerFactory
{
    public IPeriodicTimer Create(Action callback) => new ThreadingTimer(callback);

    private sealed class ThreadingTimer : IPeriodicTimer
    {
        private readonly Timer _timer;

        public ThreadingTimer(Action callback)
        {
            _timer = new Timer(_ => callback(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start(TimeSpan period) => _timer.Change(period, period);

        public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

        public void Dispose() => _timer.Dispose();
    }
}

/// <summary>
/// Bloqueo de sesión y suspensión del equipo. Radial.Windows no depende de ningún paquete para obtenerlos:
/// la aplicación aporta la implementación (en Radial.App, sobre <c>Microsoft.Win32.SystemEvents</c>).
/// Sin implementación, el servicio funciona pero no cancela por bloqueo ni suspensión.
/// </summary>
public interface ISystemStateEvents
{
    event Action? SessionLocked;

    event Action? SessionUnlocked;

    event Action? Suspending;

    event Action? Resumed;

    /// <summary>Empieza a escuchar. Se llama al iniciar la captura.</summary>
    void Attach();

    /// <summary>Deja de escuchar. Se llama al detener la captura.</summary>
    void Detach();
}

/// <summary>Sin eventos del sistema: la captura no reacciona a bloqueo ni suspensión.</summary>
public sealed class NullSystemStateEvents : ISystemStateEvents
{
#pragma warning disable CS0067 // Nunca se producen: es una implementación vacía a propósito.
    public event Action? SessionLocked;

    public event Action? SessionUnlocked;

    public event Action? Suspending;

    public event Action? Resumed;
#pragma warning restore CS0067

    public void Attach()
    {
    }

    public void Detach()
    {
    }
}
