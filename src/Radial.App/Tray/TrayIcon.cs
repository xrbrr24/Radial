using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Radial.App.Tray;

/// <summary>
/// El icono de Radial en la bandeja del sistema, con tres opciones: mostrar el centro de control, abrir la carpeta de registros y
/// salir. Es solo interfaz (la decisión de qué hacer con cada opción es de quien lo crea). También es el canal de aviso con la ventana
/// oculta: <see cref="ShowBalloon"/> muestra un globo (en Windows 11, una notificación) y <see cref="BalloonClicked"/> avisa de que el
/// usuario lo ha pulsado. Usa <c>NotifyIcon</c> de Windows Forms, que forma parte del entorno de ejecución de escritorio de .NET (sin
/// paquetes adicionales) y funciona con el bucle de mensajes de WPF. El icono se dibuja en tiempo de ejecución (un anillo con un punto
/// central con el acento del radial): no hay archivo de icono todavía (fase 7).
/// El identificador del icono nativo no se libera: es uno solo durante toda la vida del proceso. Se usa desde el hilo de la interfaz.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    // Límites de NotifyIcon.ShowBalloonTip: 63 caracteres el título y 255 el texto.
    private const int MaxTitle = 63;
    private const int MaxText = 255;

    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly Drawing.Icon _image;
    private bool _disposed;

    public TrayIcon(Action showWindow, Action openLogs, Action exit, string? toolTip = null)
    {
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(openLogs);
        ArgumentNullException.ThrowIfNull(exit);

        _image = CreateImage();
        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.Add("Mostrar Radial", null, (_, _) => showWindow());
        _menu.Items.Add("Abrir carpeta de registros", null, (_, _) => openLogs());
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Salir de Radial", null, (_, _) => exit());

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _image,
            Text = toolTip ?? "Radial",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => showWindow();
        _notifyIcon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke();
    }

    /// <summary>El usuario pulsó un globo de aviso.</summary>
    public event Action? BalloonClicked;

    /// <summary>
    /// Muestra un aviso en la bandeja. No bloquea. <paramref name="isError"/> elige el icono (error o advertencia). Los textos se
    /// recortan a los límites de Windows.
    /// </summary>
    public void ShowBalloon(string title, string text, bool isError)
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.ShowBalloonTip(
            10_000,
            Truncate(title, MaxTitle),
            Truncate(text, MaxText),
            isError ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Warning);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Visible = false primero: si no, el icono puede quedarse en la bandeja hasta que el ratón pase por encima.
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");

    private static Drawing.Icon CreateImage()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Drawing.Color.Transparent);
            using var ring = new Drawing.Pen(Drawing.Color.FromArgb(0x5B, 0xB8, 0xF0), 3f);
            graphics.DrawEllipse(ring, 4, 4, 24, 24);
            using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x9A, 0xD8, 0xFF));
            graphics.FillEllipse(dot, 12, 12, 8, 8);
        }

        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
}
