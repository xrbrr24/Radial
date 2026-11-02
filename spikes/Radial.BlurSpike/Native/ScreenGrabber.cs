using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Radial.BlurSpike.Native;

/// <summary>Captura una región de la pantalla compuesta (GDI BitBlt con CAPTUREBLT). Solo en memoria: nunca escribe en disco.</summary>
internal static class ScreenGrabber
{
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;

    public sealed record Grab(BitmapSource Bitmap, byte[] Pixels, int Width, int Height, double Milliseconds)
    {
        public int Stride => Width * 4;

        public int Luminance(int x, int y)
        {
            var i = (y * Width + x) * 4;
            return (Pixels[i + 2] * 299 + Pixels[i + 1] * 587 + Pixels[i] * 114) / 1000;
        }
    }

    public static Grab Capture(Win32.RECT area, int dpi)
    {
        var started = Stopwatch.GetTimestamp();
        var width = area.Width;
        var height = area.Height;

        var screen = Win32.GetDC(IntPtr.Zero);
        var memory = Win32.CreateCompatibleDC(screen);
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // de arriba abajo
            biPlanes = 1,
            biBitCount = 32,
        };

        var section = Win32.CreateDIBSection(memory, ref header, 0, out var bits, IntPtr.Zero, 0);
        var previous = Win32.SelectObject(memory, section);
        try
        {
            Win32.BitBlt(memory, 0, 0, width, height, screen, area.Left, area.Top, SRCCOPY | CAPTUREBLT);
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            var bitmap = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgr32, null, pixels, width * 4);
            bitmap.Freeze();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return new Grab(bitmap, pixels, width, height, elapsed);
        }
        finally
        {
            Win32.SelectObject(memory, previous);
            Win32.DeleteObject(section);
            Win32.DeleteDC(memory);
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public static Win32.RECT VirtualScreen()
    {
        const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        var left = Win32.GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = Win32.GetSystemMetrics(SM_YVIRTUALSCREEN);
        return new Win32.RECT
        {
            Left = left,
            Top = top,
            Right = left + Win32.GetSystemMetrics(SM_CXVIRTUALSCREEN),
            Bottom = top + Win32.GetSystemMetrics(SM_CYVIRTUALSCREEN),
        };
    }
}
