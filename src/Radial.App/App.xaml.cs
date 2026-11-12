using System.Windows;
using Radial.App.Overlay;
using Radial.App.Runtime;
using Radial.App.Services;
using Radial.Core.Diagnostics;
using Radial.Core.Editing;
using Radial.Core.Storage;
using Radial.Windows.Presentation;
using Radial.Windows.Runtime;
using Radial.Windows.Shortcuts;
using Radial.Windows.Storage;
using System.Security.Principal;

namespace Radial.App;

public partial class App : Application
{
    private ErrorSystem? _errors;
    private SingleInstance? _singleInstance;

    // Uso:
    //   Radial.App.exe                        centro de control (editor radial, biblioteca de acciones, ajustes y diagnóstico);
    //                                         la captura empieza sola
    //   Radial.App.exe --selftest <archivo>   inicia la captura, ejecuta la autoprueba con entrada sintética,
    //                                         escribe el resultado en <archivo> y cierra (sin pulsar ningún botón)
    //   Radial.App.exe --render-preview <png> dibuja el radial en un PNG y cierra (herramienta de desarrollo: sin ventana
    //                                         de overlay, sin hooks, sin entrada)
    //   Radial.App.exe --render-ui <carpeta>  dibuja el centro de control (editor, biblioteca, ajustes, diálogo) en PNG y cierra
    //                                         (herramienta de desarrollo: sin hooks, sin entrada, sin tocar el disco)
    //   Radial.App.exe --edge-check <txt>     recorre la ruta real del radial (coordinador + ventana) en los bordes y esquinas de cada
    //                                         monitor y escribe lo que mide Windows (sin hooks, sin entrada, sin mover el cursor)
    //   Radial.App.exe --overlay-check <txt> [--at x,y]  muestra el overlay real un instante sobre el cursor (o en x,y), escribe los estilos de la
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

        var edgeCheck = ValueOf(e.Args, "--edge-check");
        if (edgeCheck is not null)
        {
            _ = EdgeCheck.RunAsync(edgeCheck);
            return;
        }

        var check = ValueOf(e.Args, "--overlay-check");
        if (check is not null)
        {
            _ = OverlayCheck.RunAsync(check, ParsePoint(ValueOf(e.Args, "--at")), int.TryParse(ValueOf(e.Args, "--hold"), out var hold) ? Math.Clamp(hold, 0, 30_000) : 0);
            return;
        }

        // Las herramientas de desarrollo de arriba y la autoprueba deben poder coexistir con la aplicación normal: no instalan una
        // instancia residente ni deben traerla al frente. El uso real sí es siempre de una sola instancia por usuario/sesión.
        var selfTest = ValueOf(e.Args, "--selftest");
        if (selfTest is null)
        {
            _singleInstance = SingleInstance.TryAcquire(CurrentUserScope());
            if (!_singleInstance.IsPrimary)
            {
                var activated = _singleInstance.ActivatePrimaryAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown(activated ? 0 : 1);
                return;
            }
        }

        // Lo PRIMERO, antes de cualquier componente que pueda fallar: el registro persistente y la captura global de errores
        // (documento técnico, sección 20). Las herramientas de desarrollo de arriba no lo usan: no deben tocar el disco.
        var errors = new ErrorSystem(ErrorSystem.DefaultLogDirectory, this);
        _errors = errors;
        errors.Install();
        try
        {
            StartApplication(e.Args, errors, startHidden: selfTest is null && Array.IndexOf(e.Args, "--background") >= 0);
        }
        catch (Exception exception)
        {
            // Un fallo de arranque no deja un Radial a medias: se registra, queda el marcador de cierre grave y se termina (el
            // reportero pide el cierre ordenado; Shutdown(1) es la red de seguridad si no hubiera quien lo atendiera).
            errors.Reporter.Report(exception, ErrorOrigin.Startup, nameof(App), "arranque de Radial");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            base.OnExit(e);
        }
        finally
        {
            // Lo último: se vacía el registro para no perder las entradas del cierre.
            _errors?.Log.Info(nameof(App), $"Radial termina (código {e.ApplicationExitCode}).", "cierre");
            _errors?.Dispose();
            _singleInstance?.Dispose();
        }
    }

    private void StartApplication(string[] args, ErrorSystem errors, bool startHidden)
    {
        var selfTest = ValueOf(args, "--selftest");
        var assembly = typeof(App).Assembly;
        var buildLine = BuildIdentity.Describe(BuildIdentity.StampOf(assembly), BuildIdentity.DirectoryOf(assembly));
        errors.Log.Info(nameof(App), (selfTest is null ? "Radial arranca. " : "Radial arranca en modo de autoprueba. ") + buildLine, "arranque");

        var (session, notices) = selfTest is null ? LoadSession(errors) : (InMemorySession(), []);

        // El anfitrión de la captura (hooks, overlay, acciones) y el modelo del centro de control comparten la sesión de edición:
        // lo que se guarda en la interfaz es lo que el radial usa a partir de ese momento.
        CaptureHost host;
        using (errors.Trail.Begin("arranque: crear la captura y el overlay"))
        {
            host = new CaptureHost(session.Current, selfTestMode: selfTest is not null, errors.Reporter, errors.Trail);
        }

        errors.SetEmergencyShutdown(() => host.Shutdown());
        host.Log("[compilación] " + buildLine);
        host.Log($"[registro] Los registros de errores se guardan en {errors.LogDirectory}");
        foreach (var notice in notices)
        {
            host.Log($"[config] {notice.Severity}: {notice.Message}");
        }

        var dialogs = new InAppDialogs();
        var services = new UiServices(dialogs, new WpfFilePicker(), dialogs, new Win32KeyLabelProvider())
        {
            Factory = () => FactoryLibrary.Create(),
            Startup = new StartupRegistration(errors.Log),
        };

        // Un fallo inesperado dentro de un comando asíncrono de la interfaz se registra y se avisa (con un texto para personas, no
        // la excepción); nunca se pierde en silencio.
        AsyncCommand.Failed += exception =>
        {
            host.Log("[interfaz] ERROR: " + exception.GetType().Name);
            errors.Reporter.Report(exception, ErrorOrigin.Action, "Interfaz", "comando asíncrono de la interfaz");
        };
        var shell = new ShellViewModel(session, services, dialogs, new DiagnosticsPage(host, selfTest));
        if (selfTest is not null)
        {
            shell.Go(AppSection.Diagnostics);
        }

        session.Saved += host.ApplyConfiguration;
        var window = new MainWindow(host, shell, trayEnabled: selfTest is null, errors, startHidden);
        _singleInstance?.Start(() => window.Dispatcher.BeginInvoke(window.ActivateFromSecondInstance));
        window.Show();
        errors.Log.Info(nameof(App), "Radial listo.", "arranque");
    }

    private static string CurrentUserScope()
    {
        try
        {
            // El SID hace único el mutex y la tubería incluso si dos cuentas locales tienen el mismo nombre de usuario.
            return WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        }
        catch (PlatformNotSupportedException)
        {
            return Environment.UserName;
        }
    }

    /// <summary>
    /// Carga la configuración de <c>%LOCALAPPDATA%\Radial\config.json</c> (la crea en el primer inicio, recupera una copia si está
    /// dañada) y abre la sesión de edición sobre ella. Un problema de configuración nunca tumba la aplicación: ante cualquier fallo
    /// inesperado se usan los valores predeterminados en memoria (sin poder guardar) y se avisa. Los fallos de disco y los avisos de
    /// la carga quedan en el registro persistente; al usuario se le dicen en el banner de la ventana (<see cref="ShellViewModel"/>).
    /// </summary>
    private static (ConfigurationSession Session, IReadOnlyList<SettingsNotice> Notices) LoadSession(ErrorSystem errors)
    {
        using var operation = errors.Trail.Begin("arranque: cargar la configuración");
        try
        {
            var store = new FileSettingsStore(FileSettingsStore.DefaultRootDirectory, log: errors.Log);
            var result = store.LoadOrCreate();
            var notices = new List<SettingsNotice>
            {
                new(SettingsNoticeSeverity.Info, $"Configuración: {result.Source} ({store.ConfigPath})."),
            };
            notices.AddRange(result.Notices);
            errors.Log.Info(nameof(App), $"Configuración cargada: {result.Source}; se puede guardar: {result.CanSave}.", "arranque");
            foreach (var notice in result.Notices.Where(n => n.Severity != SettingsNoticeSeverity.Info))
            {
                errors.Log.Write(
                    notice.Severity == SettingsNoticeSeverity.Error ? LogLevel.Error : LogLevel.Warning,
                    nameof(App),
                    notice.Message,
                    operation: "cargar la configuración");
            }

            return (new ConfigurationSession(store, result), notices);
        }
        catch (Exception exception)
        {
            // Solo al registro: el banner de la ventana ya le dice al usuario (con el aviso de abajo) que no se podrá guardar.
            errors.Log.Write(LogLevel.Error, nameof(App), "No se pudo cargar la configuración; se usan los valores predeterminados en memoria.", exception, "cargar la configuración");
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

    // «x,y» en píxeles físicos (solo para --overlay-check); cualquier otra cosa se ignora.
    private static Radial.Windows.Input.ScreenPoint? ParsePoint(string? text)
    {
        var parts = text?.Split(',');
        return parts is { Length: 2 } && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y)
            ? new Radial.Windows.Input.ScreenPoint(x, y)
            : null;
    }

    private static string? ValueOf(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
