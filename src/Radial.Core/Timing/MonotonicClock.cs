using System.Diagnostics;

namespace Radial.Core.Timing;

/// <summary>
/// <see cref="IClock"/> de producción sobre <see cref="Stopwatch"/>: monotónico (no lo alteran los cambios de hora, la zona
/// horaria ni el horario de verano) y de alta resolución. <see cref="Now"/> es el tiempo transcurrido desde que se creó el reloj;
/// el contrato solo garantiza que las diferencias entre lecturas son fiables, no el origen. Se puede leer desde cualquier hilo.
/// Las pruebas siguen usando su reloj manual.
/// </summary>
public sealed class MonotonicClock : IClock
{
    // Se mide contra un origen en lugar de convertir el contador absoluto a ticks: el contador crece con el tiempo de
    // actividad del equipo y esa conversión podría desbordar.
    private readonly long _origin = Stopwatch.GetTimestamp();

    public TimeSpan Now => Stopwatch.GetElapsedTime(_origin);
}
