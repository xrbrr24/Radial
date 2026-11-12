using System.Runtime.InteropServices;
using Radial.Windows.Input;
using Radial.Windows.Native;

namespace Radial.Windows.Overlay;

/// <summary>Estilos extendidos del overlay tal y como los ve Windows (para comprobar que se aplicaron, no para suponerlo).</summary>
public readonly record struct OverlayStyles(bool Layered, bool Transparent, bool NoActivate, bool ToolWindow, bool Topmost, long Raw);

public readonly record struct OverlayPlacementResult(int Dpi, OverlayBounds Requested, OverlayBounds Actual);

/// <summary>
/// La parte Win32 de la ventana del overlay, sin WPF: estilos, colocación en píxeles físicos y DPI (ADR-001, secciones 6.1 y 6.2).
/// La ventana en sí (WPF) vive en Radial.App; aquí solo están las llamadas que necesita sobre su HWND.
/// </summary>
public static class OverlayWindowInterop
{
    private const long WsExTopmost = 0x00000008;

    /// <summary>
    /// Añade <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT</c> sin quitar nada. <c>WS_EX_LAYERED</c> lo aporta WPF con
    /// <c>AllowsTransparency=true</c>; aquí no se añade a mano. <c>WS_EX_TRANSPARENT</c> es el click-through: la interacción se
    /// resuelve con los hooks, nunca con el ratón sobre la ventana, así que se aplica siempre.
    /// </summary>
    public static void ApplyOverlayStyles(IntPtr hwnd)
    {
        RequireHandle(hwnd);
        var style = WindowNativeMethods.GetWindowLongPtr(hwnd, WindowNativeMethods.GWL_EXSTYLE).ToInt64();
        style |= WindowNativeMethods.WS_EX_NOACTIVATE | WindowNativeMethods.WS_EX_TOOLWINDOW | WindowNativeMethods.WS_EX_TRANSPARENT;
        WindowNativeMethods.SetWindowLongPtr(hwnd, WindowNativeMethods.GWL_EXSTYLE, new IntPtr(style));

        // Sin esto Windows no vuelve a leer los estilos.
        WindowNativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            WindowNativeMethods.SWP_NOMOVE | WindowNativeMethods.SWP_NOSIZE | WindowNativeMethods.SWP_NOZORDER | WindowNativeMethods.SWP_NOACTIVATE | WindowNativeMethods.SWP_FRAMECHANGED);
    }

    public static OverlayStyles ReadStyles(IntPtr hwnd)
    {
        RequireHandle(hwnd);
        var style = WindowNativeMethods.GetWindowLongPtr(hwnd, WindowNativeMethods.GWL_EXSTYLE).ToInt64();
        return new OverlayStyles(
            Layered: (style & WindowNativeMethods.WS_EX_LAYERED) != 0,
            Transparent: (style & WindowNativeMethods.WS_EX_TRANSPARENT) != 0,
            NoActivate: (style & WindowNativeMethods.WS_EX_NOACTIVATE) != 0,
            ToolWindow: (style & WindowNativeMethods.WS_EX_TOOLWINDOW) != 0,
            Topmost: (style & WsExTopmost) != 0,
            Raw: style);
    }

    /// <summary>
    /// Centra un cuadrado de <paramref name="sizeDip"/> DIP en <paramref name="center"/> (píxeles físicos). Es la secuencia de la
    /// sección 6.2: (1) se coloca la ventana, mínima, sobre el cursor para que quede en el monitor correcto; (2) se lee el DPI de
    /// ESE monitor con <c>GetDpiForWindow</c>; (3) se fija el tamaño físico definitivo. Sin activar la ventana en ningún paso.
    /// </summary>
    public static OverlayPlacementResult PlaceCentered(IntPtr hwnd, ScreenPoint center, double sizeDip)
    {
        RequireHandle(hwnd);

        var probe = OverlayPlacement.Centered(center, sizeDip: 1, dpi: OverlayPlacement.BaseDpi);
        SetBounds(hwnd, probe);

        var dpi = GetDpi(hwnd);
        var bounds = FittedBounds(center, sizeDip, dpi);
        SetBounds(hwnd, bounds);

        return new OverlayPlacementResult(dpi, bounds, GetActualBounds(hwnd, bounds));
    }

    /// <summary>
    /// Mueve una ventana YA visible para que su centro quede en <paramref name="center"/> (píxeles físicos), en UN solo paso con el
    /// DPI que tenía, para que cambiar de menú no parpadee (<see cref="PlaceCentered"/> pasa antes por una ventana mínima para
    /// averiguar el monitor). Si al moverla Windows la deja en un monitor con otro DPI, se corrige el tamaño. Sin activar la ventana.
    /// </summary>
    public static OverlayPlacementResult RecenterVisible(IntPtr hwnd, ScreenPoint center, double sizeDip, int currentDpi)
    {
        RequireHandle(hwnd);

        var dpi = currentDpi > 0 ? currentDpi : OverlayPlacement.BaseDpi;
        var bounds = FittedBounds(center, sizeDip, dpi);
        SetBounds(hwnd, bounds);

        var actualDpi = GetDpi(hwnd);
        if (actualDpi != dpi)
        {
            dpi = actualDpi;
            bounds = FittedBounds(center, sizeDip, dpi);
            SetBounds(hwnd, bounds);
        }

        return new OverlayPlacementResult(dpi, bounds, GetActualBounds(hwnd, bounds));
    }

    /// <summary>
    /// Traslada una ventana YA visible a donde está el cursor, que ha pasado a otro monitor, sin ocultarla ni activarla. El cursor queda a
    /// <paramref name="offsetXDip"/>, <paramref name="offsetYDip"/> DIP del centro (el mismo desfase que tenía en el monitor anterior, para
    /// que, si cabe, el sector bajo el cursor no cambie), y la ventana se desplaza hacia dentro lo justo para caber ENTERA en el área de
    /// trabajo del monitor del CURSOR (no del centro ideal, que puede quedar en el monitor anterior). Como el DPI del monitor nuevo puede
    /// ser otro, el desfase se convierte a píxeles con el DPI que Windows dé a la ventana ya movida: se coloca con el DPI anterior, se
    /// lee el real y, si difiere, se vuelve a calcular (hasta tres veces: con cada DPI el desfase en píxeles y el tamaño son otros).
    /// </summary>
    public static OverlayPlacementResult FollowCursor(IntPtr hwnd, ScreenPoint cursor, double offsetXDip, double offsetYDip, double sizeDip, int currentDpi)
    {
        RequireHandle(hwnd);

        var dpi = currentDpi > 0 ? currentDpi : OverlayPlacement.BaseDpi;
        OverlayBounds bounds = default;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            bounds = FittedBounds(OverlayPlacement.IdealCenter(cursor, offsetXDip, offsetYDip, OverlayPlacement.Scale(dpi)), cursor, sizeDip, dpi);
            SetBounds(hwnd, bounds);

            var actualDpi = GetDpi(hwnd);
            if (actualDpi == dpi)
            {
                break;
            }

            dpi = actualDpi;
        }

        return new OverlayPlacementResult(dpi, bounds, GetActualBounds(hwnd, bounds));
    }

    /// <summary>
    /// Cuadrado de <paramref name="sizeDip"/> DIP centrado en <paramref name="center"/>, desplazado hacia dentro lo justo para caber
    /// ENTERO en el área de trabajo del monitor que contiene ese punto (<see cref="OverlayPlacement.Fit"/>). Se ajusta al área de trabajo
    /// y no al monitor completo porque una barra de tareas anclada taparía parte del radial; el cursor sigue pudiendo llegar a cualquier
    /// punto del monitor, así que los ocho sectores siguen siendo alcanzables. Si Windows no da el monitor, queda centrado sin ajustar.
    /// </summary>
    private static OverlayBounds FittedBounds(ScreenPoint center, double sizeDip, int dpi) => FittedBounds(center, center, sizeDip, dpi);

    /// <summary>Como <see cref="FittedBounds(ScreenPoint, double, int)"/>, pero el monitor lo da <paramref name="areaPoint"/> (el cursor), no el centro.</summary>
    private static OverlayBounds FittedBounds(ScreenPoint center, ScreenPoint areaPoint, double sizeDip, int dpi)
    {
        var area = GetMonitorAreas(areaPoint) is { } areas ? areas.Work : default;
        var fitted = OverlayPlacement.Fit(center, OverlayPlacement.PhysicalSize(sizeDip, dpi), area);
        return OverlayPlacement.Centered(fitted, sizeDip, dpi);
    }

    /// <summary>
    /// Área completa del monitor que contiene el punto (o del más cercano). <c>null</c> si Windows no la da. Para saber dónde puede ir
    /// el radial, <see cref="GetMonitorAreas"/>.
    /// </summary>
    public static ScreenRect? GetMonitorBounds(ScreenPoint point) => GetMonitorAreas(point)?.Bounds;

    /// <summary>
    /// Área completa y área de trabajo (sin una barra de tareas anclada) del monitor que contiene el punto o del más cercano, en píxeles
    /// físicos del escritorio virtual. El área de trabajo cae al monitor completo si Windows no da un rectángulo utilizable. <c>null</c> si
    /// Windows no da el monitor.
    /// </summary>
    public static (ScreenRect Bounds, ScreenRect Work)? GetMonitorAreas(ScreenPoint point)
    {
        var monitor = WindowNativeMethods.MonitorFromPoint(
            new WindowNativeMethods.MonitorPoint { X = point.X, Y = point.Y },
            WindowNativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return null;
        }

        var info = new WindowNativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<WindowNativeMethods.MONITORINFO>() };
        if (!WindowNativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var bounds = new ScreenRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
        if (!bounds.IsUsable)
        {
            return null;
        }

        var work = new ScreenRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
        return (bounds, work.IsUsable ? work : bounds);
    }

    /// <summary>Rectángulo exterior real de la ventana según <c>GetWindowRect</c> (píxeles físicos), o <c>null</c> si Windows no lo da.</summary>
    public static ScreenRect? GetWindowBounds(IntPtr hwnd)
    {
        RequireHandle(hwnd);
        return WindowNativeMethods.GetWindowRect(hwnd, out var rect) ? new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
    }

    /// <summary>
    /// Rectángulo VISIBLE de la ventana según DWM (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>): si difiere de <see cref="GetWindowBounds"/> hay
    /// bordes o sombras invisibles que cuentan para el tamaño pero no se ven. <c>null</c> si DWM no lo da.
    /// </summary>
    public static ScreenRect? GetVisibleFrameBounds(IntPtr hwnd)
    {
        RequireHandle(hwnd);
        var hr = WindowNativeMethods.DwmGetWindowAttribute(hwnd, WindowNativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out var rect, Marshal.SizeOf<WindowNativeMethods.RECT>());
        return hr == 0 ? new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
    }

    /// <summary>Escritorio virtual completo (todos los monitores) en píxeles físicos: puede tener origen negativo.</summary>
    public static ScreenRect GetVirtualScreen()
    {
        const int xVirtual = 76, yVirtual = 77, cxVirtual = 78, cyVirtual = 79;
        var left = WindowNativeMethods.GetSystemMetrics(xVirtual);
        var top = WindowNativeMethods.GetSystemMetrics(yVirtual);
        return new ScreenRect(left, top, left + WindowNativeMethods.GetSystemMetrics(cxVirtual), top + WindowNativeMethods.GetSystemMetrics(cyVirtual));
    }

    /// <summary>DPI de la ventana según Windows (<c>GetDpiForWindow</c>); 96 si la llamada falla.</summary>
    public static int GetDpi(IntPtr hwnd)
    {
        RequireHandle(hwnd);
        var dpi = (int)WindowNativeMethods.GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi : OverlayPlacement.BaseDpi;
    }

    /// <summary>
    /// Mensajes que la ventana debe contestar ella misma. <c>WM_MOUSEACTIVATE</c> → <c>MA_NOACTIVATE</c>: un clic sobre el overlay
    /// no lo activa. (No se usa <c>WM_NCHITTEST → HTTRANSPARENT</c>: no deja pasar el clic a otro proceso.)
    /// </summary>
    public static bool TryHandleMessage(int message, out IntPtr result)
    {
        if (message == WindowNativeMethods.WM_MOUSEACTIVATE)
        {
            result = new IntPtr(WindowNativeMethods.MA_NOACTIVATE);
            return true;
        }

        result = IntPtr.Zero;
        return false;
    }

    /// <summary>Posición actual del cursor en píxeles físicos, o <c>null</c> si Windows no la da (p. ej. escritorio protegido).</summary>
    public static ScreenPoint? GetCursorPosition() =>
        NativeMethods.GetCursorPos(out var point) ? new ScreenPoint(point.X, point.Y) : null;

    /// <summary>Ventana en primer plano ahora mismo; para comprobar que mostrar el overlay no cambia el foco.</summary>
    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    private static void SetBounds(IntPtr hwnd, OverlayBounds bounds)
    {
        // HWND_TOPMOST en cada colocación: otra ventana «siempre visible» pudo ponerse encima desde la última vez.
        if (!WindowNativeMethods.SetWindowPos(hwnd, WindowNativeMethods.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Size, bounds.Size, WindowNativeMethods.SWP_NOACTIVATE))
        {
            throw new InvalidOperationException("No se pudo colocar la ventana del overlay (error " + Marshal.GetLastWin32Error() + ").");
        }
    }

    private static OverlayBounds GetActualBounds(IntPtr hwnd, OverlayBounds fallback) =>
        WindowNativeMethods.GetWindowRect(hwnd, out var rect)
            ? new OverlayBounds(rect.Left, rect.Top, Math.Max(rect.Right - rect.Left, rect.Bottom - rect.Top))
            : fallback;

    private static void RequireHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("La ventana todavía no tiene manejador (HWND).", nameof(hwnd));
        }
    }
}
