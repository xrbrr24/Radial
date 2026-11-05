using System.Collections.ObjectModel;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Core.Validation;

namespace Radial.Core.Engine;

/// <summary>
/// Motor de una interacción radial: selección por dirección, navegación entre submenús por permanencia y
/// resultado final. No ejecuta acciones, no conoce Windows ni la interfaz y no es seguro entre hilos:
/// quien lo use debe serializar las llamadas.
/// </summary>
public sealed class RadialEngine
{
    private readonly RadialConfiguration _configuration;
    private readonly IClock _clock;
    private readonly SectorSelector _selector;
    private readonly List<RadialMenu> _stack = new();
    private readonly DwellTracker _submenuDwell = new();
    private readonly DwellTracker _backDwell = new();

    /// <exception cref="InvalidConfigurationException">La configuración tiene errores de validación.</exception>
    public RadialEngine(RadialConfiguration configuration, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);

        var validation = ConfigurationValidator.Validate(configuration);
        if (!validation.IsValid)
        {
            throw new InvalidConfigurationException(validation);
        }

        _configuration = configuration;
        _clock = clock;
        _selector = new SectorSelector(configuration.Geometry.HysteresisDegrees);
        NavigationStack = new ReadOnlyCollection<RadialMenu>(_stack);
    }

    public bool IsActive => _stack.Count > 0;

    /// <summary>Menú que se está mostrando; <c>null</c> si el motor está inactivo.</summary>
    public RadialMenu? CurrentMenu => IsActive ? _stack[^1] : null;

    /// <summary>Menús abiertos, del raíz al actual. Vista en vivo: se vacía al terminar la interacción.</summary>
    public IReadOnlyList<RadialMenu> NavigationStack { get; }

    /// <summary>Centro del radial actual. Cambia al entrar o salir de un submenú.</summary>
    public PointD Origin { get; private set; }

    public PointD Pointer { get; private set; }

    /// <summary>Sector bajo el cursor; <c>null</c> en la zona central. Puede apuntar a una posición vacía.</summary>
    public RadialDirection? SelectedDirection => IsActive ? _selector.Current : null;

    public RadialItem? SelectedItem => SelectedDirection is { } direction ? CurrentMenu?.GetItem(direction) : null;

    /// <summary>Avance de la permanencia sobre un grupo, en [0, 1]. Al llegar a 1 se entra en el submenú.</summary>
    public double SubmenuProgress { get; private set; }

    /// <summary>Avance de la permanencia en el centro para volver atrás, en [0, 1].</summary>
    public double BackProgress { get; private set; }

    /// <summary>Verdadero cuando el cursor ya salió de la zona central en el menú actual y volver atrás es posible.</summary>
    public bool IsBackArmed { get; private set; }

    /// <summary>Comienza una interacción en el menú raíz con el centro en <paramref name="origin"/>. Si ya había una, la reinicia.</summary>
    public void Start(PointD origin)
    {
        RequireFinite(origin, nameof(origin));

        Reset();
        _configuration.TryGetMenu(_configuration.RootMenuId, out var root);
        _stack.Add(root!);
        Origin = origin;
        Pointer = origin;
    }

    public void UpdatePointer(PointD pointer)
    {
        RequireFinite(pointer, nameof(pointer));
        if (!IsActive)
        {
            return;
        }

        Pointer = pointer;
        Refresh();
    }

    /// <summary>Avanza la lógica dependiente del tiempo sin mover el cursor.</summary>
    public void Tick()
    {
        if (IsActive)
        {
            Refresh();
        }
    }

    /// <summary>Termina la interacción. Solo produce <see cref="RadialResultKind.Execute"/> sobre un elemento que no sea un grupo.</summary>
    public RadialResult Confirm()
    {
        if (!IsActive)
        {
            return RadialResult.Inactive;
        }

        var result = SelectedDirection is null ? RadialResult.Cancelled(CancelReason.NoSelection)
            : SelectedItem is not { } item ? RadialResult.Cancelled(CancelReason.EmptySlot)
            : item.IsGroup ? RadialResult.Cancelled(CancelReason.GroupNotEntered)
            : item.IsUnsupported ? RadialResult.Cancelled(CancelReason.UnsupportedAction)
            : RadialResult.Executed(item);

        Reset();
        return result;
    }

    /// <summary>Abandona toda la navegación y vuelve a inactivo.</summary>
    public void Cancel() => Reset();

    private void Refresh()
    {
        var now = _clock.Now;
        var previous = _selector.Current;
        var current = _selector.Update(Origin, Pointer, _configuration.Geometry.DeadZoneRadius);
        if (current != previous)
        {
            _submenuDwell.Reset();
        }

        if (UpdateSubmenuDwell(now))
        {
            return;
        }

        UpdateBackDwell(now, insideCenter: current is null);
    }

    /// <returns>Verdadero si se entró en un submenú.</returns>
    private bool UpdateSubmenuDwell(TimeSpan now)
    {
        var submenu = GetEnterableSubmenu();
        if (submenu is null)
        {
            _submenuDwell.Reset();
            SubmenuProgress = 0;
            return false;
        }

        if (!_submenuDwell.IsRunning)
        {
            _submenuDwell.Begin(now);
        }

        var dwell = _configuration.Navigation.SubmenuDwell;
        if (_submenuDwell.IsComplete(now, dwell))
        {
            ChangeMenu(push: submenu);
            return true;
        }

        SubmenuProgress = _submenuDwell.Progress(now, dwell);
        return false;
    }

    private void UpdateBackDwell(TimeSpan now, bool insideCenter)
    {
        if (!insideCenter)
        {
            // Abandonar el centro arma la vuelta atrás y cancela cualquier espera en curso.
            IsBackArmed = true;
            _backDwell.Reset();
            BackProgress = 0;
            return;
        }

        if (!IsBackArmed || _stack.Count < 2)
        {
            _backDwell.Reset();
            BackProgress = 0;
            return;
        }

        if (!_backDwell.IsRunning)
        {
            _backDwell.Begin(now);
        }

        var dwell = _configuration.Navigation.BackDwell;
        if (_backDwell.IsComplete(now, dwell))
        {
            ChangeMenu(push: null);
            return;
        }

        BackProgress = _backDwell.Progress(now, dwell);
    }

    /// <summary>Submenú al que llevaría el elemento seleccionado, o <c>null</c> si no es un grupo o no se puede entrar.</summary>
    private RadialMenu? GetEnterableSubmenu()
    {
        if (SelectedItem?.Action is not OpenMenuAction open || _stack.Count >= _configuration.Navigation.MaxDepth)
        {
            return null;
        }

        // Una configuración validada no tiene ciclos; esto protege además de entradas inesperadas.
        foreach (var menu in _stack)
        {
            if (menu.Id == open.TargetMenuId)
            {
                return null;
            }
        }

        return _configuration.TryGetMenu(open.TargetMenuId, out var target) ? target : null;
    }

    /// <summary>Entra en <paramref name="push"/> o, si es <c>null</c>, vuelve al menú anterior. En ambos casos recentra en el cursor.</summary>
    private void ChangeMenu(RadialMenu? push)
    {
        if (push is null)
        {
            _stack.RemoveAt(_stack.Count - 1);
        }
        else
        {
            _stack.Add(push);
        }

        Origin = Pointer;
        _selector.Reset();
        _submenuDwell.Reset();
        _backDwell.Reset();
        SubmenuProgress = 0;
        BackProgress = 0;
        IsBackArmed = false;
    }

    private void Reset()
    {
        _stack.Clear();
        _selector.Reset();
        _submenuDwell.Reset();
        _backDwell.Reset();
        Origin = default;
        Pointer = default;
        SubmenuProgress = 0;
        BackProgress = 0;
        IsBackArmed = false;
    }

    private static void RequireFinite(PointD point, string name)
    {
        if (!point.IsFinite)
        {
            throw new ArgumentOutOfRangeException(name, point, "La posición debe tener coordenadas finitas.");
        }
    }
}
