using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Windows.Overlay;
using Radial.Windows.Storage;

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
    private const int Panels = 5;

    public static void Render(string path)
    {
        var configuration = DefaultConfiguration.Create();
        configuration.TryGetMenu(configuration.RootMenuId, out var root);
        configuration.TryGetMenu(new MenuId(DefaultConfiguration.OpenMenuName), out var open);

        // Un menú con una acción de tipo desconocido (se conserva en la configuración pero no se puede usar), solo para verla.
        var unsupported = new RadialMenu(
            new MenuId("preview"),
            "Vista previa",
            [
                root!.Items[0],
                new RadialItem(new ItemId("preview.unknown"), "Acción futura", RadialDirection.East, new UnknownAction("futureAction", "{\"type\":\"futureAction\"}")),
                root.Items[5],
            ]);

        // Cada panel: menú, nivel, sector resaltado y progreso de las permanencias.
        (RadialMenu Menu, int Depth, RadialDirection? Selection, RadialProgress Progress)[] panels =
        [
            (root, 1, RadialDirection.NorthEast, default),
            (root, 1, RadialDirection.West, new RadialProgress(0.6, 0, false)),
            (open!, 2, RadialDirection.North, new RadialProgress(0, 0, true)),
            (unsupported, 1, RadialDirection.East, default),
            (open!, 2, null, new RadialProgress(0, 0.55, true)),
        ];

        var strip = new Grid
        {
            Width = RadialTheme.ViewSize * Panels,
            Height = RadialTheme.ViewSize,
            Background = DesktopLikeBackground(),
        };

        for (var index = 0; index < panels.Length; index++)
        {
            strip.ColumnDefinitions.Add(new ColumnDefinition());
            var view = new RadialView();
            view.SetMenu(panels[index].Menu, configuration.Geometry, panels[index].Depth);
            view.SetSelection(panels[index].Selection);
            view.SetProgress(panels[index].Progress);
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
            Width = RadialTheme.ViewSize * Panels,
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
