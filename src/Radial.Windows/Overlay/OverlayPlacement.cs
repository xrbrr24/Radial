using Radial.Windows.Input;

namespace Radial.Windows.Overlay;

/// <summary>Rectángulo cuadrado del overlay en píxeles físicos de pantalla (coordenadas del escritorio virtual: pueden ser negativas).</summary>
public readonly record struct OverlayBounds(int X, int Y, int Size)
{
    public int Right => X + Size;

    public int Bottom => Y + Size;
}

/// <summary>
/// Cálculo puro de dónde va el overlay. El cursor llega en píxeles físicos (<see cref="ScreenPoint"/>) y el tamaño se define en
/// píxeles independientes del dispositivo (DIP); la conversión usa el DPI del monitor donde se muestra. No ajusta nada a los
/// bordes de la pantalla: el radial puede quedar parcialmente fuera (opción A de la sección 6.4 del documento técnico, E-1 abierto).
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

    private static void RequireDpi(int dpi)
    {
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "El DPI debe ser positivo.");
        }
    }
}
