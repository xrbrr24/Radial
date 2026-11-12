using Radial.Core.Timing;

namespace Radial.Core.Diagnostics;

/// <summary>
/// Un presupuesto de errores recuperables: si en una ventana corta se acumulan más de los permitidos, el estado de la aplicación ya no
/// es de fiar (algo falla una y otra vez) y hay que dejar de «recuperar» y terminar. Evita los bucles de errores en la interfaz.
/// Seguro entre hilos. Usa un reloj monotónico, así que no lo afectan los cambios de hora.
/// </summary>
public sealed class ErrorBudget
{
    public const int DefaultMaxErrors = 5;

    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(60);

    private readonly IClock _clock;
    private readonly int _maxErrors;
    private readonly TimeSpan _window;
    private readonly Queue<TimeSpan> _errors = new();
    private readonly object _gate = new();

    /// <param name="maxErrors">Errores que se toleran dentro de la ventana; el siguiente agota el presupuesto.</param>
    /// <param name="window">Duración de la ventana deslizante.</param>
    public ErrorBudget(IClock clock, int maxErrors = DefaultMaxErrors, TimeSpan? window = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxErrors, 1);

        _clock = clock;
        _maxErrors = maxErrors;
        _window = window ?? DefaultWindow;
        if (_window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "La ventana debe ser positiva.");
        }
    }

    /// <summary>Errores contados dentro de la ventana actual.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                Prune(_clock.Now);
                return _errors.Count;
            }
        }
    }

    /// <summary>Anota un error. Devuelve <c>true</c> si, con él, se superó el presupuesto (hay que dejar de recuperar).</summary>
    public bool Register()
    {
        lock (_gate)
        {
            var now = _clock.Now;
            Prune(now);
            _errors.Enqueue(now);
            return _errors.Count > _maxErrors;
        }
    }

    private void Prune(TimeSpan now)
    {
        while (_errors.Count > 0 && now - _errors.Peek() >= _window)
        {
            _errors.Dequeue();
        }
    }
}
