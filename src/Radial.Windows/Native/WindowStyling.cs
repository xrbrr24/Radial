using System.Runtime.InteropServices;

namespace Radial.Windows.Native;

/// <summary>
/// Apariencia de la barra de título de una ventana normal con atributos DOCUMENTADOS de DWM (tema oscuro y color de la barra).
/// No es ningún material ni desenfoque (CLAUDE.md prohíbe esos en el producto): solo evita una barra de título clara sobre una
/// interfaz oscura. En un Windows que no admite un atributo, la llamada falla en silencio y queda la barra predeterminada.
/// </summary>
public static partial class WindowStyling
{
    private const int UseImmersiveDarkMode = 20;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>Barra de título oscura con el color de fondo y del texto dados (RGB). Devuelve si Windows aceptó el modo oscuro.</summary>
    public static bool UseDarkTitleBar(IntPtr window, byte red, byte green, byte blue, byte textRed, byte textGreen, byte textBlue)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }

        var dark = 1;
        var accepted = DwmSetWindowAttribute(window, UseImmersiveDarkMode, ref dark, sizeof(int)) == 0;

        // COLORREF es 0x00BBGGRR.
        var caption = blue << 16 | green << 8 | red;
        var text = textBlue << 16 | textGreen << 8 | textRed;
        _ = DwmSetWindowAttribute(window, CaptionColor, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(window, TextColor, ref text, sizeof(int));
        return accepted;
    }
}
