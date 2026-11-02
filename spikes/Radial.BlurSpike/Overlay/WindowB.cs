using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Radial.BlurSpike.Native;

namespace Radial.BlurSpike.Overlay;

/// <summary>
/// VARIANTE B — material del sistema con DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE).
/// Sin AllowsTransparency (no es una ventana layered). Se prueba DWMSBT_TRANSIENTWINDOW (Acrylic). Mica
/// (DWMSBT_MAINWINDOW) está disponible como opción de comparación, sin que se haya probado en run1.
/// La forma es rectangular (esquinas redondeadas por DWM): la variante C añade la región circular.
/// Resultado observado en run1: relleno gris uniforme, sin blur apreciable; la causa NO se determinó.
/// </summary>
internal class WindowB : OverlayWindowBase
{
    private readonly Rectangle _tint = new();
    private string _dwmLog = string.Empty;

    /// <param name="useWindowChrome">
    /// NO VALIDADO: añadido tras run1; compila, nunca se ha ejecutado (variante "B2").
    /// Experimento: con WindowChrome (marco de cristal sin barra de título) el estilo de la ventana es el que
    /// usan las bibliotecas que consiguen Mica/Acrylic en WPF, en vez de un popup desnudo.
    /// </param>
    public WindowB(bool useWindowChrome = false)
        : base(allowsTransparency: false)
    {
        if (useWindowChrome)
        {
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new System.Windows.Thickness(0),
                GlassFrameThickness = new System.Windows.Thickness(-1),
                UseAeroCaptionButtons = false,
            });
        }

        Root.Children.Add(_tint);
        AddDiagnosticContent();
    }

    protected override void OnHandleReady()
    {
        // WPF debe pintar con alfa sobre el material: fondo de la superficie transparente + marco de cristal.
        HwndSource.FromHwnd(Hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
        var margins = new Win32.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var extend = Win32.DwmExtendFrameIntoClientArea(Hwnd, ref margins);
        var dark = Win32.SetDwmInt(Hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        var corner = Win32.SetDwmInt(Hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, 2);
        var border = Win32.SetDwmInt(Hwnd, Win32.DWMWA_BORDER_COLOR, unchecked((int)0xFFFFFFFE)); // DWMWA_COLOR_NONE
        _dwmLog = $"ExtendFrame=0x{extend:X}; DarkMode=0x{dark:X}; Corner=0x{corner:X}; BorderNone=0x{border:X}";
    }

    protected override void ApplyRequest(PresentRequest request)
    {
        _tint.Fill = Tint(request.Opacity);
        var backdrop = Win32.SetDwmInt(Hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE, (int)request.Backdrop);
        Notes = $"DWMWA_SYSTEMBACKDROP_TYPE={(int)request.Backdrop} ({request.Backdrop}) -> HRESULT 0x{backdrop:X}; {_dwmLog}";
    }
}
