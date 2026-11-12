using Radial.Windows.Input;

namespace Radial.Windows.Overlay;

/// <summary>Un monitor: su área completa y su área de trabajo (sin una barra de tareas anclada), en píxeles físicos del escritorio virtual.</summary>
public readonly record struct MonitorAreas(ScreenRect Bounds, ScreenRect Work);

/// <summary>
/// Dónde están los monitores. En la aplicación lo contesta Windows (<see cref="SystemMonitorLocator"/>); las pruebas lo sustituyen por
/// una disposición inventada (monitores con coordenadas negativas, a distinto lado, de distinto tamaño).
/// </summary>
public interface IMonitorLocator
{
    /// <summary>El monitor que contiene el punto o, si no hay ninguno, el más cercano. <c>null</c> si no se sabe.</summary>
    MonitorAreas? Find(ScreenPoint point);
}

/// <summary>El localizador real: <c>MonitorFromPoint</c> y <c>GetMonitorInfo</c>.</summary>
public sealed class SystemMonitorLocator : IMonitorLocator
{
    public MonitorAreas? Find(ScreenPoint point) =>
        OverlayWindowInterop.GetMonitorAreas(point) is { } areas ? new MonitorAreas(areas.Bounds, areas.Work) : null;
}

/// <summary>
/// Cuándo el radial abierto debe seguir al cursor a otro monitor. Lógica pura. Con el cursor justo en el borde compartido, un temblor de
/// un píxel lo haría cruzar de un monitor al otro varias veces por segundo, y el radial saltaría con él: por eso solo se pasa a otro
/// monitor cuando el cursor se ha alejado <paramref name="marginPixels"/> del monitor donde está el radial (la distancia se mide hasta
/// el rectángulo de ese monitor, no hasta el siguiente). Es simétrico: volver exige alejarse el mismo margen del monitor nuevo.
/// </summary>
public static class MonitorFollow
{
    /// <summary>Distancia (DIP) que el cursor debe alejarse del monitor del radial antes de que este lo siga.</summary>
    public const double MarginDip = 12;

    /// <summary>Verdadero si el cursor está en OTRO monitor y a <paramref name="marginPixels"/> o más del rectángulo del monitor actual.</summary>
    public static bool ShouldFollow(MonitorAreas current, MonitorAreas atCursor, ScreenPoint cursor, int marginPixels)
    {
        if (atCursor.Bounds == current.Bounds)
        {
            return false;
        }

        return DistanceOutside(current.Bounds, cursor) >= Math.Max(marginPixels, 1);
    }

    /// <summary>
    /// Distancia, en píxeles y por el eje más lejano (Chebyshev), desde el punto hasta el rectángulo; 0 si está dentro. Derecha y abajo
    /// son exclusivos: el último píxel de un monitor es <c>Right - 1</c>.
    /// </summary>
    public static int DistanceOutside(ScreenRect rect, ScreenPoint point)
    {
        long dx = Math.Max(Math.Max((long)rect.Left - point.X, (long)point.X - (rect.Right - 1)), 0);
        long dy = Math.Max(Math.Max((long)rect.Top - point.Y, (long)point.Y - (rect.Bottom - 1)), 0);
        return (int)Math.Min(Math.Max(dx, dy), int.MaxValue);
    }
}
