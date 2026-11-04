using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Radial.Core.Geometry;

namespace Radial.App.Overlay;

/// <summary>
/// Herramienta de desarrollo: dibuja el radial en un PNG (<c>Radial.App.exe --render-preview &lt;archivo.png&gt;</c>) sin ventana
/// de overlay, sin hooks y sin entrada de ningún tipo. Muestra el mismo <see cref="RadialView"/> en reposo y con dos sectores
/// resaltados sobre un fondo con detalle, para poder ver el diseño y la translucidez sin lanzar el gesto. No es el overlay real
/// (no prueba transparencia de ventana, click-through ni foco: eso se comprueba a mano, ver el documento técnico).
/// </summary>
internal static class PreviewRenderer
{
    private const double Scale = 1.5;

    public static void Render(string path)
    {
        var configuration = ProvisionalMenu.Create();
        configuration.TryGetMenu(configuration.RootMenuId, out var menu);

        RadialDirection?[] selections = [null, RadialDirection.NorthEast, RadialDirection.South];
        var strip = new Grid
        {
            Width = RadialTheme.ViewSize * selections.Length,
            Height = RadialTheme.ViewSize,
            Background = DesktopLikeBackground(),
        };

        for (var index = 0; index < selections.Length; index++)
        {
            strip.ColumnDefinitions.Add(new ColumnDefinition());
            var view = new RadialView();
            view.SetMenu(menu!, configuration.Geometry);
            view.SetSelection(selections[index]);
            Grid.SetColumn(view, index);
            strip.Children.Add(view);
        }

        strip.Measure(new Size(strip.Width, strip.Height));
        strip.Arrange(new Rect(0, 0, strip.Width, strip.Height));
        strip.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)(strip.Width * Scale), (int)(strip.Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(strip);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Un fondo claro con bloques de color y texto, para que se note la translucidez del panel.</summary>
    private static Brush DesktopLikeBackground()
    {
        var visual = new Grid
        {
            Width = RadialTheme.ViewSize * 3,
            Height = RadialTheme.ViewSize,
            Background = new LinearGradientBrush(Color.FromRgb(0xCF, 0xDD, 0xEE), Color.FromRgb(0xE9, 0xD7, 0xC4), 20),
        };

        for (var i = 0; i < 9; i++)
        {
            visual.Children.Add(new Rectangle
            {
                Width = 150,
                Height = 40,
                Fill = i % 2 == 0 ? Brushes.SteelBlue : Brushes.Goldenrod,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(40 + i % 3 * 400, 60 + i / 3 * 120, 0, 0),
            });
        }

        visual.Children.Add(new TextBlock
        {
            Text = "Texto de una aplicación debajo del radial — Lorem ipsum dolor sit amet",
            FontSize = 18,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        });

        visual.Measure(new Size(visual.Width, visual.Height));
        visual.Arrange(new Rect(0, 0, visual.Width, visual.Height));
        return new VisualBrush(visual) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
    }
}
