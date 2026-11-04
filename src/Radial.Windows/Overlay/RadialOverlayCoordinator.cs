using System.Diagnostics;
using Radial.Core.Engine;
using Radial.Core.Execution;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Input;

namespace Radial.Windows.Overlay;

/// <summary>
/// Lo que el coordinador necesita de la ventana del overlay. La implementa Radial.App con WPF; las pruebas, con un doble.
/// Todas las llamadas llegan desde el hilo de la interfaz.
/// </summary>
public interface IRadialPresenter
{
    /// <summary>
    /// Muestra el radial centrado en <paramref name="center"/> (píxeles físicos) y sin activar la ventana.
    /// </summary>
    /// <returns>El factor píxeles físicos / DIP del monitor donde quedó (1.25 al 125 %); el coordinador lo usa para medir en DIP.</returns>
    double Show(RadialMenu menu, GeometrySettings geometry, ScreenPoint center);

    /// <summary>Resalta un sector; <c>null</c> quita el resalte (cursor en la zona central).</summary>
    void SetSelection(RadialDirection? direction);

    void Hide();
}

/// <summary>
/// Une el gesto global, el motor del radial y la ventana del overlay: al activarse el gesto muestra el menú sobre el cursor, mientras
/// dura resalta el sector bajo el cursor y al terminar lo oculta. Es el «coordinador» de la fase 4: no sabe de WPF ni de hooks.
/// Si se le da un <see cref="IActionExecutor"/>, al confirmar un elemento ejecutable pide su ejecución (sin bloquear: la acción
/// puede esperar, p. ej. a que se suelte Ctrl) e informa del resultado con <see cref="ActionFinished"/>; sin ejecutor, solo
/// informa de lo elegido. Una acción pendiente se cancela al empezar otro gesto, al aislar el coordinador y al cerrarlo.
///
/// Navegación entre menús: NO soportada todavía en el overlay (fase 5). El motor sabe entrar en un submenú por permanencia, pero este
/// coordinador y la ventana solo representan el menú raíz. Para que nunca se vea un menú y se ejecute otro, si el motor deja de
/// estar en el menú que se mostró el gesto se cancela de forma segura: se oculta el overlay, se abandona el motor, no se ejecuta
/// nada y se avisa con <see cref="UnsupportedNavigation"/>. Esto incluye la confirmación: la posición del evento de soltar se
/// aplica ANTES de confirmar y, si con ella el motor cambia de menú, no se confirma.
///
/// Todo se mide en DIP relativos al centro del radial, porque así está dibujado (el radio de la zona central de la configuración
/// coincide con el del botón central dibujado, a cualquier DPI). El cursor llega en píxeles físicos y se divide por la escala.
///
/// Autoprueba sintética: una autoprueba NUNCA debe mostrar el overlay ni ejecutar una acción. Hay tres barreras independientes:
/// (1) la marca <see cref="GestureEvent.SyntheticWindow"/> que el servicio de captura pone a todo evento publicado mientras acepta
/// entrada sintética propia: viaja con el evento y no depende de nada más; (2) el aislamiento explícito (<see cref="Isolate"/>),
/// que además oculta lo que hubiera en pantalla y cancela la acción pendiente; (3) el descarte por marca de tiempo al reanudar
/// (<see cref="Resume"/>) de lo que quedó en el canal. Mientras está aislado no se ejecuta NINGUNA acción. Solo se usa desde el
/// hilo de la interfaz.
/// </summary>
public sealed class RadialOverlayCoordinator
{
    private readonly RadialConfiguration _configuration;
    private readonly RadialEngine _engine;
    private readonly IRadialPresenter _presenter;
    private readonly Func<ScreenPoint> _pointerSource;
    private readonly Func<long> _timestampSource;
    private readonly IActionExecutor? _executor;

    private CancellationTokenSource? _pendingAction;
    private ScreenPoint _originPixels;
    private RadialMenu? _presentedMenu;
    private double _scale = 1.0;
    private RadialDirection? _shown;
    private bool _isolated;
    private bool _visible;
    private long _ignoreBefore;

    /// <exception cref="InvalidConfigurationException">La configuración no es válida.</exception>
    /// <param name="pointerSource">Última posición del cursor (píxeles físicos), p. ej. <c>GlobalInputService.LatestPointer</c>.</param>
    /// <param name="timestampSource">Reloj de las marcas de tiempo de los eventos; por defecto <see cref="Stopwatch.GetTimestamp"/>.</param>
    /// <param name="executor">Quien ejecuta las acciones confirmadas; si es nulo no se ejecuta nada.</param>
    public RadialOverlayCoordinator(
        RadialConfiguration configuration,
        IClock clock,
        IRadialPresenter presenter,
        Func<ScreenPoint> pointerSource,
        Func<long>? timestampSource = null,
        IActionExecutor? executor = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(pointerSource);

        _configuration = configuration;
        _engine = new RadialEngine(configuration, clock);
        _presenter = presenter;
        _pointerSource = pointerSource;
        _timestampSource = timestampSource ?? Stopwatch.GetTimestamp;
        _executor = executor;
    }

    /// <summary>Hay un radial en pantalla.</summary>
    public bool IsActive => _engine.IsActive;

    public bool IsIsolated => _isolated;

    /// <summary>Se confirmó el gesto (con o sin elemento ejecutable). Se produce antes de pedir la ejecución de la acción.</summary>
    public event Action<RadialResult>? Completed;

    /// <summary>Terminó la ejecución de la acción de un elemento confirmado (correcta, fallida o cancelada).</summary>
    public event Action<RadialItem, ActionExecutionResult>? ActionFinished;

    /// <summary>Hay una acción pedida que todavía no terminó (p. ej. un atajo esperando a que se suelte Ctrl).</summary>
    public bool HasPendingAction => _pendingAction is not null;

    /// <summary>El gesto se canceló (Esc, soltar Ctrl, parada de la captura...).</summary>
    public event Action<CancelCause>? Aborted;

    /// <summary>
    /// El motor pasó a otro menú (submenú o vuelta atrás) que el overlay no sabe representar: el gesto se canceló sin ejecutar nada
    /// y el overlay se ocultó. El argumento es el menú al que habría pasado. Se produce DESPUÉS de ocultar y soltar el motor; no
    /// se produce <see cref="Completed"/> ni <see cref="Aborted"/> por el mismo gesto.
    /// </summary>
    public event Action<MenuId>? UnsupportedNavigation;

    /// <summary>
    /// Un suscriptor de <see cref="Completed"/>, <see cref="ActionFinished"/>, <see cref="Aborted"/> o
    /// <see cref="UnsupportedNavigation"/> lanzó una excepción (nombre del evento y excepción). Todos se invocan suscriptor a suscriptor y siempre DESPUÉS de haber ocultado el overlay y soltado
    /// el estado del motor, así que ningún suscriptor puede impedir eso. Un suscriptor defectuoso tampoco impide ejecutar la acción
    /// confirmada ni que los demás reciban el aviso, y su fallo se comunica aquí. Si un suscriptor de este evento también lanza,
    /// se ignora.
    /// </summary>
    public event Action<string, Exception>? SubscriberFailed;

    public void Handle(GestureEvent gesture)
    {
        // Tres barreras independientes contra la autoprueba sintética. La primera (la marca del propio evento) no depende de que
        // nadie recuerde llamar a Isolate/Resume: el servicio la fija al publicar y viaja con el evento, también si se lee tarde.
        if (gesture.SyntheticWindow || _isolated || gesture.Timestamp < _ignoreBefore)
        {
            return;
        }

        switch (gesture.Kind)
        {
            case GestureEventKind.Activated:
                Begin(gesture.Point);
                break;
            case GestureEventKind.Confirmed:
                Confirm(gesture.Point);
                break;
            case GestureEventKind.Cancelled:
                Abort(gesture.Cause);
                break;
        }
    }

    /// <summary>Lee la posición del cursor y actualiza el resalte. Se llama periódicamente mientras <see cref="IsActive"/>.</summary>
    public void Poll()
    {
        if (!_engine.IsActive)
        {
            return;
        }

        Update(_pointerSource());
    }

    /// <summary>Oculta el radial, cancela cualquier interacción y deja de reaccionar a los eventos hasta <see cref="Resume"/>.</summary>
    public void Isolate()
    {
        _isolated = true;
        CancelPendingAction();
        Close();
    }

    /// <summary>Vuelve a reaccionar. Se descartan los eventos con marca anterior a este momento (los de la autoprueba).</summary>
    public void Resume()
    {
        _ignoreBefore = _timestampSource();
        _isolated = false;
    }

    /// <summary>Oculta el radial y abandona la interacción sin informar a nadie (cierre de la aplicación, error de la ventana).</summary>
    public void Dismiss()
    {
        CancelPendingAction();
        Close();
    }

    private void Begin(ScreenPoint center)
    {
        // Un gesto nuevo sustituye a lo que siguiera pendiente del anterior.
        CancelPendingAction();
        _engine.Start(new PointD(0, 0));
        _originPixels = center;
        _shown = null;

        // Visible antes de llamar: si Show falla a medias, Close ocultará lo que haya quedado.
        var root = RootMenu();
        _presentedMenu = root;
        _visible = true;
        try
        {
            _scale = _presenter.Show(root, _configuration.Geometry, center);
        }
        catch
        {
            Close();
            throw;
        }

        if (!double.IsFinite(_scale) || _scale <= 0)
        {
            _scale = 1.0;
        }

        // El cursor pudo moverse entre la pulsación y este instante.
        Update(_pointerSource());
    }

    private void Confirm(ScreenPoint point)
    {
        if (!_engine.IsActive)
        {
            return;
        }

        // El punto del evento de soltar es la posición exacta de la confirmación. Si con él el motor cambia de menú, Update ya
        // canceló el gesto: no se confirma nada, para no ejecutar un elemento de un menú que no es el que se vio.
        if (!Update(point))
        {
            return;
        }

        var result = _engine.Confirm();
        Close();
        Raise(Completed, nameof(Completed), result);

        // El menú ya está oculto: lo que ejecute una acción (p. ej. un atajo) llega a la aplicación que había debajo.
        if (result.Kind == RadialResultKind.Execute && _executor is not null)
        {
            StartAction(result.Item!);
        }
    }

    /// <summary>Pide la ejecución sin bloquear. Cualquier resultado, incluido un fallo inesperado, llega por <see cref="ActionFinished"/>.</summary>
    private void StartAction(RadialItem item)
    {
        CancelPendingAction();
        var cancellation = new CancellationTokenSource();
        _pendingAction = cancellation;
        _ = RunActionAsync(item, cancellation);
    }

    private async Task RunActionAsync(RadialItem item, CancellationTokenSource cancellation)
    {
        ActionExecutionResult result;
        try
        {
            result = await _executor!.ExecuteAsync(item.Action, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            result = ActionExecutionResult.Cancelled;
        }
        catch (Exception exception)
        {
            // Un ejecutor no debe lanzar (contrato), pero un fallo no puede perderse en una tarea que nadie espera.
            result = ActionExecutionResult.Failure(ActionErrorCodes.UnexpectedError, $"Error inesperado al ejecutar la acción ({exception.GetType().Name}).");
        }

        if (ReferenceEquals(_pendingAction, cancellation))
        {
            _pendingAction = null;
        }

        cancellation.Dispose();

        // Esta tarea no la espera nadie: nada que lance un suscriptor puede salir de aquí.
        Raise(ActionFinished, nameof(ActionFinished), item, result);
    }

    /// <summary>Invoca cada suscriptor por separado: el fallo de uno se comunica y no afecta a los demás ni al flujo del coordinador.</summary>
    private void Raise<T>(Action<T>? handlers, string eventName, T argument)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(argument);
            }
            catch (Exception exception)
            {
                ReportSubscriberFailure(eventName, exception);
            }
        }
    }

    private void Raise<T1, T2>(Action<T1, T2>? handlers, string eventName, T1 first, T2 second)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<T1, T2>>())
        {
            try
            {
                handler(first, second);
            }
            catch (Exception exception)
            {
                ReportSubscriberFailure(eventName, exception);
            }
        }
    }

    private void ReportSubscriberFailure(string eventName, Exception exception)
    {
        try
        {
            SubscriberFailed?.Invoke(eventName, exception);
        }
        catch
        {
            // Avisar del fallo es lo último que se puede hacer: si quien lo recibe también falla, no hay otro sitio donde decirlo.
        }
    }

    private void CancelPendingAction()
    {
        // No se libera aquí: lo hace RunActionAsync cuando la ejecución vuelve (con Cancelled).
        var pending = _pendingAction;
        _pendingAction = null;
        pending?.Cancel();
    }

    private void Abort(CancelCause cause)
    {
        if (!_engine.IsActive)
        {
            return;
        }

        Close();
        Raise(Aborted, nameof(Aborted), cause);
    }

    /// <returns>
    /// <c>false</c> si el motor pasó a un menú distinto del mostrado: el gesto ya se canceló (overlay oculto, motor abandonado,
    /// <see cref="UnsupportedNavigation"/> avisado) y quien llama no debe seguir con él. La selección nueva NO se comunica al
    /// presentador en ese caso: sería una dirección de otro menú.
    /// </returns>
    private bool Update(ScreenPoint pointer)
    {
        _engine.UpdatePointer(new PointD((pointer.X - _originPixels.X) / _scale, (pointer.Y - _originPixels.Y) / _scale));

        if (_engine.CurrentMenu is { } current && !ReferenceEquals(current, _presentedMenu))
        {
            Close();
            Raise(UnsupportedNavigation, nameof(UnsupportedNavigation), current.Id);
            return false;
        }

        var selected = _engine.SelectedDirection;
        if (selected != _shown)
        {
            _shown = selected;
            _presenter.SetSelection(selected);
        }

        return true;
    }

    /// <summary>Abandona el motor y oculta la ventana si estaba visible (el motor ya puede estar inactivo tras confirmar).</summary>
    private void Close()
    {
        _engine.Cancel();
        _presentedMenu = null;
        _shown = null;
        if (_visible)
        {
            _visible = false;
            _presenter.Hide();
        }
    }

    private RadialMenu RootMenu()
    {
        _configuration.TryGetMenu(_configuration.RootMenuId, out var root);
        return root!;
    }
}
