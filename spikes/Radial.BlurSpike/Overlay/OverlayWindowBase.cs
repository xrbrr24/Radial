using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Radial.BlurSpike.Native;

namespace Radial.BlurSpike.Overlay;

public enum ClickThroughMode
{
    /// <summary>La ventana recibe el ratón (salvo píxeles con alfa 0 en ventanas layered).</summary>
    None,

    /// <summary>Solo WS_EX_TRANSPARENT. En run1 atravesó el ratón en A y E (ventanas layered) y no en B, C ni D (no layered); la causa no se verificó.</summary>
    ExTransparent,

    /// <summary>WS_EX_LAYERED | WS_EX_TRANSPARENT: la combinación documentada para dejar pasar el ratón.</summary>
    ExLayeredTransparent,

    /// <summary>WM_NCHITTEST devuelve HTTRANSPARENT: solo atraviesa hacia ventanas del mismo hilo.</summary>
    HitTestTransparent,
}

public enum BackdropKind
{
    Acrylic = 3, // DWMSBT_TRANSIENTWINDOW
    Mica = 2,    // DWMSBT_MAINWINDOW (solo como referencia: NO es Acrylic)
    None = 1,    // DWMSBT_NONE
}

public enum AccentKind
{
    AcrylicBlurBehind,
    BlurBehind,
}

public sealed record PresentRequest(
    int CenterX,
    int CenterY,
    int SizeDip,
    double Opacity,
    double BlurRadius,
    ClickThroughMode ClickThrough,
    BackdropKind Backdrop = BackdropKind.Acrylic,
    AccentKind Accent = AccentKind.AcrylicBlurBehind,
    bool CircularRegion = true);

/// <summary>
/// Ventana overlay de prueba. Contiene SOLO contenido de diagnóstico: círculo central (para sondear píxeles),
/// contador de fotogramas y la capa de la variante. Aplica los estilos necesarios para no activarse y deja el
/// resto de decisiones (transparencia, backdrop, forma) a cada variante.
/// </summary>
internal abstract class OverlayWindowBase : Window
{
    protected const int Inset = 16;
    public static readonly Color Accent = Color.FromRgb(0x4C, 0xC2, 0xFF);
    public static readonly Color TintColor = Color.FromRgb(0x14, 0x16, 0x1A);

    private readonly TextBlock _frameText = new();
    private IntPtr _hwnd;
    private ClickThroughMode _mode;
    private bool _layeredAddedByUs;
    private bool _rendering;
    private TimeSpan _lastRender;
    private long _frames;
    private PresentRequest? _request;

    protected OverlayWindowBase(bool allowsTransparency)
    {
        AllowsTransparency = allowsTransparency;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Width = Height = 300;
        Content = Root;
        PreviewMouseDown += (_, _) => MouseDownCount++;
    }

    protected Grid Root { get; } = new();

    public IntPtr Hwnd => _hwnd;

    public int Dpi { get; private set; } = 96;

    public int PhysicalSize { get; private set; }

    public int MouseDownCount { get; private set; }

    public long Frames => _frames;

    /// <summary>Resultado de las llamadas a la API de la variante (HRESULT, etc.), para el informe.</summary>
    public string Notes { get; protected set; } = string.Empty;

    /// <summary>Marca de tiempo (Stopwatch) del primer fotograma de WPF tras el último Present.</summary>
    public long FirstRenderTimestamp { get; private set; }

    public event Action<double>? FrameInterval;

    public string ExStyleHex => _hwnd == IntPtr.Zero ? "-" : "0x" + Win32.GetExStyle(_hwnd).ToString("X8");

    /// <summary>Añade el contenido común de diagnóstico encima de lo que haya puesto la variante.</summary>
    protected void AddDiagnosticContent()
    {
        Root.Children.Add(new Ellipse
        {
            Width = 28,
            Height = 28,
            Fill = new SolidColorBrush(Accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        });

        _frameText.Foreground = Brushes.White;
        _frameText.FontSize = 14;
        _frameText.FontFamily = new FontFamily("Consolas");
        _frameText.HorizontalAlignment = HorizontalAlignment.Center;
        _frameText.VerticalAlignment = VerticalAlignment.Top;
        _frameText.Margin = new Thickness(0, 60, 0, 0);
        _frameText.IsHitTestVisible = false;
        _frameText.Text = "frame 0";
        Root.Children.Add(_frameText);
    }

    protected static SolidColorBrush Tint(double opacity) =>
        new(Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), TintColor.R, TintColor.G, TintColor.B));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Mínimo común a todas las variantes: no activar y no aparecer en Alt+Tab.
        Win32.SetExStyle(_hwnd, Win32.GetExStyle(_hwnd) | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
        OnHandleReady();
    }

    /// <summary>Configuración específica de la variante que necesita el HWND (DWM, composición...).</summary>
    protected virtual void OnHandleReady()
    {
    }

    /// <summary>Se llama en cada Present, antes de mostrar. Aquí se aplican opacidad, blur, backdrop...</summary>
    protected virtual void ApplyRequest(PresentRequest request)
    {
    }

    /// <summary>La ventana ya está oculta y colocada; la variante E captura aquí la pantalla.</summary>
    protected virtual void BeforeShow()
    {
    }

    /// <summary>Tras cada colocación o movimiento (tamaño físico ya fijado).</summary>
    protected virtual void OnPlaced(Win32.RECT bounds)
    {
    }

    protected virtual void OnHidden()
    {
    }

    public void Present(PresentRequest request)
    {
        _request = request;
        new WindowInteropHelper(this).EnsureHandle();

        ApplyRequest(request);
        SetClickThrough(request.ClickThrough);

        if (!IsVisible)
        {
            Place(request);
            BeforeShow();
        }

        FirstRenderTimestamp = 0;
        Show();
        Place(request);
        StartRendering();
    }

    public void HideOverlay()
    {
        StopRendering();
        Hide();
        OnHidden();
    }

    /// <summary>
    /// Coloca el overlay en PÍXELES FÍSICOS. Primero lo sitúa en el monitor de destino con el tamaño actual,
    /// después lee el DPI de ESE monitor con GetDpiForWindow y fija el tamaño físico definitivo.
    /// </summary>
    public void Place(PresentRequest request)
    {
        var current = Win32.GetRect(_hwnd);
        var provisional = Math.Max(current.Width, 2);
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, request.CenterX - provisional / 2, request.CenterY - provisional / 2, provisional, provisional, Win32.SWP_NOACTIVATE);

        Dpi = (int)Win32.GetDpiForWindow(_hwnd);
        PhysicalSize = (int)Math.Round(request.SizeDip * Dpi / 96.0);
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, request.CenterX - PhysicalSize / 2, request.CenterY - PhysicalSize / 2, PhysicalSize, PhysicalSize, Win32.SWP_NOACTIVATE);
        OnPlaced(Win32.GetRect(_hwnd));
    }

    /// <summary>Mueve el centro del overlay sin cambiar su tamaño.</summary>
    public void MoveCenterTo(int centerX, int centerY)
    {
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, centerX - PhysicalSize / 2, centerY - PhysicalSize / 2, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        OnPlaced(Win32.GetRect(_hwnd));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // WPF aplica el rectángulo sugerido por Windows; lo sustituimos por el nuestro cuando termine.
        if (_request is not null)
        {
            Dispatcher.BeginInvoke(() => Place(_request), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    public void SetClickThrough(ClickThroughMode mode)
    {
        _mode = mode;
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // Partimos siempre de "sin transparente" y sin el LAYERED que añadimos nosotros (el de WPF no se toca).
        var style = Win32.GetExStyle(_hwnd) & ~Win32.WS_EX_TRANSPARENT;
        if (_layeredAddedByUs)
        {
            style &= ~Win32.WS_EX_LAYERED;
            _layeredAddedByUs = false;
        }

        switch (mode)
        {
            case ClickThroughMode.ExTransparent:
                style |= Win32.WS_EX_TRANSPARENT;
                break;
            case ClickThroughMode.ExLayeredTransparent:
                style |= Win32.WS_EX_TRANSPARENT;
                if ((style & Win32.WS_EX_LAYERED) == 0)
                {
                    style |= Win32.WS_EX_LAYERED;
                    _layeredAddedByUs = true;
                }

                break;
        }

        Win32.SetExStyle(_hwnd, style);
        if (_layeredAddedByUs)
        {
            Win32.SetLayeredWindowAttributes(_hwnd, 0, 255, Win32.LWA_ALPHA);
        }
    }

    protected void ApplyEllipseRegion()
    {
        var bounds = Win32.GetRect(_hwnd);

        // El sistema se queda con la región si SetWindowRgn tiene éxito; si falla hay que liberarla.
        var region = Win32.CreateEllipticRgn(0, 0, bounds.Width + 1, bounds.Height + 1);
        var result = Win32.SetWindowRgn(_hwnd, region, true);
        if (result == 0)
        {
            Win32.DeleteObject(region);
        }

        var boxType = Win32.GetWindowRgnBox(_hwnd, out var box);
        RegionLog = $"SetWindowRgn={result} (0=fallo); GetWindowRgnBox tipo={boxType} (0=error,1=NULLREGION,2=SIMPLE,3=COMPLEX) caja={box.Width}x{box.Height}";
    }

    /// <summary>NO VALIDADO: añadido tras run1; compila, nunca se ha ejecutado. Resultado de SetWindowRgn/GetWindowRgnBox.</summary>
    public string RegionLog { get; private set; } = string.Empty;

    /// <summary>
    /// NO VALIDADO: añadido tras run1; compila, nunca se ha ejecutado.
    /// Experimento: permite que el overlay se active (para comparar el material activo con el inactivo).
    /// </summary>
    public bool AllowActivation { get; set; }

    protected void ClearRegion() => Win32.SetWindowRgn(_hwnd, IntPtr.Zero, true);

    private void StartRendering()
    {
        if (!_rendering)
        {
            CompositionTarget.Rendering += OnRendering;
            _rendering = true;
        }
    }

    private void StopRendering()
    {
        if (_rendering)
        {
            CompositionTarget.Rendering -= OnRendering;
            _rendering = false;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (time == _lastRender)
        {
            return;
        }

        if (FirstRenderTimestamp == 0)
        {
            FirstRenderTimestamp = Stopwatch.GetTimestamp();
        }

        var interval = (time - _lastRender).TotalMilliseconds;
        _lastRender = time;
        _frames++;
        _frameText.Text = $"frame {_frames}";
        FrameInterval?.Invoke(interval);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case Win32.WM_MOUSEACTIVATE when !AllowActivation:
                handled = true;
                return (IntPtr)Win32.MA_NOACTIVATE;
            case Win32.WM_NCHITTEST when _mode == ClickThroughMode.HitTestTransparent:
                handled = true;
                return (IntPtr)Win32.HTTRANSPARENT;
        }

        return IntPtr.Zero;
    }
}
