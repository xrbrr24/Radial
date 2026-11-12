using Radial.Windows.Input;

namespace Radial.Windows.Overlay;

/// <summary>Rectángulo cuadrado del overlay en píxeles físicos de pantalla (coordenadas del escritorio virtual: pueden ser negativas).</summary>
public readonly record struct OverlayBounds(int X, int Y, int Size)
{
    public int Right => X + Size;

    public int Bottom => Y + Size;

    /// <summary>Centro del cuadrado (el tamaño es siempre par, así que cae exactamente sobre un píxel).</summary>
    public ScreenPoint Center => new(X + Size / 2, Y + Size / 2);
}

/// <summary>Área de un monitor en píxeles físicos del escritorio virtual (pueden ser negativos). Derecha y abajo son EXCLUSIVOS.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    /// <summary>Tiene área: un rectángulo vacío o invertido (Windows no devolvió nada útil) no sirve para ajustar nada.</summary>
    public bool IsUsable => Right > Left && Bottom > Top;
}

/// <summary>
/// Cálculo puro de dónde va el overlay. El cursor llega en píxeles físicos (<see cref="ScreenPoint"/>) y el tamaño se define en
/// píxeles independientes del dispositivo (DIP); la conversión usa el DPI del monitor donde se muestra. Con el cursor cerca de un
/// borde, <see cref="Fit"/> desplaza el centro hacia dentro lo justo para que toda la ventana quepa en ese monitor: así los ocho
/// sectores se ven y el cursor puede alcanzarlos todos (sección 6.4 del documento técnico, E-1 resuelto con la opción de ajuste).
/// </summary>
public static class OverlayPlacement
{
    public const int BaseDpi = 96;

    /// <summary>Factor entre píxeles físicos y DIP: 1.25 al 125 %.</summary>
    public static double Scale(int dpi)
    {
        RequireDpi(dpi);
        return (double)dpi / BaseDpi;
    }

    /// <summary>
    /// Tamaño físico de un cuadrado de <paramref name="sizeDip"/> DIP. Siempre par, para que el centro caiga exactamente sobre un
    /// píxel y el cursor quede en el centro del dibujo.
    /// </summary>
    public static int PhysicalSize(double sizeDip, int dpi)
    {
        if (!double.IsFinite(sizeDip) || sizeDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeDip), sizeDip, "El tamaño debe ser un número positivo.");
        }

        var size = (int)Math.Round(sizeDip * Scale(dpi), MidpointRounding.AwayFromZero);
        size += size & 1;
        return Math.Max(size, 2);
    }

    /// <summary>Cuadrado de <paramref name="sizeDip"/> DIP centrado en <paramref name="center"/>.</summary>
    public static OverlayBounds Centered(ScreenPoint center, double sizeDip, int dpi)
    {
        var size = PhysicalSize(sizeDip, dpi);
        return new OverlayBounds(center.X - size / 2, center.Y - size / 2, size);
    }


    /// <summary>
    /// El centro con el que el cursor queda a (<paramref name="offsetXDip"/>, <paramref name="offsetYDip"/>) DIP de él, a esta escala:
    /// <c>cursor − desfase × escala</c>, redondeado a píxel. Con desfase 0 es el propio cursor. Es el centro «ideal» al seguir al cursor a
    /// otro monitor: si <see cref="Fit"/> no lo mueve, el cursor conserva su posición relativa al radial y la selección no cambia.
    /// </summary>
    public static ScreenPoint IdealCenter(ScreenPoint cursor, double offsetXDip, double offsetYDip, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "La escala debe ser un número positivo.");
        }

        if (!double.IsFinite(offsetXDip) || !double.IsFinite(offsetYDip))
        {
            throw new ArgumentOutOfRangeException(nameof(offsetXDip), "El desfase debe ser finito.");
        }

        return new ScreenPoint(
            cursor.X - (int)Math.Round(offsetXDip * scale, MidpointRounding.AwayFromZero),
            cursor.Y - (int)Math.Round(offsetYDip * scale, MidpointRounding.AwayFromZero));
    }
    /// <summary>
    /// Centro más cercano a <paramref name="desired"/> con el que un cuadrado de <paramref name="size"/> píxeles cabe entero en
    /// <paramref name="monitor"/>, eje por eje. Si el cursor está lejos de los bordes devuelve <paramref name="desired"/> sin tocarlo.
    /// Casos límite, todos sin lanzar: si el monitor es más pequeño que el cuadrado en un eje, se centra en el monitor en ese eje
    /// (no cabe, pero queda simétrico); si el rectángulo no es utilizable (Windows no dio el monitor) no se ajusta nada. Un centro
    /// fuera del monitor (cursor en otro monitor, valores extremos) se lleva dentro. No cambia el tamaño ni la escala.
    /// </summary>
    public static ScreenPoint Fit(ScreenPoint desired, int size, ScreenRect monitor)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "El tamaño debe ser positivo.");
        }

        if (!monitor.IsUsable)
        {
            return desired;
        }

        return new ScreenPoint(
            FitAxis(desired.X, size, monitor.Left, monitor.Right),
            FitAxis(desired.Y, size, monitor.Top, monitor.Bottom));
    }

    private static int FitAxis(int desired, int size, int low, int high)
    {
        // long: coordenadas extremas no deben desbordar al sumar la mitad del tamaño.
        var half = size / 2;
        long min = (long)low + half;
        long max = (long)high - half;
        if (min > max)
        {
            return (int)(((long)low + high) / 2);
        }

        return (int)Math.Clamp(desired, min, max);
    }

    private static void RequireDpi(int dpi)
    {
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "El DPI debe ser positivo.");
        }
    }
}
