using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Radial.BlurSpike.Native;

namespace Radial.BlurSpike.Harness;

/// <summary>
/// Ventana receptora. Se ejecuta en un PROCESO APARTE (mismo exe con --receiver): HTTRANSPARENT solo atraviesa
/// ventanas del mismo hilo, así que un receptor en el mismo proceso daría falsos positivos.
/// Muestra franjas blancas y negras (para medir el desenfoque) y publica sus contadores en el título de la ventana.
/// </summary>
internal sealed class ReceiverWindow : Window
{
    public const string TitlePrefix = "BlurSpikeReceiver";

    private readonly Ellipse _indicator = new() { Width = 24, Height = 24, Fill = Brushes.Red, Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly DispatcherTimer _fade = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly TextBlock _label = new() { Foreground = Brushes.Yellow, Background = Brushes.Black, FontFamily = new FontFamily("Consolas"), FontSize = 16, Margin = new Thickness(48, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private int _clicks;
    private int _moves;
    private readonly int _x, _y, _width, _height;

    public ReceiverWindow(int x, int y, int width, int height)
    {
        (_x, _y, _width, _height) = (x, y, width, height);
        WindowStyle = WindowStyle.SingleBorderWindow;
        ShowInTaskbar = true;
        Topmost = false;
        Width = 400;
        Height = 300;

        var tile = new DrawingGroup();
        tile.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 32, 64))));
        tile.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(32, 0, 32, 64))));
        var stripes = new DrawingBrush(tile)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 64, 64),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 64, 64),
            ViewportUnits = BrushMappingMode.Absolute,
        };

        var grid = new Grid { Background = stripes, SnapsToDevicePixels = true };
        RenderOptions.SetEdgeMode(grid, EdgeMode.Aliased);
        grid.Children.Add(_indicator);
        grid.Children.Add(_label);
        Content = grid;

        PreviewMouseDown += (_, _) => { _clicks++; Flash(); };
        PreviewMouseMove += (_, _) => { _moves++; Flash(); };
        _fade.Tick += (_, _) => { _indicator.Fill = Brushes.Red; _fade.Stop(); };
        UpdateTitle();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetWindowPos(hwnd, IntPtr.Zero, _x, _y, _width, _height, Win32.SWP_NOZORDER);
        };
    }

    private void Flash()
    {
        _indicator.Fill = Brushes.LimeGreen;
        _fade.Stop();
        _fade.Start();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        Title = $"{TitlePrefix} clicks={_clicks} moves={_moves}";
        _label.Text = $"clicks={_clicks} moves={_moves}";
    }
}
