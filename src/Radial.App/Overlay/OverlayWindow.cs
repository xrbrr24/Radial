using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.App.Overlay;

/// <summary>
/// La ventana del overlay (ADR-001, variante A): ventana layered de WPF, transparente, siempre encima, que no se activa y deja
/// pasar el ratón. Se crea una vez al arrancar (<see cref="Prepare"/>) y se reutiliza: mostrar y ocultar, nunca cerrar. Contiene un
/// <see cref="RadialView"/>; la parte Win32 (estilos, colocación en píxeles físicos, DPI) está en <see cref="OverlayWindowInterop"/>.
/// No lee el ratón ni el teclado: quien la usa (<see cref="RadialOverlayCoordinator"/>) le dice qué mostrar y qué resaltar.
/// </summary>
internal sealed class OverlayWindow : Window, IRadialPresenter
{
    private readonly RadialView _view = new();
    private IntPtr _hwnd;
    private RadialMenu? _menu;
    private GeometrySettings? _geometry;
    private ScreenPoint _center;
    private bool _placed;

    public OverlayWindow()
    {
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Background = Brushes.Transparent;
        UseLayoutRounding = false;
        SnapsToDevicePixels = false;
        Width = RadialTheme.ViewSize;
        Height = RadialTheme.ViewSize;
        Content = _view;
    }

    /// <summary>Se produce al mostrar el overlay, con una línea que resume cómo quedó (DPI, tamaño, estilos, foco).</summary>
    public event Action<string>? Diagnostic;

    public IntPtr Hwnd => _hwnd;

    /// <summary>Estilos extendidos reales de la ventana (después de <see cref="Prepare"/>).</summary>
    public OverlayStyles Styles => OverlayWindowInterop.ReadStyles(_hwnd);

    /// <summary>Dónde quedó la ventana la última vez que se mostró (lo que pidió y lo que Windows dejó).</summary>
    public OverlayPlacementResult? LastPlacement { get; private set; }

    /// <summary>Si la ventana en primer plano seguía siendo la misma justo después de mostrar el overlay la última vez.</summary>
    public bool? LastShowKeptForeground { get; private set; }

    /// <summary>
    /// Crea el HWND (sin mostrar nada) y le aplica los estilos del overlay. Se llama una vez al arrancar la aplicación, para que
    /// la primera aparición no pague la creación de la ventana.
    /// </summary>
    public void Prepare()
    {
        if (_hwnd != IntPtr.Zero)
        {
            return;
        }

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayWindowInterop.ApplyOverlayStyles(_hwnd);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    public double Show(RadialMenu menu, GeometrySettings geometry, ScreenPoint center)
    {
        Prepare();
        _center = center;

        if (!ReferenceEquals(menu, _menu) || !ReferenceEquals(geometry, _geometry))
        {
            _menu = menu;
            _geometry = geometry;
            _view.SetMenu(menu, geometry);
        }

        _view.SetSelection(null);

        var foregroundBefore = OverlayWindowInterop.GetForegroundWindow();

        // Invisible hasta tener la posición y el tamaño definitivos: WPF puede colocar la ventana por su cuenta al mostrarla
        // por primera vez, y no debe verse ese fotograma.
        Opacity = 0;
        OverlayWindowInterop.PlaceCentered(_hwnd, center, RadialTheme.ViewSize);
        if (!IsVisible)
        {
            base.Show(); // ShowActivated=false: se muestra sin activarla
        }

        var placement = OverlayWindowInterop.PlaceCentered(_hwnd, center, RadialTheme.ViewSize);
        _placed = true;
        Opacity = 1;

        var foregroundAfter = OverlayWindowInterop.GetForegroundWindow();
        LastPlacement = placement;
        LastShowKeptForeground = foregroundAfter == foregroundBefore;
        var styles = OverlayWindowInterop.ReadStyles(_hwnd);
        Diagnostic?.Invoke(
            $"overlay en ({center.X}, {center.Y}); DPI {placement.Dpi} ({OverlayPlacement.Scale(placement.Dpi) * 100:F0} %); " +
            $"ventana {placement.Actual.Size}x{placement.Actual.Size} px en ({placement.Actual.X}, {placement.Actual.Y}); " +
            $"estilos 0x{styles.Raw:X8} (layered={styles.Layered}, click-through={styles.Transparent}, sin activar={styles.NoActivate}); " +
            $"foco: {(foregroundAfter == foregroundBefore ? "sin cambios" : "CAMBIÓ")}");

        return OverlayPlacement.Scale(placement.Dpi);
    }

    public void SetSelection(RadialDirection? direction) => _view.SetSelection(direction);

    void IRadialPresenter.Hide()
    {
        _placed = false;
        Hide();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // Windows sugiere un rectángulo al cambiar de monitor; se sustituye por el cálculo propio cuando termina el mensaje.
        if (_placed)
        {
            var center = _center;
            Dispatcher.BeginInvoke(
                () =>
                {
                    if (_placed && IsVisible)
                    {
                        OverlayWindowInterop.PlaceCentered(_hwnd, center, RadialTheme.ViewSize);
                    }
                },
                DispatcherPriority.Background);
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (OverlayWindowInterop.TryHandleMessage(message, out var result))
        {
            handled = true;
            return result;
        }

        return IntPtr.Zero;
    }
}
