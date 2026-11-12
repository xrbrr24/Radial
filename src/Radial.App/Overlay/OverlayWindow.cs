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

    /// <summary>El sector resaltado que dibuja la vista ahora mismo (para las herramientas de comprobación).</summary>
    public RadialDirection? CurrentSelection => _view.Selection;

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

    public PresentedPlacement Show(RadialMenu menu, GeometrySettings geometry, ScreenPoint center)
    {
        Prepare();
        _center = center;

        // Se redibuja siempre: el menú pudo cambiar de contenido aunque sea el mismo objeto, y empieza sin selección ni progreso.
        _menu = menu;
        _geometry = geometry;
        _view.SetMenu(menu, geometry, depth: 1);

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

        return new PresentedPlacement(OverlayPlacement.Scale(placement.Dpi), placement.Requested.Center);
    }

    /// <summary>
    /// Cambia el menú de la ventana ya visible (entrar en un grupo o volver atrás) y la recoloca centrada en el cursor, sin ocultarla
    /// ni recrearla: se redibuja el contenido y se mueve en un solo paso (<see cref="OverlayWindowInterop.RecenterVisible"/>).
    /// </summary>
    public PresentedPlacement ShowMenu(RadialMenu menu, int depth, ScreenPoint center)
    {
        if (_geometry is null || _hwnd == IntPtr.Zero || !IsVisible)
        {
            throw new InvalidOperationException("No se puede cambiar de menú: el overlay no está visible.");
        }

        _menu = menu;
        _center = center;
        _view.SetMenu(menu, _geometry, depth);

        var dpi = LastPlacement?.Dpi ?? OverlayPlacement.BaseDpi;
        var placement = OverlayWindowInterop.RecenterVisible(_hwnd, center, RadialTheme.ViewSize, dpi);
        LastPlacement = placement;
        Diagnostic?.Invoke(
            $"menú «{menu.Name}» (nivel {depth}) en ({center.X}, {center.Y}); DPI {placement.Dpi}; " +
            $"ventana {placement.Actual.Size}x{placement.Actual.Size} px en ({placement.Actual.X}, {placement.Actual.Y})");

        return new PresentedPlacement(OverlayPlacement.Scale(placement.Dpi), placement.Requested.Center);
    }

    /// <summary>
    /// El cursor pasó a otro monitor: traslada la ventana ya visible al área de trabajo de ese monitor, con el cursor a
    /// <paramref name="offsetDip"/> DIP del centro si cabe (<see cref="OverlayWindowInterop.FollowCursor"/>). No oculta ni redibuja.
    /// </summary>
    public PresentedPlacement FollowCursor(ScreenPoint cursor, PointD offsetDip)
    {
        if (_hwnd == IntPtr.Zero || !IsVisible)
        {
            throw new InvalidOperationException("No se puede seguir al cursor: el overlay no está visible.");
        }

        var dpi = LastPlacement?.Dpi ?? OverlayPlacement.BaseDpi;
        var placement = OverlayWindowInterop.FollowCursor(_hwnd, cursor, offsetDip.X, offsetDip.Y, RadialTheme.ViewSize, dpi);
        LastPlacement = placement;

        // El centro «pedido» a partir de ahora es el centro ya ajustado, que está dentro del monitor nuevo: si Windows avisa de un cambio de
        // DPI después, recolocar desde él da el mismo monitor (desde el centro ideal, que puede caer en el monitor anterior, no).
        _center = placement.Requested.Center;
        Diagnostic?.Invoke(
            $"el cursor pasó a otro monitor ({cursor.X}, {cursor.Y}); DPI {placement.Dpi}; " +
            $"ventana {placement.Actual.Size}x{placement.Actual.Size} px en ({placement.Actual.X}, {placement.Actual.Y})");

        return new PresentedPlacement(OverlayPlacement.Scale(placement.Dpi), placement.Requested.Center);
    }

    public void SetSelection(RadialDirection? direction) => _view.SetSelection(direction);

    public void SetProgress(RadialProgress progress) => _view.SetProgress(progress);

    void IRadialPresenter.Hide()
    {
        _placed = false;
        Hide();
    }

    /// <inheritdoc />
    public event Action<PresentedPlacement>? PlacementChanged;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // Windows sugiere un rectángulo al cambiar de monitor; se sustituye por el cálculo propio cuando termina el mensaje. Ese
        // cálculo puede dar otro centro (el ajuste a los bordes depende del tamaño en píxeles, que depende del DPI) y otra escala: se
        // le dice al coordinador, que mide la selección desde el centro dibujado. Se usa el centro pedido VIGENTE al ejecutarse
        // (un cambio de menú pudo cambiarlo entre tanto).
        if (_placed)
        {
            Dispatcher.BeginInvoke(
                () =>
                {
                    if (_placed && IsVisible)
                    {
                        var placement = OverlayWindowInterop.PlaceCentered(_hwnd, _center, RadialTheme.ViewSize);
                        LastPlacement = placement;
                        PlacementChanged?.Invoke(new PresentedPlacement(OverlayPlacement.Scale(placement.Dpi), placement.Requested.Center));
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
