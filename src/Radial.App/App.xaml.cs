using System.Windows;
using Radial.App.Overlay;
using Radial.App.Runtime;
using Radial.App.Services;
using Radial.Core.Editing;
using Radial.Core.Storage;
using Radial.Windows.Presentation;
using Radial.Windows.Shortcuts;
using Radial.Windows.Storage;

namespace Radial.App;

public partial class App : Application
{
    // Uso:
    //   Radial.App.exe                        centro de control (editor radial, biblioteca de acciones, ajustes y diagnóstico);
    //                                         la captura empieza sola
    //   Radial.App.exe --selftest <archivo>   inicia la captura, ejecuta la autoprueba con entrada sintética,
    //                                         escribe el resultado en <archivo> y cierra (sin pulsar ningún botón)
    //   Radial.App.exe --render-preview <png> dibuja el radial en un PNG y cierra (herramienta de desarrollo: sin ventana
    //                                         de overlay, sin hooks, sin entrada)
    //   Radial.App.exe --render-ui <carpeta>  dibuja el centro de control (editor, biblioteca, ajustes, diálogo) en PNG y cierra
    //                                         (herramienta de desarrollo: sin hooks, sin entrada, sin tocar el disco)
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

        var renderUi = ValueOf(e.Args, "--render-ui");
        if (renderUi is not null)
        {
            try
            {
                UiPreviewRenderer.Render(renderUi);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                System.IO.Directory.CreateDirectory(renderUi);
                System.IO.File.WriteAllText(System.IO.Path.Combine(renderUi, "error.txt"), exception.ToString());
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
        var (session, notices) = selfTest is null ? LoadSession() : (InMemorySession(), []);

        // El anfitrión de la captura (hooks, overlay, acciones) y el modelo del centro de control comparten la sesión de edición:
        // lo que se guarda en la interfaz es lo que el radial usa a partir de ese momento.
        var host = new CaptureHost(session.Current, selfTestMode: selfTest is not null);
        foreach (var notice in notices)
        {
            host.Log($"[config] {notice.Severity}: {notice.Message}");
        }

        var dialogs = new InAppDialogs();
        var services = new UiServices(dialogs, new WpfFilePicker(), dialogs, new Win32KeyLabelProvider())
        {
            Factory = () => FactoryLibrary.Create(),
        };
        var shell = new ShellViewModel(session, services, dialogs, new DiagnosticsPage(host, selfTest));
        if (selfTest is not null)
        {
            shell.Go(AppSection.Diagnostics);
        }

        session.Saved += host.ApplyConfiguration;
        new MainWindow(host, shell, trayEnabled: selfTest is null).Show();
    }

    /// <summary>
    /// Carga la configuración de <c>%LOCALAPPDATA%\Radial\config.json</c> (la crea en el primer inicio, recupera una copia si está
    /// dañada) y abre la sesión de edición sobre ella. Un problema de configuración nunca tumba la aplicación: ante cualquier fallo
    /// inesperado se usan los valores predeterminados en memoria (sin poder guardar) y se avisa.
    /// </summary>
    private static (ConfigurationSession Session, IReadOnlyList<SettingsNotice> Notices) LoadSession()
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
            return (new ConfigurationSession(store, result), notices);
        }
        catch (Exception exception)
        {
            var configuration = DefaultConfiguration.Create();
            var notice = new SettingsNotice(SettingsNoticeSeverity.Error, $"No se pudo cargar la configuración ({exception.GetType().Name}); se usan los valores predeterminados en memoria y no se podrá guardar.");
            var result = new SettingsLoadResult(configuration, SettingsSource.DefaultsNotPersisted, [notice], CanSave: false);
            return (new ConfigurationSession(new InMemorySettingsStore(configuration), result), [notice]);
        }
    }

    /// <summary>La sesión de la autoprueba: la configuración predeterminada en memoria; no toca el disco.</summary>
    private static ConfigurationSession InMemorySession()
    {
        var store = new InMemorySettingsStore(DefaultConfiguration.Create());
        return new ConfigurationSession(store, store.LoadOrCreate());
    }

    private static string? ValueOf(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
