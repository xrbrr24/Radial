using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Radial.BlurSpike.Diagnostics;
using Radial.BlurSpike.Native;
using Radial.BlurSpike.Overlay;

namespace Radial.BlurSpike.Harness;

public partial class ControlWindow : Window
{
    private readonly AutoRunner _runner;
    private readonly Dictionary<string, OverlayWindowBase> _windows = new();
    private readonly bool _auto;
    private readonly string _outDir;
    private readonly DispatcherTimer _status = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public ControlWindow(bool auto, string outDir, string[]? only = null)
    {
        InitializeComponent();
        _auto = auto;
        _outDir = outDir;
        _runner = new AutoRunner(this, outDir, Log);

        VariantBox.ItemsSource = VariantCatalog.All.Select(v => $"{v.Id} — {v.Name}").ToList();
        VariantBox.SelectedIndex = 0;
        SizeBox.ItemsSource = new[] { 200, 300, 400 };
        SizeBox.SelectedItem = 300;
        ClickBox.ItemsSource = Enum.GetValues<ClickThroughMode>();
        ClickBox.SelectedItem = ClickThroughMode.ExLayeredTransparent;
        BackdropBox.ItemsSource = Enum.GetValues<BackdropKind>();
        BackdropBox.SelectedItem = BackdropKind.Acrylic;
        AccentBox.ItemsSource = Enum.GetValues<AccentKind>();
        AccentBox.SelectedItem = AccentKind.AcrylicBlurBehind;

        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            if (_auto)
            {
                // Fuera del receptor (60..1160 px) para no tapar la prueba.
                Win32.SetWindowPos(Hwnd, IntPtr.Zero, 1200, 60, 650, 800, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            }
        };

        _status.Tick += (_, _) => RefreshStatus();
        _status.Start();
        Closed += (_, _) => _runner.Receiver.Dispose();
        Loaded += async (_, _) =>
        {
            if (_auto)
            {
                var code = 1;
                try
                {
                    code = await _runner.RunAsync(VariantCatalog.All.Where(v => only is null || only.Contains(v.Id)));
                }
                catch (Exception exception)
                {
                    Log("FALLO DE LA BATERÍA: " + exception);
                }

                Application.Current.Shutdown(code);
            }
        };
    }

    public IntPtr Hwnd { get; private set; }

    private VariantInfo Selected => VariantCatalog.All[Math.Max(VariantBox.SelectedIndex, 0)];

    public void Log(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
        try
        {
            Directory.CreateDirectory(_outDir);
            File.AppendAllText(Path.Combine(_outDir, "run.log"), line + Environment.NewLine);
        }
        catch (IOException)
        {
            // El log a archivo es solo una ayuda del prototipo.
        }
    }

    private void OnVariantChanged(object sender, SelectionChangedEventArgs e)
    {
        var v = Selected;
        TechniqueText.Text = v.Technique;
        OpacitySlider.Value = v.DefaultOpacity;
        BlurSlider.IsEnabled = v.UsesBlurRadius;
        BackdropBox.IsEnabled = v.UsesBackdropChoice;
        AccentBox.IsEnabled = v.UsesAccentChoice;
        CircularBox.IsEnabled = v.UsesCircularToggle && v.Id == "D";
    }

    private OverlayWindowBase WindowFor(VariantInfo info)
    {
        if (!_windows.TryGetValue(info.Id, out var window))
        {
            window = info.Create();
            _windows[info.Id] = window;
        }

        return window;
    }

    private async Task<(OverlayWindowBase Window, PresentRequest Request)> PrepareAsync()
    {
        await _runner.EnsureReceiverAsync();
        foreach (var other in _windows.Values.Where(w => w.IsVisible))
        {
            other.HideOverlay();
        }

        var info = Selected;
        var (cx, cy) = _runner.Receiver.Center();
        var request = new PresentRequest(
            cx,
            cy,
            (int)SizeBox.SelectedItem,
            OpacitySlider.Value,
            BlurSlider.Value,
            (ClickThroughMode)ClickBox.SelectedItem,
            (BackdropKind)BackdropBox.SelectedItem,
            (AccentKind)AccentBox.SelectedItem,
            CircularBox.IsChecked == true);
        return (WindowFor(info), request);
    }

    private async void OnShow(object sender, RoutedEventArgs e)
    {
        try
        {
            var (window, request) = await PrepareAsync();
            var sample = await Probes.MeasureShowAsync(window, request);
            Log($"[{Selected.Id}] Show: llamada {sample.ShowCallMs:F1} ms, visible en pantalla {sample.VisibleMs:F1} ms. {window.Notes}");
        }
        catch (Exception exception)
        {
            Log("ERROR: " + exception.Message);
        }
    }

    private void OnHide(object sender, RoutedEventArgs e)
    {
        foreach (var window in _windows.Values.Where(w => w.IsVisible))
        {
            window.HideOverlay();
        }

        Log("Hide");
    }

    private async void OnFocusTest(object sender, RoutedEventArgs e)
    {
        var (window, request) = await PrepareAsync();
        Log($"[{Selected.Id}] " + await _runner.FocusTestAsync(window, request));
    }

    private async void OnClickTest(object sender, RoutedEventArgs e)
    {
        var (window, request) = await PrepareAsync();
        if (!window.IsVisible)
        {
            window.Present(request);
            await Task.Delay(600);
        }

        var result = await _runner.ClickTestAsync(window, request);
        Log($"[{Selected.Id}] clic ({result.Mode}): receptor clics={result.ReceiverClicks}, overlay clics={result.OverlayClicks}, atraviesa={result.PassesThrough} {result.Skipped}");
    }

    private async void OnAuto(object sender, RoutedEventArgs e)
    {
        foreach (var window in _windows.Values.Where(w => w.IsVisible))
        {
            window.HideOverlay();
        }

        await _runner.RunAsync(VariantCatalog.All);
    }

    private void RefreshStatus()
    {
        var visible = _windows.Values.FirstOrDefault(w => w.IsVisible);
        var receiver = _runner.Receiver.IsRunning ? _runner.Receiver.ReadCounters() : (Clicks: -1, Moves: -1);
        StatusText.Text = visible is null
            ? $"Overlay: INVISIBLE   receptor: clics={receiver.Clicks} movs={receiver.Moves}"
            : $"Overlay: VISIBLE ({visible.GetType().Name}, {visible.PhysicalSize}px @ {visible.Dpi} DPI, fotogramas {visible.Frames}, clics en overlay {visible.MouseDownCount})   receptor: clics={receiver.Clicks} movs={receiver.Moves}   estilos {visible.ExStyleHex}";
    }
}
