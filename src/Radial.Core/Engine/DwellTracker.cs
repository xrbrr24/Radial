namespace Radial.Core.Engine;

/// <summary>Mide cuánto tiempo lleva sostenida una condición, usando lecturas de un reloj monotónico.</summary>
internal sealed class DwellTracker
{
    private TimeSpan? _start;

    public bool IsRunning => _start is not null;

    public void Begin(TimeSpan now) => _start = now;

    public void Reset() => _start = null;

    /// <summary>Progreso normalizado en [0, 1]. La duración debe ser positiva (la garantiza la validación).</summary>
    public double Progress(TimeSpan now, TimeSpan duration)
    {
        if (_start is not { } start)
        {
            return 0;
        }

        var elapsed = now - start;
        return elapsed <= TimeSpan.Zero ? 0 : Math.Min(1.0, elapsed / duration);
    }

    public bool IsComplete(TimeSpan now, TimeSpan duration) => _start is { } start && now - start >= duration;
}
