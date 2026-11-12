using Radial.Core.Timing;

namespace Radial.Core.Diagnostics;

/// <summary>El resultado de consultar el limitador: si se muestra el aviso y cuántos iguales se callaron desde el último que se mostró.</summary>
public readonly record struct ThrottleDecision(bool Show, int Suppressed);

/// <summary>
/// Evita las tormentas de avisos. Dos límites, ambos con un reloj monotónico:
/// (1) un aviso con la misma clave no se repite antes de <c>perKeyWindow</c> (se cuentan los silenciados y el siguiente que se muestre
/// los incluye), y (2) como mucho <c>maxPerWindow</c> avisos de cualquier clase por <c>globalWindow</c>, para que muchos fallos
/// distintos a la vez tampoco inunden la bandeja. Solo limita los AVISOS al usuario: el registro en disco no pasa por aquí.
/// Seguro entre hilos; la memoria está acotada.
/// </summary>
public sealed class NotificationThrottle
{
    public static readonly TimeSpan DefaultPerKeyWindow = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultGlobalWindow = TimeSpan.FromSeconds(60);
    public const int DefaultMaxPerWindow = 3;
    private const int MaxKeys = 256;

    private readonly IClock _clock;
    private readonly TimeSpan _perKeyWindow;
    private readonly int _maxPerWindow;
    private readonly TimeSpan _globalWindow;
    private readonly Dictionary<string, KeyState> _keys = new(StringComparer.Ordinal);
    private readonly Queue<TimeSpan> _shown = new();
    private readonly object _gate = new();

    public NotificationThrottle(IClock clock, TimeSpan? perKeyWindow = null, int maxPerWindow = DefaultMaxPerWindow, TimeSpan? globalWindow = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPerWindow, 1);

        _clock = clock;
        _perKeyWindow = perKeyWindow ?? DefaultPerKeyWindow;
        _maxPerWindow = maxPerWindow;
        _globalWindow = globalWindow ?? DefaultGlobalWindow;
        if (_perKeyWindow <= TimeSpan.Zero || _globalWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(perKeyWindow), "Las ventanas deben ser positivas.");
        }
    }

    public ThrottleDecision Evaluate(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_gate)
        {
            var now = _clock.Now;
            while (_shown.Count > 0 && now - _shown.Peek() >= _globalWindow)
            {
                _shown.Dequeue();
            }

            _keys.TryGetValue(key, out var state);
            var recentlyShown = state.LastShown is { } last && now - last < _perKeyWindow;
            if (recentlyShown || _shown.Count >= _maxPerWindow)
            {
                Store(key, state with { Suppressed = state.Suppressed + 1 });
                return new ThrottleDecision(false, state.Suppressed + 1);
            }

            _shown.Enqueue(now);
            Store(key, new KeyState(now, 0));
            return new ThrottleDecision(true, state.Suppressed);
        }
    }

    private void Store(string key, KeyState state)
    {
        if (_keys.Count >= MaxKeys && !_keys.ContainsKey(key))
        {
            // Memoria acotada: se descartan las claves que no tienen silenciados pendientes y se mostraron hace mucho.
            var now = _clock.Now;
            foreach (var stale in _keys.Where(pair => pair.Value.Suppressed == 0 && (pair.Value.LastShown is null || now - pair.Value.LastShown.Value > _perKeyWindow)).Select(pair => pair.Key).ToList())
            {
                _keys.Remove(stale);
            }

            if (_keys.Count >= MaxKeys)
            {
                _keys.Clear();
            }
        }

        _keys[key] = state;
    }

    private readonly record struct KeyState(TimeSpan? LastShown, int Suppressed);
}
