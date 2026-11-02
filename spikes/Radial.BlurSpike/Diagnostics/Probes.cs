using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Radial.BlurSpike.Native;
using Radial.BlurSpike.Overlay;

namespace Radial.BlurSpike.Diagnostics;

internal static class Probes
{
    public static double ElapsedMs(long since) => Stopwatch.GetElapsedTime(since).TotalMilliseconds;

    public static double Median(IReadOnlyList<double> values) => Percentile(values, 0.5);

    /// <summary>Percentil por rango más próximo (nearest-rank).</summary>
    public static double Percentile(IReadOnlyList<double> values, double fraction)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var rank = (int)Math.Ceiling(fraction * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }

    private static bool IsAccent(uint colorRef)
    {
        if (colorRef == 0xFFFFFFFF)
        {
            return false;
        }

        // COLORREF = 0x00BBGGRR; el centro del overlay es un punto de color de acento (R=4C, G=C2, B=FF).
        var r = (int)(colorRef & 0xFF);
        var g = (int)((colorRef >> 8) & 0xFF);
        var b = (int)((colorRef >> 16) & 0xFF);
        return Math.Abs(r - 0x4C) < 10 && Math.Abs(g - 0xC2) < 10 && Math.Abs(b - 0xFF) < 10;
    }

    public sealed record ShowSample(double ShowCallMs, double VisibleMs, double FirstWpfRenderMs);

    /// <summary>
    /// Tiempo hasta que el píxel central del overlay (punto de acento) aparece en la PANTALLA COMPUESTA.
    /// Un hilo aparte sondea GetPixel del DC de pantalla; la resolución práctica la limita el refresco del monitor (60 Hz).
    /// </summary>
    public static async Task<ShowSample> MeasureShowAsync(OverlayWindowBase window, PresentRequest request)
    {
        long t0 = 0;
        using var go = new ManualResetEventSlim(false);
        var poll = Task.Run(() => PollPixel(request.CenterX, request.CenterY, wantAccent: true, go, () => t0));

        t0 = Stopwatch.GetTimestamp();
        go.Set();
        window.Present(request);
        var showCall = ElapsedMs(t0);

        var visible = await poll;
        var firstRender = window.FirstRenderTimestamp == 0 ? double.NaN : Stopwatch.GetElapsedTime(t0, window.FirstRenderTimestamp).TotalMilliseconds;
        return new ShowSample(showCall, visible, firstRender);
    }

    public static async Task<double> MeasureHideAsync(OverlayWindowBase window, PresentRequest request)
    {
        long t0 = 0;
        using var go = new ManualResetEventSlim(false);
        var poll = Task.Run(() => PollPixel(request.CenterX, request.CenterY, wantAccent: false, go, () => t0));

        t0 = Stopwatch.GetTimestamp();
        go.Set();
        window.HideOverlay();
        return await poll;
    }

    private static double PollPixel(int x, int y, bool wantAccent, ManualResetEventSlim go, Func<long> startTimestamp)
    {
        var dc = Win32.GetDC(IntPtr.Zero);
        try
        {
            go.Wait();
            var t0 = startTimestamp();
            while (ElapsedMs(t0) < 3000)
            {
                if (IsAccent(Win32.GetPixel(dc, x, y)) == wantAccent)
                {
                    return ElapsedMs(t0);
                }
            }

            return double.NaN; // no se observó el cambio en 3 s
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, dc);
        }
    }

    public sealed record RowStats(double Sharpness, double Range, double MeanLuminance);

    /// <summary>
    /// Mide cuánto del contraste de las franjas sigue siendo un escalón de un píxel. Nítido ≈ 1.0 (aunque esté
    /// oscurecido por un tinte); desenfocado &lt;&lt; 1. Promedia varias filas dentro de la zona central inferior del overlay.
    /// </summary>
    public static RowStats Sharpness(ScreenGrabber.Grab grab, int centerX, int centerY, int physicalSize, Win32.RECT grabArea)
    {
        var sharp = new List<double>();
        var ranges = new List<double>();
        var means = new List<double>();
        for (var step = 0; step < 5; step++)
        {
            var y = centerY - grabArea.Top + (int)(0.24 * physicalSize) + step * 6;
            var x0 = centerX - grabArea.Left - (int)(0.22 * physicalSize);
            var x1 = centerX - grabArea.Left + (int)(0.22 * physicalSize);
            int min = 255, max = 0, peak = 0;
            long sum = 0;
            var previous = grab.Luminance(x0, y);
            for (var x = x0; x <= x1; x++)
            {
                var luminance = grab.Luminance(x, y);
                min = Math.Min(min, luminance);
                max = Math.Max(max, luminance);
                sum += luminance;
                if (x > x0)
                {
                    peak = Math.Max(peak, Math.Abs(luminance - previous));
                }

                previous = luminance;
            }

            var range = max - min;
            sharp.Add(range < 8 ? 0 : (double)peak / range);
            ranges.Add(range);
            means.Add((double)sum / (x1 - x0 + 1));
        }

        return new RowStats(sharp.Average(), ranges.Average(), means.Average());
    }

    public static string Verdict(RowStats stats) =>
        stats.Range < 8 ? "uniforme (no se distingue el fondo)"
        : stats.Sharpness >= 0.6 ? "nítido (el fondo NO está desenfocado)"
        : stats.Sharpness <= 0.35 ? "desenfocado"
        : "intermedio";

    /// <summary>Guarda una captura de DIAGNÓSTICO en la carpeta de resultados (solo del harness; las variantes nunca escriben en disco).</summary>
    public static void SavePng(ScreenGrabber.Grab grab, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(grab.Bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Recorte ampliado con vecino más próximo, para juzgar los bordes píxel a píxel.</summary>
    public static void SaveZoomPng(ScreenGrabber.Grab grab, int x, int y, int size, int factor, string path)
    {
        var width = size * factor;
        var pixels = new byte[width * width * 4];
        for (var row = 0; row < width; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var sx = Math.Clamp(x + column / factor, 0, grab.Width - 1);
                var sy = Math.Clamp(y + row / factor, 0, grab.Height - 1);
                Array.Copy(grab.Pixels, (sy * grab.Width + sx) * 4, pixels, (row * width + column) * 4, 4);
            }
        }

        var bitmap = BitmapSource.Create(width, width, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
