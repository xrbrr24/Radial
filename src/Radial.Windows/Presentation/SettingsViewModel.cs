using Radial.Core.Editing;
using Radial.Core.Model;
using Radial.Core.Validation;

namespace Radial.Windows.Presentation;

/// <summary>
/// Ajustes de navegación del radial: tiempo para entrar en un submenú, tiempo para volver atrás y profundidad máxima. Solo lo que
/// el usuario necesita; los ajustes avanzados del gesto y los temas son de otra fase. Los valores se limitan a lo que la
/// configuración admite, así que no se puede escribir un valor inválido; si el cambio no es aplicable (p. ej. bajar la profundidad
/// por debajo de la de los menús existentes) no se aplica y se explica el motivo.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ConfigurationSession _session;
    private int _submenuDwellMs;
    private int _backDwellMs;
    private int _maxDepth;
    private string _message = string.Empty;
    private bool _applying;

    public SettingsViewModel(ConfigurationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        RestoreDefaults = new RelayCommand(ResetToDefaults);
        _session.Changed += Refresh;
        Refresh();
    }

    public RelayCommand RestoreDefaults { get; }

    public int MinDwellMs { get; } = (int)ConfigurationLimits.MinDwell.TotalMilliseconds;

    private static readonly int ConfigurationLimitMs = (int)ConfigurationLimits.MaxDwell.TotalMilliseconds;

    /// <summary>El máximo que ofrece el control: 3 s o, si la configuración ya tiene un valor mayor, ese (así el control nunca recorta un valor existente).</summary>
    public int MaxDwellMs => Math.Max(3000, Math.Max(_submenuDwellMs, _backDwellMs));

    public int MaxDepthLimit { get; } = ConfigurationLimits.MaxDepthLimit;

    public int SubmenuDwellMs
    {
        get => _submenuDwellMs;
        set => ApplyNavigation(value, _backDwellMs, _maxDepth);
    }

    public int BackDwellMs
    {
        get => _backDwellMs;
        set => ApplyNavigation(_submenuDwellMs, value, _maxDepth);
    }

    public int MaxDepth
    {
        get => _maxDepth;
        set => ApplyNavigation(_submenuDwellMs, _backDwellMs, value);
    }

    /// <summary>Por qué no se aplicó el último cambio; vacío si todo fue bien.</summary>
    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public string SubmenuDwellText => $"{_submenuDwellMs} ms";

    public string BackDwellText => $"{_backDwellMs} ms";

    public string MaxDepthText => _maxDepth == 1 ? "1 nivel (sin submenús)" : $"{_maxDepth} niveles";

    private void ApplyNavigation(int submenuMs, int backMs, int depth)
    {
        if (_applying)
        {
            return;
        }

        var navigation = new NavigationSettings
        {
            SubmenuDwell = TimeSpan.FromMilliseconds(Math.Clamp(submenuMs, MinDwellMs, ConfigurationLimitMs)),
            BackDwell = TimeSpan.FromMilliseconds(Math.Clamp(backMs, MinDwellMs, ConfigurationLimitMs)),
            MaxDepth = Math.Clamp(depth, 1, MaxDepthLimit),
        };

        if (navigation == _session.Current.Navigation)
        {
            return;
        }

        _applying = true;
        try
        {
            var result = _session.Editor.SetNavigation(navigation);
            Message = result.Success ? string.Empty : result.Message;
        }
        finally
        {
            _applying = false;
        }

        // Sea cual sea el resultado, los controles vuelven a mostrar lo que hay de verdad en la configuración.
        Refresh();
    }

    private void ResetToDefaults()
    {
        var defaults = new NavigationSettings();
        _applying = true;
        try
        {
            var result = _session.Editor.SetNavigation(defaults);
            Message = result.Success ? string.Empty : result.Message;
        }
        finally
        {
            _applying = false;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_applying)
        {
            return;
        }

        var navigation = _session.Current.Navigation;
        _submenuDwellMs = (int)navigation.SubmenuDwell.TotalMilliseconds;
        _backDwellMs = (int)navigation.BackDwell.TotalMilliseconds;
        _maxDepth = navigation.MaxDepth;
        foreach (var name in new[] { nameof(SubmenuDwellMs), nameof(BackDwellMs), nameof(MaxDepth), nameof(SubmenuDwellText), nameof(BackDwellText), nameof(MaxDepthText), nameof(MaxDwellMs) })
        {
            Raise(name);
        }
    }
}
