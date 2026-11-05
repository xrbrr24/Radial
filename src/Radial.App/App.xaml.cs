using System.Windows;
using Radial.App.Overlay;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Windows.Storage;

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

        var selfTest = ValueOf(e.Args, "--selftest");
        var (configuration, notices) = selfTest is null ? LoadConfiguration() : (DefaultConfiguration.Create(), []);
        new MainWindow(configuration, notices, selfTest).Show();
    }

    /// <summary>
    /// Carga la configuración de <c>%LOCALAPPDATA%\Radial\config.json</c> (la crea en el primer inicio, recupera una copia si está
    /// dañada). Un problema de configuración nunca tumba la aplicación: ante cualquier fallo inesperado se usan los valores
    /// predeterminados en memoria y se avisa en el registro. La autoprueba no toca el disco: usa la predeterminada en memoria.
    /// </summary>
    private static (RadialConfiguration Configuration, IReadOnlyList<SettingsNotice> Notices) LoadConfiguration()
    {
        try
        {
            var store = new FileSettingsStore(FileSettingsStore.DefaultRootDirectory);
            var result = store.LoadOrCreate();
            var notices = new List<SettingsNotice>
            {
                new(SettingsNoticeSeverity.Info, $"Configuración: {result.Source} ({store.ConfigPath})."),
            };
            notices.AddRange(result.Notices);
            return (result.Configuration, notices);
        }
        catch (Exception exception)
        {
            return (
                DefaultConfiguration.Create(),
                [new SettingsNotice(SettingsNoticeSeverity.Error, $"No se pudo cargar la configuración ({exception.GetType().Name}); se usan los valores predeterminados en memoria.")]);
        }
    }

    private static string? ValueOf(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
