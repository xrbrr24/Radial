using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Radial.App.Tray;

/// <summary>
/// El icono de Radial en la bandeja del sistema, con dos opciones: mostrar el centro de control y salir. Es solo interfaz (la
/// decisión de qué hacer con cada opción es de quien lo crea). Usa <c>NotifyIcon</c> de Windows Forms, que forma parte del entorno
/// de ejecución de escritorio de .NET (sin paquetes adicionales) y funciona con el bucle de mensajes de WPF. El icono se dibuja en
/// tiempo de ejecución (un anillo con un punto central con el acento del radial): no hay archivo de icono todavía (fase 7).
/// El identificador del icono nativo no se libera: es uno solo durante toda la vida del proceso. Se usa desde el hilo de la interfaz.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly Drawing.Icon _image;
    private bool _disposed;

    public TrayIcon(Action showWindow, Action exit)
    {
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(exit);

        _image = CreateImage();
        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.Add("Mostrar Radial", null, (_, _) => showWindow());
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Salir de Radial", null, (_, _) => exit());

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _image,
            Text = "Radial",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => showWindow();
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
