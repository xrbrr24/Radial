using System.Runtime.InteropServices;

namespace Radial.Windows.Native;

/// <summary>
/// P/Invoke de la ventana del overlay: estilos extendidos, colocación en píxeles físicos y DPI. Aparte de
/// <see cref="NativeMethods"/> (captura global) para no mezclar ni tocar lo que ya está validado. Solo Windows x64.
/// </summary>
internal static partial class WindowNativeMethods
{
    internal const int GWL_EXSTYLE = -20;

    internal const long WS_EX_TRANSPARENT = 0x00000020;
    internal const long WS_EX_TOOLWINDOW = 0x00000080;
    internal const long WS_EX_LAYERED = 0x00080000;
    internal const long WS_EX_NOACTIVATE = 0x08000000;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_FRAMECHANGED = 0x0020;

    internal const int WM_MOUSEACTIVATE = 0x0021;
    internal const int MA_NOACTIVATE = 3;

    internal static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>Monitor que contiene el punto o, si no hay ninguno, el más cercano (nunca devuelve nulo con MONITOR_DEFAULTTONEAREST salvo fallo).</summary>
    [LibraryImport("user32.dll")]
    internal static partial IntPtr MonitorFromPoint(MonitorPoint pt, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    // Control de ventanas externas (acción «controlar ventana»). Todas son consultas sin bloqueo salvo PostMessage, que solo encola.
    internal const int GWL_STYLE = -16;
    internal const long WS_MAXIMIZEBOX = 0x00010000;
    internal const long WS_MINIMIZEBOX = 0x00020000;
    internal const uint WM_SYSCOMMAND = 0x0112;
    internal const int SC_CLOSE = 0xF060;
    internal const int SC_MAXIMIZE = 0xF030;
    internal const int SC_MINIMIZE = 0xF020;
    internal const int SC_RESTORE = 0xF120;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsZoomed(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial int GetClassName(IntPtr hWnd, [Out] char[] className, int maxCount);

    /// <summary>Encola un mensaje en la ventana y vuelve (no espera a que la ventana lo atienda: una ventana colgada no bloquea a Radial).</summary>
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>Rectángulo visible de la ventana según DWM (sin sombras ni bordes invisibles de redimensionado).</summary>
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
}
