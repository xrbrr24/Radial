using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Radial.BlurSpike.Harness;
using Radial.BlurSpike.Native;
using Radial.BlurSpike.Overlay;

namespace Radial.BlurSpike.Diagnostics;

/// <summary>
/// Ejecuta la batería completa por variante: foco, aspecto, click-through REAL (clics inyectados contra un receptor
/// en otro proceso) y rendimiento. Todo se mide; lo que no se puede medir se deja vacío o se anota.
/// </summary>
internal sealed class AutoRunner
{
    private const int ReceiverX = 60, ReceiverY = 60, ReceiverWidth = 1100, ReceiverHeight = 800;
    private const int SizeDip = 300;
    private const int Cycles = 30;
    private const int ShotMargin = 40;

    private readonly ControlWindow _host;
    private readonly string _outDir;
    private readonly Action<string> _log;
    private readonly ReceiverClient _receiver = new();

    public AutoRunner(ControlWindow host, string outDir, Action<string> log)
    {
        _host = host;
        _outDir = outDir;
        _log = log;
    }

    public ReceiverClient Receiver => _receiver;

    public async Task EnsureReceiverAsync()
    {
        if (!_receiver.IsRunning)
        {
            await _receiver.StartAsync(ReceiverX, ReceiverY, ReceiverWidth, ReceiverHeight);
        }
    }

    public PresentRequest MakeRequest(VariantInfo info, ClickThroughMode mode)
    {
        var (cx, cy) = _receiver.Center();
        return new PresentRequest(cx, cy, SizeDip, info.DefaultOpacity, 24, mode);
    }

    public async Task<int> RunAsync(IEnumerable<VariantInfo> variants)
    {
        await EnsureReceiverAsync();
        var suite = new SuiteReport
        {
            Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Windows = RuntimeInformation.OSDescription,
            Dotnet = RuntimeInformation.FrameworkDescription,
            Gpu = "Intel(R) Graphics (integrada)",
            Monitors = $"{Win32.GetSystemMetrics(80)} monitor(es), {Win32.GetSystemMetrics(0)}x{Win32.GetSystemMetrics(1)} px (primario)",
            Dpi = HostDpi(),
            OverlaySizeDip = SizeDip,
        };

        foreach (var info in variants)
        {
            _log($"=== Variante {info.Id}: {info.Name} ===");
            try
            {
                suite.Variants.Add(await RunVariantAsync(info));
            }
            catch (Exception exception)
            {
                _log($"ERROR en {info.Id}: {exception}");
                suite.Variants.Add(new VariantReport { Id = info.Id, Name = info.Name, Errors = { exception.ToString() } });
            }
        }

        Directory.CreateDirectory(_outDir);
        File.WriteAllText(Path.Combine(_outDir, "results.json"), JsonSerializer.Serialize(suite, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(_outDir, "results.md"), RenderMarkdown(suite));
        _log($"Resultados escritos en {_outDir}");
        _receiver.Dispose();
        return 0;
    }

    private int HostDpi() => (int)Math.Round(VisualTreeHelper.GetDpi(_host).PixelsPerDip * 96);

    /// <summary>Prueba de clic real sobre el overlay ya visible, con el modo de la solicitud (botón del harness).</summary>
    public Task<ClickResult> ClickTestAsync(OverlayWindowBase window, PresentRequest request)
    {
        var physical = (int)Math.Round(request.SizeDip * HostDpi() / 96.0);
        return ClickTestAsync(window, request, ShotArea(request), HostDpi(), physical, request.ClickThrough);
    }

    /// <summary>Prueba de foco interactiva: lleva el foco al receptor, muestra el overlay y compara.</summary>
    public async Task<string> FocusTestAsync(OverlayWindowBase window, PresentRequest request)
    {
        await FocusReceiverAsync();
        var before = Win32.GetForegroundWindow();
        window.Present(request);
        await Task.Delay(800);
        var after = Win32.GetForegroundWindow();
        return $"foco antes={Describe(before, window)}, después={Describe(after, window)} => {(before == after ? "SIN cambios" : "CAMBIÓ")}";
    }

    private Win32.RECT ShotArea(PresentRequest request)
    {
        var physical = (int)Math.Round(request.SizeDip * HostDpi() / 96.0);
        var area = new Win32.RECT
        {
            Left = request.CenterX - physical / 2 - ShotMargin,
            Top = request.CenterY - physical / 2 - ShotMargin,
            Right = request.CenterX - physical / 2 + physical + ShotMargin,
            Bottom = request.CenterY - physical / 2 + physical + ShotMargin,
        };
        return area;
    }

    private string Describe(IntPtr hwnd, OverlayWindowBase? overlay)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "ninguna";
        }

        var root = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
        root = root == IntPtr.Zero ? hwnd : root;
        if (overlay is not null && root == overlay.Hwnd)
        {
            return "OVERLAY";
        }

        if (root == _receiver.Hwnd)
        {
            return "receptor";
        }

        return root == _host.Hwnd ? "harness" : $"otra: '{Win32.GetTitle(root)}' (pid {Win32.GetPid(root)})";
    }

    /// <summary>Lleva el foco al receptor con un clic real en una zona suya que el overlay no cubre.</summary>
    private async Task FocusReceiverAsync()
    {
        var rect = _receiver.Bounds;
        var x = rect.Left + 120;
        var y = rect.Top + 120;
        var hit = Win32.WindowFromPoint(new Win32.POINT { X = x, Y = y });
        if (Win32.GetPid(hit) != _receiver.Pid)
        {
            _log("  (aviso) no se pudo dar el foco al receptor: otra ventana cubre su esquina.");
            return;
        }

        Win32.GetCursorPos(out var saved);
        Win32.ClickAt(x, y);
        await Task.Delay(400);
        Win32.SetCursorPos(saved.X, saved.Y);
    }

    private async Task<VariantReport> RunVariantAsync(VariantInfo info)
    {
        var report = new VariantReport { Id = info.Id, Name = info.Name };
        var window = info.Create();
        var request = MakeRequest(info, ClickThroughMode.None);
        var area = ShotArea(request);
        var dpi = HostDpi();
        var physical = (int)Math.Round(SizeDip * dpi / 96.0);

        try
        {
            await FocusReceiverAsync();

            // Referencia: el mismo recorte SIN overlay.
            var reference = ScreenGrabber.Capture(area, dpi);
            var referenceStats = Probes.Sharpness(reference, request.CenterX, request.CenterY, physical, area);
            report.ReferenceSharpness = Round(referenceStats.Sharpness);

            // --- Foco y primera aparición (en frío) ---
            var foregroundBefore = Win32.GetForegroundWindow();
            var cold = await Probes.MeasureShowAsync(window, request);
            await Task.Delay(800);
            var foregroundAfter = Win32.GetForegroundWindow();
            report.ForegroundBefore = Describe(foregroundBefore, window);
            report.ForegroundAfter = Describe(foregroundAfter, window);
            report.ForegroundUnchanged = foregroundBefore == foregroundAfter;
            report.Perf.ColdShowCallMs = Round(cold.ShowCallMs);
            report.Perf.ColdVisibleMs = Round(cold.VisibleMs);
            report.Notes = window.Notes + (window.RegionLog.Length > 0 ? " | " + window.RegionLog : string.Empty);
            report.ExStyle = window.ExStyleHex;
            var bounds = Win32.GetRect(window.Hwnd);
            report.RuntimeWindowSize = $"{bounds.Width}x{bounds.Height} px a {window.Dpi} DPI (pedido {SizeDip} DIP => {physical} px)";
            report.AltTabEligibleHeuristic = IsAltTabEligible(window.Hwnd);
            _log($"  foco antes={report.ForegroundBefore} después={report.ForegroundAfter} sin cambios={report.ForegroundUnchanged}; estilos {report.ExStyle}");

            // --- Aspecto (sin click-through) ---
            var shot = ScreenGrabber.Capture(area, dpi);
            var stats = Probes.Sharpness(shot, request.CenterX, request.CenterY, physical, area);
            report.Sharpness = Round(stats.Sharpness);
            report.ContrastRange = Round(stats.Range);
            report.MeanLuminance = Round(stats.MeanLuminance);
            report.BlurVerdict = Probes.Verdict(stats);
            var cornerX = request.CenterX - physical / 2 - area.Left + 4;
            var cornerY = request.CenterY - physical / 2 - area.Top + 4;
            report.CornerTransparent = Math.Abs(shot.Luminance(cornerX, cornerY) - reference.Luminance(cornerX, cornerY)) <= 6;
            SaveShots(info.Id, "default", shot, area, request, physical);
            Probes.SavePng(reference, Path.Combine(_outDir, "shots", "reference.png"));
            _log($"  nitidez={report.Sharpness} (referencia {report.ReferenceSharpness}) rango={report.ContrastRange} => {report.BlurVerdict}; esquina transparente={report.CornerTransparent}");

            // --- Click-through real, por modo ---
            foreach (var mode in Enum.GetValues<ClickThroughMode>())
            {
                var result = await ClickTestAsync(window, request, area, dpi, physical, mode);
                report.Clicks.Add(result);
                _log($"  click-through {mode}: receptor clics={result.ReceiverClicks} movs={result.ReceiverMoves}; overlay clics={result.OverlayClicks}; pasa={result.PassesThrough}; blur={result.BlurVerdict}; foco={result.ForegroundAfter}{(result.Skipped.Length > 0 ? " OMITIDO: " + result.Skipped : string.Empty)}");
                await FocusReceiverAsync(); // vuelve a un estado conocido para la siguiente prueba
            }

            if (info.Id is "B" or "B2" or "C" or "D")
            {
                await ActivationExperimentAsync(window, request, area, dpi, physical, report);
            }

            var chosen = ChooseMode(report);
            report.ChosenClickThrough = chosen.ToString();

            // --- Rendimiento en el modo elegido ---
            window.SetClickThrough(chosen);
            var perfRequest = request with { ClickThrough = chosen };
            await MeasurePerformanceAsync(info, window, perfRequest, report);
        }
        finally
        {
            window.HideOverlay();
            window.Close();
        }

        return report;
    }

    /// <summary>
    /// NO VALIDADO: añadido tras run1; compila, nunca se ha ejecutado (igual que ShowMisses/HideMisses, el título de la
    /// ventana ajena en los clics omitidos y el registro de la región). No hay resultados de este experimento.
    /// Activa el overlay a propósito (quita NOACTIVATE y hace un clic real sobre él) y mide si el material cambia.
    /// Sirve para saber si el aspecto plano del material se debe a que la ventana está inactiva.
    /// </summary>
    private async Task ActivationExperimentAsync(OverlayWindowBase window, PresentRequest request, Win32.RECT area, int dpi, int physical, VariantReport report)
    {
        report.ActivationTested = true;
        window.SetClickThrough(ClickThroughMode.None);
        window.AllowActivation = true;
        Win32.SetExStyle(window.Hwnd, Win32.GetExStyle(window.Hwnd) & ~Win32.WS_EX_NOACTIVATE);
        await Task.Delay(300);

        var target = new Win32.POINT { X = request.CenterX, Y = request.CenterY };
        if (Win32.GetPid(Win32.WindowFromPoint(target)) == (uint)Environment.ProcessId)
        {
            Win32.GetCursorPos(out var saved);
            Win32.ClickAt(request.CenterX, request.CenterY);
            await Task.Delay(900);
            Win32.SetCursorPos(saved.X, saved.Y);
        }

        report.ActivationBecameForeground = Win32.GetForegroundWindow() == window.Hwnd;
        var shot = ScreenGrabber.Capture(area, dpi);
        var stats = Probes.Sharpness(shot, request.CenterX, request.CenterY, physical, area);
        report.ActiveSharpness = Round(stats.Sharpness);
        report.ActiveContrastRange = Round(stats.Range);
        report.ActiveMeanLuminance = Round(stats.MeanLuminance);
        report.ActiveVerdict = Probes.Verdict(stats);
        SaveShots(report.Id, "activated", shot, area, request, physical);
        _log($"  ACTIVADO (foreground={report.ActivationBecameForeground}): nitidez={report.ActiveSharpness} rango={report.ActiveContrastRange} luminancia={report.ActiveMeanLuminance} => {report.ActiveVerdict}");

        window.AllowActivation = false;
        Win32.SetExStyle(window.Hwnd, Win32.GetExStyle(window.Hwnd) | Win32.WS_EX_NOACTIVATE);
        await FocusReceiverAsync();
    }

    private static ClickThroughMode ChooseMode(VariantReport report)
    {
        var noneVerdict = report.BlurVerdict;
        var passing = report.Clicks.Where(c => c.PassesThrough && c.Mode != nameof(ClickThroughMode.None)).ToList();

        // Entre los que dejan pasar el ratón, preferir el que conserva el aspecto de la variante.
        var best = passing.FirstOrDefault(c => c.BlurVerdict == noneVerdict) ?? passing.FirstOrDefault();
        return best is null ? ClickThroughMode.None : Enum.Parse<ClickThroughMode>(best.Mode);
    }

    private async Task<ClickResult> ClickTestAsync(OverlayWindowBase window, PresentRequest request, Win32.RECT area, int dpi, int physical, ClickThroughMode mode)
    {
        var result = new ClickResult { Mode = mode.ToString() };
        window.SetClickThrough(mode);
        await Task.Delay(500);
        result.ExStyle = window.ExStyleHex;

        var shot = ScreenGrabber.Capture(area, dpi);
        var stats = Probes.Sharpness(shot, request.CenterX, request.CenterY, physical, area);
        result.SharpnessWhileActive = Round(stats.Sharpness);
        result.BlurVerdict = Probes.Verdict(stats);

        // Seguridad: solo se inyecta el clic si el punto pertenece a una de NUESTRAS ventanas.
        var target = new Win32.POINT { X = request.CenterX, Y = request.CenterY };
        var hit = Win32.WindowFromPoint(target);
        var hitPid = Win32.GetPid(hit);
        if (hitPid != (uint)Environment.ProcessId && hitPid != _receiver.Pid)
        {
            var hitRoot = Win32.GetAncestor(hit, Win32.GA_ROOT);
            result.Skipped = $"WindowFromPoint devolvió una ventana ajena (pid {hitPid}, '{Win32.GetTitle(hitRoot == IntPtr.Zero ? hit : hitRoot)}'); no se inyectó el clic.";
            return result;
        }

        Win32.GetCursorPos(out var saved);
        var receiverBefore = _receiver.ReadCounters();
        var overlayBefore = window.MouseDownCount;

        Win32.ClickAt(request.CenterX, request.CenterY);
        await Task.Delay(600);

        var receiverAfter = _receiver.ReadCounters();
        result.ReceiverClicks = receiverAfter.Clicks - receiverBefore.Clicks;
        result.ReceiverMoves = receiverAfter.Moves - receiverBefore.Moves;
        result.OverlayClicks = window.MouseDownCount - overlayBefore;
        result.ForegroundAfter = Describe(Win32.GetForegroundWindow(), window);
        result.PassesThrough = result.ReceiverClicks >= 1 && result.OverlayClicks == 0;
        Win32.SetCursorPos(saved.X, saved.Y);
        return result;
    }

    private async Task MeasurePerformanceAsync(VariantInfo info, OverlayWindowBase window, PresentRequest request, VariantReport report)
    {
        var perf = report.Perf;
        window.HideOverlay();
        await Task.Delay(500);

        var shows = new List<double>();
        var visibles = new List<double>();
        var renders = new List<double>();
        var hides = new List<double>();
        for (var i = 0; i < Cycles; i++)
        {
            var show = await Probes.MeasureShowAsync(window, request);
            shows.Add(show.ShowCallMs);
            visibles.Add(show.VisibleMs);
            renders.Add(show.FirstWpfRenderMs);
            await Task.Delay(400);
            hides.Add(await Probes.MeasureHideAsync(window, request));
            await Task.Delay(250);
        }

        perf.Cycles = Cycles;
        perf.ShowMisses = visibles.Count(double.IsNaN);
        perf.HideMisses = hides.Count(double.IsNaN);
        perf.MissedSamples = perf.ShowMisses + perf.HideMisses;
        var okVisible = visibles.Where(v => !double.IsNaN(v)).ToList();
        var okHide = hides.Where(v => !double.IsNaN(v)).ToList();
        perf.ShowCallMedianMs = Round(Probes.Median(shows));
        perf.ShowCallP95Ms = Round(Probes.Percentile(shows, 0.95));
        perf.VisibleMedianMs = Round(Probes.Median(okVisible));
        perf.VisibleP95Ms = Round(Probes.Percentile(okVisible, 0.95));
        perf.FirstRenderMedianMs = Round(Probes.Median(renders.Where(v => !double.IsNaN(v)).ToList()));
        perf.HideMedianMs = Round(Probes.Median(okHide));
        perf.HideP95Ms = Round(Probes.Percentile(okHide, 0.95));
        _log($"  {Cycles} aperturas: visible mediana={perf.VisibleMedianMs} ms p95={perf.VisibleP95Ms} ms; ocultar mediana={perf.HideMedianMs} ms; llamada Show mediana={perf.ShowCallMedianMs} ms");

        // CPU y memoria: oculto frente a visible (con el contador de fotogramas forzando render continuo).
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(300);
        perf.CpuHiddenPercentOfOneCore = Round(await MeasureCpuAsync(TimeSpan.FromSeconds(3)));
        perf.PrivateMbHidden = Round(Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0);

        window.Present(request);
        await Task.Delay(800);
        perf.CpuVisiblePercentOfOneCore = Round(await MeasureCpuAsync(TimeSpan.FromSeconds(3)));
        var process = Process.GetCurrentProcess();
        perf.PrivateMbVisible = Round(process.PrivateMemorySize64 / 1048576.0);
        perf.WorkingSetMbVisible = Round(process.WorkingSet64 / 1048576.0);
        _log($"  CPU oculto={perf.CpuHiddenPercentOfOneCore}% visible={perf.CpuVisiblePercentOfOneCore}% (de un núcleo); memoria privada {perf.PrivateMbHidden} -> {perf.PrivateMbVisible} MB");

        // Fluidez durante el movimiento: 3 s describiendo un círculo de 70 px, un paso por fotograma.
        var intervals = new List<double>();
        var started = Stopwatch.GetTimestamp();
        void OnFrame(double interval)
        {
            intervals.Add(interval);
            var t = Probes.ElapsedMs(started) / 1000.0;
            window.MoveCenterTo(request.CenterX + (int)(70 * Math.Cos(t * 2)), request.CenterY + (int)(70 * Math.Sin(t * 2)));
        }

        window.FrameInterval += OnFrame;
        await Task.Delay(3000);
        window.FrameInterval -= OnFrame;
        window.MoveCenterTo(request.CenterX, request.CenterY);
        var valid = intervals.Skip(2).ToList();
        perf.MoveFrames = valid.Count;
        perf.MoveFrameMedianMs = Round(Probes.Median(valid));
        perf.MoveFrameP95Ms = Round(Probes.Percentile(valid, 0.95));
        perf.MoveFrameMaxMs = Round(valid.Count == 0 ? double.NaN : valid.Max());
        perf.MoveSlowFrames = valid.Count(v => v > 25);
        _log($"  movimiento: {perf.MoveFrames} fotogramas; intervalo mediana={perf.MoveFrameMedianMs} ms p95={perf.MoveFrameP95Ms} ms máx={perf.MoveFrameMaxMs} ms; >25 ms: {perf.MoveSlowFrames}");

        if (info.Id == "E" && window is WindowE e)
        {
            var area = ShotArea(request);
            var captures = new List<double>();
            var dpi = HostDpi();
            window.HideOverlay();
            await Task.Delay(300);
            for (var i = 0; i < Cycles; i++)
            {
                captures.Add(ScreenGrabber.Capture(area, dpi).Milliseconds);
            }

            perf.CaptureMedianMs = Round(Probes.Median(captures));
            perf.CaptureP95Ms = Round(Probes.Percentile(captures, 0.95));
            window.Present(request);
            await Task.Delay(300);
            perf.CaptureMegabytes = Round(e.CapturedBytes / 1048576.0);
            _log($"  captura (solo región pedida): mediana={perf.CaptureMedianMs} ms p95={perf.CaptureP95Ms} ms; bitmap en memoria {perf.CaptureMegabytes} MB");
        }
    }

    private static async Task<double> MeasureCpuAsync(TimeSpan duration)
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpuBefore = process.TotalProcessorTime;
        var started = Stopwatch.GetTimestamp();
        await Task.Delay(duration);
        process.Refresh();
        return (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / Probes.ElapsedMs(started) * 100.0;
    }

    private void SaveShots(string id, string label, ScreenGrabber.Grab shot, Win32.RECT area, PresentRequest request, int physical)
    {
        Probes.SavePng(shot, Path.Combine(_outDir, "shots", $"{id}-{label}.png"));

        // Zoom (x6) del borde derecho del círculo, a la altura del centro, para juzgar el suavizado.
        var edgeX = request.CenterX - area.Left + (int)(physical / 2.0 - 16 * HostDpi() / 96.0) - 16;
        var edgeY = request.CenterY - area.Top - 16;
        Probes.SaveZoomPng(shot, edgeX, edgeY, 32, 6, Path.Combine(_outDir, "shots", $"{id}-{label}-edge-zoom.png"));
    }

    /// <summary>Heurística de Alt+Tab (reglas conocidas, no una API): visible, sin dueño, ni TOOLWINDOW ni NOACTIVATE, no "cloaked".</summary>
    private static bool IsAltTabEligible(IntPtr hwnd)
    {
        var style = Win32.GetExStyle(hwnd);
        Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_CLOAKED, out var cloaked, sizeof(int));
        const uint GW_OWNER = 4;
        return Win32.IsWindowVisible(hwnd)
            && Win32.GetWindow(hwnd, GW_OWNER) == IntPtr.Zero
            && (style & (Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE)) == 0
            && cloaked == 0;
    }

    private static double Round(double value) => double.IsNaN(value) ? value : Math.Round(value, 2);

    private static string RenderMarkdown(SuiteReport suite)
    {
        var text = new StringBuilder();
        text.AppendLine($"# Resultados automáticos — {suite.Date}");
        text.AppendLine();
        text.AppendLine($"{suite.Windows} · {suite.Dotnet} · {suite.Gpu} · {suite.Monitors} · {suite.Dpi} DPI · overlay {suite.OverlaySizeDip} DIP");
        foreach (var v in suite.Variants)
        {
            text.AppendLine();
            text.AppendLine($"## {v.Id} — {v.Name}");
            text.AppendLine($"- Notas: {v.Notes}");
            text.AppendLine($"- Estilos extendidos: {v.ExStyle}; tamaño real: {v.RuntimeWindowSize}");
            text.AppendLine($"- Foco: antes={v.ForegroundBefore}, después={v.ForegroundAfter}, sin cambios={v.ForegroundUnchanged}; elegible para Alt+Tab (heurística)={v.AltTabEligibleHeuristic}");
            text.AppendLine($"- Aspecto: nitidez {v.Sharpness} (referencia sin overlay {v.ReferenceSharpness}), rango de contraste {v.ContrastRange}, luminancia media {v.MeanLuminance} => {v.BlurVerdict}; esquina transparente={v.CornerTransparent}");
            if (v.ActivationTested)
            {
                text.AppendLine($"- Experimento ACTIVADO: foreground={v.ActivationBecameForeground}; nitidez {v.ActiveSharpness}, rango {v.ActiveContrastRange}, luminancia {v.ActiveMeanLuminance} => {v.ActiveVerdict}");
            }

            text.AppendLine("- Click-through:");
            foreach (var c in v.Clicks)
            {
                text.AppendLine($"  - {c.Mode}: receptor clics={c.ReceiverClicks}, movs={c.ReceiverMoves}; overlay clics={c.OverlayClicks}; **pasa={c.PassesThrough}**; blur={c.BlurVerdict} (nitidez {c.SharpnessWhileActive}); foco={c.ForegroundAfter}; estilos {c.ExStyle} {c.Skipped}");
            }

            var p = v.Perf;
            text.AppendLine($"- Modo elegido para rendimiento: {v.ChosenClickThrough}");
            text.AppendLine($"- Aparición en frío: Show()={p.ColdShowCallMs} ms, visible en pantalla={p.ColdVisibleMs} ms");
            text.AppendLine($"- {p.Cycles} aperturas: visible mediana={p.VisibleMedianMs} ms, p95={p.VisibleP95Ms} ms; llamada Show() mediana={p.ShowCallMedianMs} ms, p95={p.ShowCallP95Ms} ms; primer render WPF mediana={p.FirstRenderMedianMs} ms; ocultar mediana={p.HideMedianMs} ms, p95={p.HideP95Ms} ms; muestras perdidas={p.MissedSamples} (apariciones {p.ShowMisses}, ocultaciones {p.HideMisses})");
            text.AppendLine($"- CPU (de un núcleo): oculto={p.CpuHiddenPercentOfOneCore}%, visible={p.CpuVisiblePercentOfOneCore}%; memoria privada {p.PrivateMbHidden} -> {p.PrivateMbVisible} MB; working set visible {p.WorkingSetMbVisible} MB");
            text.AppendLine($"- Movimiento: {p.MoveFrames} fotogramas, intervalo mediana={p.MoveFrameMedianMs} ms, p95={p.MoveFrameP95Ms} ms, máx={p.MoveFrameMaxMs} ms, >25 ms={p.MoveSlowFrames}");
            if (p.CaptureMedianMs > 0)
            {
                text.AppendLine($"- Captura: mediana={p.CaptureMedianMs} ms, p95={p.CaptureP95Ms} ms, bitmap {p.CaptureMegabytes} MB");
            }

            foreach (var error in v.Errors)
            {
                text.AppendLine($"- ERROR: {error}");
            }
        }

        return text.ToString();
    }
}
