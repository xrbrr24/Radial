using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Radial.App.Runtime;
using Radial.App.Tray;
using Radial.Core.Execution;
using Radial.Windows.Native;
using Radial.Windows.Presentation;

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
    private readonly TrayIcon? _tray;
    private bool _exiting;
    private bool _exitConfirmed;
    private bool _sessionEnding;
    private bool _hiddenHintShown;

    internal MainWindow(CaptureHost host, ShellViewModel shell, bool trayEnabled)
    {
        _host = host;
        _shell = shell;
        InitializeComponent();
        DataContext = shell;

        // Sin supervisión la ventana debe estar siempre encima: la autoprueba no inyecta nada si otra ventana tapa la zona de prueba.
        Topmost = host.SelfTestMode;

        // Una acción que falla se comunica dentro de la ventana de Radial (si está visible) y en el registro; nunca con un cuadro de
        // mensaje: robaría el foco a la aplicación que recibe el atajo.
        host.ActionCompleted += OnActionCompleted;
        host.CloseContinues += Close;

        // Bandeja del sistema (solo en uso normal): mostrar la ventana y salir. Al cerrar la sesión de Windows se permite cerrar de
        // verdad: una ventana que se niega a cerrarse bloquearía el apagado.
        if (trayEnabled)
        {
            _tray = new TrayIcon(ShowFromTray, RequestExit);
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
        };
    }

    /// <summary>Barra de título oscura con los colores del tema (atributos documentados de DWM; no es ningún material ni desenfoque).</summary>
    private void OnSourceInitialized(object? sender, EventArgs e) =>
        WindowStyling.UseDarkTitleBar(new WindowInteropHelper(this).Handle, 0x0E, 0x10, 0x14, 0xE7, 0xEA, 0xEF);

    private void OnActionCompleted(string name, ActionExecutionResult result)
    {
        if (result.Status == ActionExecutionStatus.Failed && result.Error is { } error)
        {
            _shell.Dialogs.Show($"«{name}» no se pudo ejecutar: {error.Message}", isError: true);
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
            _ = ConfirmExitAsync();
            return;
        }

        _host.Shutdown(() => _tray?.Dispose());
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
