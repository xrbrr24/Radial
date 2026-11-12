using System.Windows;
using System.Windows.Threading;
using Radial.App.Tray;
using Radial.Core.Diagnostics;
using Radial.Windows.Diagnostics;
using Radial.Windows.Presentation;

namespace Radial.App.Runtime;

/// <summary>
/// Si el usuario está mirando la ventana de Radial: visible, no minimizada y activa. Una ventana que está abierta pero detrás de otra
/// aplicación NO cuenta (el aviso dentro de ella no se vería). Los campos los actualizan los eventos de la ventana (hilo de interfaz) y
/// se leen desde cualquier hilo.
/// </summary>
internal sealed class WindowPresence : IUserPresence
{
    private volatile bool _visible;
    private volatile bool _active;
    private volatile bool _minimized;

    public bool IsLookingAtWindow => _visible && _active && !_minimized;

    /// <summary>Empieza a seguir la ventana. Hilo de interfaz.</summary>
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        void Refresh()
        {
            _visible = window.IsVisible;
            _active = window.IsActive;
            _minimized = window.WindowState == WindowState.Minimized;
        }

        window.IsVisibleChanged += (_, _) => Refresh();
        window.Activated += (_, _) => Refresh();
        window.Deactivated += (_, _) => Refresh();
        window.StateChanged += (_, _) => Refresh();
        Refresh();
    }
}

/// <summary>
/// El canal «dentro de la ventana»: un aviso breve (<see cref="UserNoticeKind.Notice"/>) o el banner persistente
/// (<see cref="UserNoticeKind.Problem"/>) del centro de control. Reutiliza los avisos y el banner que ya tiene la ventana. Llega al
/// hilo de interfaz con <c>BeginInvoke</c> (no espera: se puede llamar desde cualquier hilo) y un fallo al mostrarlo se registra sin
/// propagarse (no provoca otro aviso).
/// </summary>
internal sealed class ShellNoticeSink : INoticeSink
{
    private readonly Dispatcher _dispatcher;
    private readonly IRadialLog _log;
    private volatile ShellViewModel? _shell;
    private volatile InAppDialogs? _dialogs;

    public ShellNoticeSink(Dispatcher dispatcher, IRadialLog log)
    {
        _dispatcher = dispatcher;
        _log = log;
    }

    public bool IsAvailable => _shell is not null && !_dispatcher.HasShutdownStarted;

    public void Attach(ShellViewModel shell, InAppDialogs dialogs)
    {
        _dialogs = dialogs;
        _shell = shell;
    }

    public void Show(UserNotice notice)
    {
        var shell = _shell;
        var dialogs = _dialogs;
        if (shell is null || dialogs is null)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (notice.Kind == UserNoticeKind.Problem)
                {
                    shell.ShowProblem(notice.Message);
                }
                else
                {
                    dialogs.Show(notice.Message, isError: true);
                }
            }
            catch (Exception exception)
            {
                try
                {
                    _log.Write(LogLevel.Warning, nameof(ShellNoticeSink), "No se pudo mostrar un aviso dentro de la ventana.", exception, "avisar al usuario");
                }
                catch
                {
                    // El registro es lo último: no se propaga.
                }
            }
        });
    }
}

/// <summary>
/// El canal «bandeja»: un globo de aviso (en Windows 11, una notificación) que se ve aunque la ventana esté oculta. Llega al hilo de
/// interfaz con <c>BeginInvoke</c> (<c>NotifyIcon</c> solo se usa desde ahí) y no espera. No está disponible hasta que la ventana
/// crea el icono de la bandeja, ni cuando ya se ha liberado.
/// </summary>
internal sealed class TrayNoticeSink : INoticeSink
{
    private readonly Dispatcher _dispatcher;
    private readonly IRadialLog _log;
    private volatile TrayIcon? _tray;

    public TrayNoticeSink(Dispatcher dispatcher, IRadialLog log)
    {
        _dispatcher = dispatcher;
        _log = log;
    }

    public bool IsAvailable => _tray is not null && !_dispatcher.HasShutdownStarted;

    public void Attach(TrayIcon tray) => _tray = tray;

    /// <summary>El icono se liberó (cierre): ya no se puede avisar por aquí.</summary>
    public void Detach() => _tray = null;

    public void Show(UserNotice notice)
    {
        var tray = _tray;
        if (tray is null)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Si el icono se liberó entre tanto, no se muestra nada.
                if (ReferenceEquals(_tray, tray))
                {
                    tray.ShowBalloon(notice.Title, notice.Message, isError: notice.Kind == UserNoticeKind.Problem);
                }
            }
            catch (Exception exception)
            {
                try
                {
                    _log.Write(LogLevel.Warning, nameof(TrayNoticeSink), "No se pudo mostrar un aviso en la bandeja.", exception, "avisar al usuario");
                }
                catch
                {
                    // El registro es lo último: no se propaga.
                }
            }
        });
    }
}
