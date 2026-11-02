using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Radial.BlurSpike.Native;

namespace Radial.BlurSpike.Overlay;

/// <summary>
/// VARIANTE E — captura de pantalla + BlurEffect de WPF.
/// ESTO NO ES BLUR EN VIVO: es una FOTO estática del escritorio tomada justo antes de mostrar la ventana y
/// desenfocada por WPF. Si lo que hay debajo cambia mientras el overlay está visible, no se refleja.
/// La captura solo vive en memoria y se descarta al ocultar. No se escribe en disco ni se envía a ningún sitio.
/// </summary>
internal sealed class WindowE : OverlayWindowBase
{
    private readonly Grid _host = new() { Margin = new Thickness(Inset), ClipToBounds = true };
    private readonly Canvas _canvas = new();
    private readonly Image _image = new() { Stretch = Stretch.None };
    private readonly Ellipse _tint = new();
    private readonly Ellipse _ring = new();
    private readonly BlurEffect _blur = new() { KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
    private ScreenGrabber.Grab? _grab;
    private Win32.RECT _grabArea;
    private double _blurRadius;

    public WindowE()
        : base(allowsTransparency: true)
    {
        _image.Effect = _blur;
        _canvas.Children.Add(_image);
        _host.Children.Add(_canvas);
        _host.Children.Add(_tint);
        _host.Children.Add(_ring);
        Root.Children.Add(new Ellipse { Margin = new Thickness(Inset), Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.5, Color = Colors.Black } });
        Root.Children.Add(_host);
        AddDiagnosticContent();

        _ring.Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        _ring.StrokeThickness = 1;
        SizeChanged += (_, _) => UpdateClip();
        Notes = "Captura GDI (BitBlt) + System.Windows.Media.Effects.BlurEffect. Blur de una FOTO estática, no en vivo.";
    }

    /// <summary>Tamaño de la última captura en memoria (BGRA, 4 bytes/píxel).</summary>
    public long CapturedBytes => _grab?.Pixels.Length ?? 0;

    public double LastCaptureMilliseconds { get; private set; }

    protected override void ApplyRequest(PresentRequest request)
    {
        _blurRadius = request.BlurRadius;
        _blur.Radius = request.BlurRadius;
        _tint.Fill = Tint(request.Opacity);
    }

    protected override void BeforeShow()
    {
        // La ventana ya está colocada pero oculta: no aparece en la captura.
        var bounds = Win32.GetRect(Hwnd);
        var margin = (int)Math.Ceiling(3 * _blurRadius * Dpi / 96.0) + 120; // 120 px extra para el test de movimiento
        var wanted = new Win32.RECT { Left = bounds.Left - margin, Top = bounds.Top - margin, Right = bounds.Right + margin, Bottom = bounds.Bottom + margin };
        var screen = ScreenGrabber.VirtualScreen();
        _grabArea = new Win32.RECT
        {
            Left = Math.Max(wanted.Left, screen.Left),
            Top = Math.Max(wanted.Top, screen.Top),
            Right = Math.Min(wanted.Right, screen.Right),
            Bottom = Math.Min(wanted.Bottom, screen.Bottom),
        };

        _grab = ScreenGrabber.Capture(_grabArea, Dpi);
        LastCaptureMilliseconds = _grab.Milliseconds;
        _image.Source = _grab.Bitmap;
        UpdateClip();
        OnPlaced(bounds);
    }

    protected override void OnPlaced(Win32.RECT bounds)
    {
        if (_grab is null)
        {
            return;
        }

        // La imagen está fijada a coordenadas de PANTALLA: al mover la ventana se desplaza en sentido contrario
        // para que el desenfoque siga alineado con lo que hay debajo.
        var scale = 96.0 / Dpi;
        var hostLeft = bounds.Left + Inset * Dpi / 96.0;
        var hostTop = bounds.Top + Inset * Dpi / 96.0;
        Canvas.SetLeft(_image, (_grabArea.Left - hostLeft) * scale);
        Canvas.SetTop(_image, (_grabArea.Top - hostTop) * scale);
    }

    protected override void OnHidden()
    {
        _image.Source = null;
        _grab = null;
    }

    private void UpdateClip()
    {
        var diameter = Math.Max(1, ActualWidth - 2 * Inset);
        var radius = diameter / 2;
        _host.Clip = new EllipseGeometry(new Point(radius, radius), radius, radius);
        _tint.Width = _ring.Width = diameter;
        _tint.Height = _ring.Height = diameter;
    }
}
