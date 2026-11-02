using Microsoft.Win32;
using Radial.Windows.Input;

namespace Radial.App;

/// <summary>
/// Bloqueo de sesión y suspensión sobre <see cref="SystemEvents"/> (API documentada del framework de escritorio).
/// <c>SystemEvents</c> crea internamente su propia ventana oculta y su hilo; se suscribe al iniciar la captura y se
/// desuscribe al detenerla.
/// </summary>
internal sealed class SystemEventsAdapter : ISystemStateEvents
{
    public event Action? SessionLocked;

    public event Action? SessionUnlocked;

    public event Action? Suspending;

    public event Action? Resumed;

    public void Attach()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Detach()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                SessionLocked?.Invoke();
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                SessionUnlocked?.Invoke();
                break;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            Suspending?.Invoke();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            Resumed?.Invoke();
        }
    }
}
