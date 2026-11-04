using System.Windows;
using Radial.App.Overlay;

namespace Radial.App;

public partial class App : Application
{
    // Uso:
    //   Radial.App.exe                        ventana de diagnóstico y radial (inicia la captura con el botón)
    //   Radial.App.exe --selftest <archivo>   inicia la captura, ejecuta la autoprueba con entrada sintética,
    //                                         escribe el resultado en <archivo> y cierra (sin pulsar ningún botón)
    //   Radial.App.exe --render-preview <png> dibuja el radial en un PNG y cierra (herramienta de desarrollo: sin ventana
    //                                         de overlay, sin hooks, sin entrada)
    //   Radial.App.exe --overlay-check <txt>  muestra el overlay real un instante sobre el cursor, escribe los estilos de la
    //                                         ventana, el centrado y el foco, y cierra (sin hooks, sin entrada, sin clics)
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var preview = ValueOf(e.Args, "--render-preview");
        if (preview is not null)
        {
            try
            {
                PreviewRenderer.Render(preview);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                System.IO.File.WriteAllText(preview + ".error.txt", exception.ToString());
                Shutdown(1);
            }

            return;
        }

        var check = ValueOf(e.Args, "--overlay-check");
        if (check is not null)
        {
            _ = OverlayCheck.RunAsync(check);
            return;
        }

        new MainWindow(ValueOf(e.Args, "--selftest")).Show();
    }

    private static string? ValueOf(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
