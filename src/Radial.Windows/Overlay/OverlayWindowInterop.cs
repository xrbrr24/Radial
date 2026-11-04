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
        var bounds = OverlayPlacement.Centered(center, sizeDip, dpi);
        SetBounds(hwnd, bounds);

        return new OverlayPlacementResult(dpi, bounds, GetActualBounds(hwnd, bounds));
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
