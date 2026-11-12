namespace Radial.Windows.Input;

/// <summary>
/// Una posición del cursor en un solo <c>long</c>, para publicarla desde el hilo del hook sin cerrojo (<c>Volatile</c>). X va en los 32 bits
/// altos y Y en los bajos, AMBAS con signo: un monitor a la izquierda o encima del principal tiene coordenadas negativas, y el
/// seguimiento del cursor entre monitores depende de que lleguen intactas.
/// </summary>
internal static class PointerPacking
{
    public static long Pack(ScreenPoint point) => ((long)point.X << 32) | (uint)point.Y;

    public static ScreenPoint Unpack(long packed) => new((int)(packed >> 32), (int)(packed & 0xFFFFFFFF));
}
