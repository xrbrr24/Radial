using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Radial.App.Runtime;
using Radial.App.Tray;
using Radial.Core.Diagnostics;
using Radial.Windows.Native;
using Radial.Windows.Presentation;
using Radial.Windows.Runtime;

namespace Radial.App;

/// <summary>
/// La ventana del centro de control. Solo la aloja y gestiona su ciclo de vida: la X la oculta (Radial sigue en la bandeja), «Salir de
/// Radial» la cierra de verdad —preguntando antes si hay cambios sin guardar— y el cierre ordenado de la captura lo hace
/// <see cref="CaptureHost"/>. Todo lo demás vive en los modelos de vista y en las vistas.
/// </summary>
public partial class MainWindow : Window
{
    private readonly CaptureHost _host;
    private readonly ShellViewModel _shell;
    private readonly ErrorSystem _errors;
    private readonly TrayIcon? _tray;
    private readonly bool _startHidden;
    private bool _exiting;
    private bool _exitConfirmed;
    private bool _sessionEnding;
    private bool _hiddenHintShown;
    private bool _shutdownDone;

    internal MainWindow(CaptureHost host, ShellViewModel shell, bool trayEnabled, ErrorSystem errors, bool startHidden = false)
    {
        _host = host;
        _shell = shell;
        _errors = errors;
        _startHidden = startHidden;
        InitializeComponent();
        DataContext = shell;

        // La compilación que se está ejecutando, visible: una instancia antigua en la bandeja hace que abrir otra compilación solo la traiga
        // al frente (instancia única), y lo que se ve parece un fallo del código nuevo.
        var buildStamp = BuildIdentity.StampOf(typeof(MainWindow).Assembly);
        Title = BuildIdentity.WindowTitle(buildStamp);

        // Sin supervisión la ventana debe estar siempre encima: la autoprueba no inyecta nada si otra ventana tapa la zona de prueba.
        Topmost = host.SelfTestMode;

        // Los fallos de las acciones y de la captura los comunica el sistema de errores (CaptureHost → ErrorReporter): dentro de esta
        // ventana si el usuario la está mirando y, si no, en la bandeja. Nunca con un cuadro de mensaje: robaría el foco a la
        // aplicación que recibe el atajo. Aquí solo se conectan sus dos canales.
        errors.Presence.Attach(this);
        errors.InAppSink.Attach(shell, shell.Dialogs);
        errors.SetEmergencyShutdown(EmergencyShutdown);
        host.CloseContinues += Close;

        // Bandeja del sistema (solo en uso normal): mostrar la ventana, abrir los registros y salir. Al cerrar la sesión de Windows se
        // permite cerrar de verdad: una ventana que se niega a cerrarse bloquearía el apagado.
        if (trayEnabled)
        {
            _tray = new TrayIcon(ShowFromTray, OpenLogFolder, RequestExit, BuildIdentity.TrayText(buildStamp));
            _tray.BalloonClicked += ShowDiagnostics;
            errors.TraySink.Attach(_tray);
            Application.Current.SessionEnding += (_, _) =>
            {
                _exiting = true;
                _sessionEnding = true;
            };
        }

        Closing += OnWindowClosing;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            host.StartReader();
            if (!host.SelfTestMode)
            {
                // La configuración ya está cargada y el lector en marcha: la captura empieza sola.
                host.StartCapture();
            }

            // Ya hay ventana y bandeja: se entregan los avisos que esperaban (arranque) y se anuncia un cierre grave anterior.
            errors.AnnouncePreviousFatal();
            errors.Router.DeliverPending();

            if (_startHidden && _tray is not null)
            {
                Hide();
            }
        };
    }

    /// <summary>Barra de título oscura con los colores del tema (atributos documentados de DWM; no es ningún material ni desenfoque).</summary>
    private void OnSourceInitialized(object? sender, EventArgs e) =>
        WindowStyling.UseDarkTitleBar(new WindowInteropHelper(this).Handle, 0x0E, 0x10, 0x14, 0xE7, 0xEA, 0xEF);

    /// <summary>Abre la carpeta de registros en el Explorador (opción de la bandeja). Un fallo se registra y se avisa; no tumba nada.</summary>
    private void OpenLogFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_errors.LogDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_errors.LogDirectory) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception)
        {
            _errors.Reporter.Report(exception, ErrorOrigin.Action, nameof(MainWindow), "abrir la carpeta de registros", "No se pudo abrir la carpeta de registros.");
        }
    }

    /// <summary>Pulsar un globo de aviso lleva al usuario a Diagnóstico, donde está lo que Radial sabe del fallo.</summary>
    private void ShowDiagnostics()
    {
        ShowFromTray();
        _shell.Go(AppSection.Diagnostics);
    }

    /// <summary>
    /// Cierre ordenado por un error grave (lo pide el sistema de errores): se suelta la captura y la bandeja YA, sin preguntar por
    /// cambios sin guardar (no hay un estado de confianza donde preguntar) y sin pasar por «ocultar a la bandeja».
    /// </summary>
    private void EmergencyShutdown()
    {
        _exiting = true;
        _exitConfirmed = true;
        ShutdownOnce();
    }

    /// <summary>La captura, el overlay y la bandeja se cierran una sola vez, aunque el cierre normal y el de emergencia coincidan.</summary>
    private void ShutdownOnce()
    {
        if (_shutdownDone)
        {
            return;
        }

        _shutdownDone = true;
        try
        {
            _errors.TraySink.Detach();
            _host.Shutdown(() => _tray?.Dispose());
        }
        catch (Exception exception)
        {
            // Un fallo al cerrar no puede impedir cerrar: se registra y la aplicación termina igualmente.
            _errors.Reporter.Report(exception, ErrorOrigin.Shutdown, nameof(MainWindow), "cierre ordenado de la captura");
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>
    /// Una segunda ejecución de Radial pidió activar la primera instancia. Llega al dispatcher de esta ventana desde la tubería
    /// local; reutiliza exactamente el flujo de «Mostrar Radial» de la bandeja y no crea otra captura ni otra sesión.
    /// </summary>
    internal void ActivateFromSecondInstance() => ShowFromTray();

    /// <summary>Cierra Radial de verdad (la opción «Salir» de la bandeja). Si hay una autoprueba en curso, se cancela y el cierre se completa al terminar.</summary>
    private void RequestExit()
    {
        _exiting = true;
        Close();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        // Cerrar durante la autoprueba: se aplaza el cierre, se cancela la prueba y su bloque final libera lo pulsado.
        // Cuando termina, el anfitrión vuelve a pedir el cierre.
        if (_host.DeferCloseIfSelfTestRunning())
        {
            e.Cancel = true;
            return;
        }

        // Con la bandeja, la X de la ventana la oculta: Radial sigue en segundo plano y se cierra con «Salir» del icono.
        // Los cambios sin guardar siguen en memoria mientras la ventana está oculta.
        if (!_exiting && _tray is not null)
        {
            e.Cancel = true;

            // Lo escrito y no confirmado entra en la sesión antes de ocultar: cuenta como cambio sin guardar y no depende del foco.
            // Si la configuración lo rechaza, la ventana NO se oculta: el usuario debe ver el aviso y poder corregirlo.
            if (!_shell.CommitPending())
            {
                return;
            }

            Hide();
            if (!_hiddenHintShown)
            {
                _hiddenHintShown = true;
                _host.Log("Radial sigue en la bandeja del sistema. Para cerrarlo del todo, usa «Salir de Radial» en el menú del icono.");
            }

            return;
        }

        // Salir con cambios sin guardar: se pregunta (guardar, descartar o seguir editando). No se pregunta al cerrar la sesión de Windows.
        if (_tray is not null && !_exitConfirmed && !_sessionEnding && _shell.IsDirty)
        {
            e.Cancel = true;
            _ = _errors.Reporter.RunGuardedAsync(ConfirmExitAsync, ErrorOrigin.Action, nameof(MainWindow), "salir de Radial");
            return;
        }

        ShutdownOnce();
    }

    private async Task ConfirmExitAsync()
    {
        // La ventana debe verse para que el usuario pueda responder.
        ShowFromTray();
        if (await _shell.ConfirmCloseAsync())
        {
            _exitConfirmed = true;
            Close();
        }
        else
        {
            // Se sigue editando: la ventana se queda abierta y Radial no sale.
            _exiting = false;
        }
    }
}
