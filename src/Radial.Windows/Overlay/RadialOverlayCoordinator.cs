using System.Diagnostics;
using Radial.Core.Engine;
using Radial.Core.Execution;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Input;

namespace Radial.Windows.Overlay;

/// <summary>
/// Progreso de las dos permanencias que el motor mide (entrar en un grupo y volver atrás), para que la ventana lo dibuje. Los
/// valores están en [0, 1]; <see cref="BackArmed"/> dice si volver por el centro ya está permitido (el cursor salió antes del centro).
/// </summary>
public readonly record struct RadialProgress(double Submenu, double Back, bool BackArmed);

/// <summary>
/// Dónde quedó realmente el radial tras mostrarlo: el factor píxeles físicos / DIP del monitor y el centro FÍSICO de la ventana. El
/// centro puede no ser el pedido: junto a un borde de pantalla la ventana se desplaza hacia dentro para caber entera, y la selección
/// tiene que medirse desde donde está dibujado.
/// </summary>
public readonly record struct PresentedPlacement(double Scale, ScreenPoint Center);

/// <summary>
/// Lo que el coordinador necesita de la ventana del overlay. La implementa Radial.App con WPF; las pruebas, con un doble.
/// Todas las llamadas llegan desde el hilo de la interfaz. El estado que se presenta es: el menú actual (y su nivel), el centro
/// donde está colocado, el sector resaltado y el progreso de las permanencias.
/// </summary>
public interface IRadialPresenter
{
    /// <summary>
    /// Muestra el radial del menú raíz centrado en <paramref name="center"/> (píxeles físicos) y sin activar la ventana. Si el centro
    /// queda demasiado cerca del borde del monitor para que la ventana quepa, la ventana se desplaza hacia dentro lo justo.
    /// </summary>
    /// <returns>El factor píxeles físicos / DIP del monitor donde quedó (1.25 al 125 %) y el centro físico REAL de la ventana; el
    /// coordinador mide desde ese centro, no desde el pedido.</returns>
    PresentedPlacement Show(RadialMenu menu, GeometrySettings geometry, ScreenPoint center);

    /// <summary>
    /// Cambia el menú que se muestra (entrar en un grupo o volver atrás) y recoloca la ventana ya visible centrada en
    /// <paramref name="center"/>, SIN ocultarla, recrearla ni parpadear. La selección y el progreso quedan a cero.
    /// <paramref name="depth"/> es el número de menús apilados, raíz incluida (1 = menú raíz): con más de uno el centro ofrece volver.
    /// </summary>
    /// <returns>La escala del monitor y el centro físico real de la ventana (como <see cref="Show"/>; también se ajusta a los bordes).</returns>
    PresentedPlacement ShowMenu(RadialMenu menu, int depth, ScreenPoint center);

    /// <summary>
    /// El cursor pasó a otro monitor con el radial visible: traslada la ventana, SIN ocultarla ni redibujarla, para que quede ENTERA en el
    /// área de trabajo del monitor del cursor y el cursor a <paramref name="offsetDip"/> DIP de su centro (el mismo desfase que tenía; se
    /// convierte a píxeles con el DPI del monitor nuevo). Si no cabe así, la desplaza hacia dentro lo justo. Mantiene menú, sector resaltado
    /// y progreso (los comunica el coordinador). Se llama desde el hilo de la interfaz.
    /// </summary>
    /// <returns>La escala del monitor nuevo y el centro físico REAL de la ventana (como <see cref="ShowMenu"/>).</returns>
    PresentedPlacement FollowCursor(ScreenPoint cursor, PointD offsetDip);

    /// <summary>Resalta un sector; <c>null</c> quita el resalte (cursor en la zona central).</summary>
    void SetSelection(RadialDirection? direction);

    /// <summary>
    /// La ventana se recolocó SOLA estando visible (cambio de DPI al pasar a otro monitor, <c>WM_DPICHANGED</c>): el centro dibujado y la
    /// escala pueden ser otros que los devueltos por <see cref="Show"/> o <see cref="ShowMenu"/>. El coordinador mide desde el centro
    /// dibujado, así que tiene que enterarse. Se produce en el hilo de la interfaz.
    /// </summary>
    event Action<PresentedPlacement>? PlacementChanged;

    /// <summary>Actualiza el progreso de la permanencia para entrar en un grupo o para volver atrás.</summary>
    void SetProgress(RadialProgress progress);

    void Hide();
}

/// <summary>
/// Une el gesto global, el motor del radial y la ventana del overlay: al activarse el gesto muestra el menú sobre el cursor, mientras
/// dura resalta el sector bajo el cursor, dibuja el progreso de la permanencia y cambia de menú cuando el motor entra en un grupo o
/// vuelve atrás, y al terminar lo oculta. No sabe de WPF ni de hooks.
/// Si se le da un <see cref="IActionExecutor"/>, al confirmar un elemento ejecutable pide su ejecución (sin bloquear: la acción
/// puede esperar, p. ej. a que se suelte Ctrl) e informa del resultado con <see cref="ActionFinished"/>; sin ejecutor, solo
/// informa de lo elegido. Una acción pendiente se cancela al empezar otro gesto, al aislar el coordinador y al cerrarlo.
///
/// Navegación entre menús (fase 5): el motor decide cuándo se entra en un grupo (permanencia) o se vuelve atrás; este coordinador lo
/// refleja en la ventana. Cuando cambia el menú del motor se llama a <see cref="IRadialPresenter.ShowMenu"/> para el menú nuevo,
/// centrado donde el motor recentró (la posición del cursor en ese instante), con la ventana ya visible. Los submenús se
/// anidan hasta <c>MaxDepth</c> y las permanencias son las de la configuración.
///
/// Coordenadas: el motor trabaja en UN solo sistema de DIP durante toda la interacción (el del primer centro). El coordinador solo
/// convierte entre píxeles físicos y ese sistema con un ANCLA (píxel físico de un punto y su coordenada en el motor) y la escala
/// del monitor donde está el radial: <c>motor = ancla.motor + (píxel − ancla.píxel) / escala</c>. Al cambiar de menú, el motor
/// pone su origen geométrico en el cursor; el centro FÍSICO de la ventana es ese mismo punto convertido de vuelta, y se re-ancla ahí
/// con la escala del monitor donde quedó. Así el origen no se resta dos veces ni la conversión se desincroniza, y las coordenadas del
/// motor son continuas aunque la ventana cambie de sitio.
///
/// Protección contra estados incoherentes: la pila de menús que muestra el coordinador se compara con la del motor tras cada
/// actualización. Solo se admiten tres situaciones: igual, un nivel más (entrar) o un nivel menos (volver). Cualquier otra (un salto
/// de varios niveles, otro menú, una pila vacía) cancela el gesto de forma segura: se oculta el overlay, se abandona el motor, no se
/// ejecuta nada y se avisa con <see cref="UnsupportedNavigation"/>. Nunca puede haber un menú mostrado distinto del que decide
/// qué se ejecuta.
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
    // Conversión píxeles físicos <-> sistema DIP del motor (ver el resumen de la clase): un ancla y la escala del monitor actual.
    private ScreenPoint _anchorPixels;
    private PointD _anchorEngine;
    private double _scale = 1.0;

    // Seguir al cursor entre monitores: sin localizador (nulo) no hay seguimiento. _monitor es el monitor donde está el radial ahora.
    private readonly IMonitorLocator? _monitors;
    private MonitorAreas? _monitor;
    private string? _traceKey;
    private bool _traceLostCursorMonitor;
    private int _traceWaits;

    /// <summary>Líneas «todavía no lo sigue» por gesto como máximo: un cursor que tiembla sobre el borde no inunda el diagnóstico.</summary>
    public const int MaxWaitTraces = 20;

    // Ajuste a los bordes: si el radial se desplazó para caber en el monitor, el cursor queda lejos del centro dibujado (incluso sobre un
    // sector). Hasta que se mueva ArmDistanceDip desde donde estaba al mostrarse, el motor ve el cursor en el centro (sin selección):
    // soltar el botón sin mover nada no ejecuta el sector que quedó bajo el cursor ni entra solo en un grupo.
    private bool _armed = true;
    private ScreenPoint _armOrigin;

    /// <summary>Distancia (DIP) que el cursor debe recorrer, tras un radial desplazado por un borde, para que empiece a seleccionar.</summary>
    public const double ArmDistanceDip = 10;

    // Lo que la ventana está mostrando: la pila de menús (espejo de la del motor), el sector resaltado y el progreso.
    private readonly List<RadialMenu> _presented = [];
    private RadialDirection? _shown;
    private RadialProgress _progress;
    private bool _isolated;
    private bool _visible;
    private long _ignoreBefore;

    /// <exception cref="InvalidConfigurationException">La configuración no es válida.</exception>
    /// <param name="pointerSource">Última posición del cursor (píxeles físicos), p. ej. <c>GlobalInputService.LatestPointer</c>.</param>
    /// <param name="timestampSource">Reloj de las marcas de tiempo de los eventos; por defecto <see cref="Stopwatch.GetTimestamp"/>.</param>
    /// <param name="executor">Quien ejecuta las acciones confirmadas; si es nulo no se ejecuta nada.</param>
    /// <param name="monitors">Dónde están los monitores; con él, el radial abierto sigue al cursor cuando pasa a otro monitor (ver <see cref="FollowMonitor"/>). Si es nulo, no.</param>
    public RadialOverlayCoordinator(
        RadialConfiguration configuration,
        IClock clock,
        IRadialPresenter presenter,
        Func<ScreenPoint> pointerSource,
        Func<long>? timestampSource = null,
        IActionExecutor? executor = null,
        IMonitorLocator? monitors = null)
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
        _monitors = monitors;
    }

    /// <summary>Hay un radial en pantalla.</summary>
    public bool IsActive => _engine.IsActive;

    public bool IsIsolated => _isolated;

    /// <summary>Hay un localizador de monitores: el radial abierto sigue al cursor a otro monitor. Sin él (nulo) el seguimiento está apagado.</summary>
    public bool FollowsMonitors => _monitors is not null;

    /// <summary>
    /// Diagnóstico del seguimiento entre monitores, una línea cada vez que algo cambia (no en cada sondeo): activación y monitor del radial,
    /// el cursor aparece en otro monitor (y a qué distancia), el radial lo sigue (y si se desplazó), o algo falla. Sirve para saber, sin
    /// capturas de pantalla, si Radial recibió el movimiento, qué monitor detectó y si pidió trasladar la ventana. Se produce en el hilo de la
    /// interfaz; un suscriptor que lance se ignora.
    /// </summary>
    public event Action<string>? FollowTrace;

    /// <summary>Se confirmó el gesto (con o sin elemento ejecutable). Se produce antes de pedir la ejecución de la acción.</summary>
    public event Action<RadialResult>? Completed;

    /// <summary>Terminó la ejecución de la acción de un elemento confirmado (correcta, fallida o cancelada).</summary>
    public event Action<RadialItem, ActionExecutionResult>? ActionFinished;

    /// <summary>Hay una acción pedida que todavía no terminó (p. ej. un atajo esperando a que se suelte Ctrl).</summary>
    public bool HasPendingAction => _pendingAction is not null;

    /// <summary>El gesto se canceló (Esc, soltar Ctrl, parada de la captura...).</summary>
    public event Action<CancelCause>? Aborted;

    /// <summary>
    /// La ventana pasó a mostrar otro menú porque el motor entró en un grupo o volvió atrás (menú nuevo y número de menús apilados,
    /// raíz incluida). Se produce después de recolocar la ventana.
    /// </summary>
    public event Action<RadialMenu, int>? MenuChanged;

    /// <summary>
    /// La pila de menús del motor no es coherente con la que se muestra (ni igual, ni un nivel más, ni un nivel menos): el gesto se
    /// canceló sin ejecutar nada y el overlay se ocultó. El argumento es el menú actual del motor. Se produce DESPUÉS de ocultar y
    /// soltar el motor; no se produce <see cref="Completed"/> ni <see cref="Aborted"/> por el mismo gesto. No debería ocurrir; es
    /// la red de seguridad que evita mostrar un menú y ejecutar otro.
    /// </summary>
    public event Action<MenuId>? UnsupportedNavigation;

    /// <summary>
    /// Un suscriptor de <see cref="Completed"/>, <see cref="ActionFinished"/>, <see cref="Aborted"/> o
    /// <see cref="UnsupportedNavigation"/> lanzó una excepción (nombre del evento y excepción). Todos se invocan suscriptor a
    /// suscriptor y siempre DESPUÉS de haber ocultado el overlay y soltado el estado del motor, así que ningún suscriptor puede impedir eso. Un suscriptor defectuoso tampoco impide ejecutar la acción
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
        _anchorPixels = center;
        _anchorEngine = new PointD(0, 0);
        _shown = null;
        _progress = default;

        // Visible antes de llamar: si Show falla a medias, Close ocultará lo que haya quedado.
        var root = RootMenu();
        _presented.Clear();
        _presented.Add(root);
        _visible = true;
        _presenter.PlacementChanged -= OnPlacementChanged; // Begin puede repetirse sin Close (Start reinicia): nunca dos veces
        _presenter.PlacementChanged += OnPlacementChanged;
        PresentedPlacement placed;
        try
        {
            placed = _presenter.Show(root, _configuration.Geometry, center);
        }
        catch
        {
            Close();
            throw;
        }

        // El motor mide desde su origen (0, 0); ese origen es el centro DIBUJADO, que junto a un borde no es el cursor.
        _scale = SanitizeScale(placed.Scale);
        _anchorPixels = placed.Center;
        _armOrigin = center;
        _armed = placed.Center == center;
        _monitor = _monitors?.Find(center);
        _traceKey = null;
        _traceLostCursorMonitor = false;
        _traceWaits = 0;
        TraceActivation(center, placed.Center);

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
        if (!Update(point, follow: false))
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
    /// <c>false</c> si la pila de menús del motor es incoherente con la mostrada: el gesto ya se canceló (overlay oculto, motor
    /// abandonado, <see cref="UnsupportedNavigation"/> avisado) y quien llama no debe seguir con él. En ese caso la selección nueva NO
    /// se comunica al presentador: sería una dirección de un menú que no es el que se ve.
    /// </returns>
    private bool Update(ScreenPoint pointer, bool follow = true)
    {
        if (follow)
        {
            FollowMonitor(pointer);
        }

        _engine.UpdatePointer(ToEngine(Effective(pointer)));

        switch (Classify(_presented, _engine.NavigationStack))
        {
            case NavigationStep.None:
                break;

            case NavigationStep.Enter:
            case NavigationStep.Back:
                Navigate();
                break;

            default:
                var current = _engine.CurrentMenu;
                Close();
                Raise(UnsupportedNavigation, nameof(UnsupportedNavigation), current?.Id ?? default);
                return false;
        }

        var selected = _engine.SelectedDirection;
        if (selected != _shown)
        {
            _shown = selected;
            _presenter.SetSelection(selected);
        }

        var progress = new RadialProgress(_engine.SubmenuProgress, _engine.BackProgress, _engine.IsBackArmed);
        if (progress != _progress)
        {
            _progress = progress;
            _presenter.SetProgress(progress);
        }

        return true;
    }

    /// <summary>
    /// El motor entró en un grupo o volvió atrás y recentró su origen en el cursor: cambia el menú de la ventana, que se recoloca
    /// con ese punto como centro físico sin ocultarse, y se re-ancla la conversión de coordenadas ahí con la escala del monitor donde
    /// quedó. La selección y el progreso empiezan de cero (el motor los reinició).
    /// </summary>
    private void Navigate()
    {
        var menu = _engine.CurrentMenu!;
        var depth = _engine.NavigationStack.Count;
        var center = FromEngine(_engine.Origin);

        PresentedPlacement placed;
        try
        {
            placed = _presenter.ShowMenu(menu, depth, center);
        }
        catch
        {
            Close();
            throw;
        }

        // El origen del motor pasa a ser el centro DIBUJADO: coincide con el cursor salvo que el radial se haya desplazado para
        // caber en el monitor (borde de pantalla); en ese caso el cursor queda lejos del centro y se espera a que se mueva.
        _anchorPixels = placed.Center;
        _anchorEngine = _engine.Origin;
        _scale = SanitizeScale(placed.Scale);
        _armOrigin = center;
        _armed = placed.Center == center;
        _monitor = _monitors?.Find(center);
        _traceKey = null;
        _presented.Clear();
        _presented.AddRange(_engine.NavigationStack);
        _shown = null;
        _progress = default;
        Raise(MenuChanged, nameof(MenuChanged), menu, depth);
    }

    // motor = ancla.motor + (píxel − ancla.píxel) / escala
    private PointD ToEngine(ScreenPoint pixels) => new(
        _anchorEngine.X + ((pixels.X - _anchorPixels.X) / _scale),
        _anchorEngine.Y + ((pixels.Y - _anchorPixels.Y) / _scale));

    // píxel = ancla.píxel + (motor − ancla.motor) · escala
    private ScreenPoint FromEngine(PointD point) => new(
        _anchorPixels.X + (int)Math.Round((point.X - _anchorEngine.X) * _scale),
        _anchorPixels.Y + (int)Math.Round((point.Y - _anchorEngine.Y) * _scale));

    /// <summary>
    /// La posición que se le da al motor: el cursor, o el centro dibujado mientras el radial esté desplazado por un borde y el cursor
    /// no se haya movido <see cref="ArmDistanceDip"/>. Una vez que se mueve, sigue al cursor hasta el final de la interacción.
    /// </summary>
    private ScreenPoint Effective(ScreenPoint pointer)
    {
        if (_armed)
        {
            return pointer;
        }

        var limit = ArmDistanceDip * _scale;
        double dx = pointer.X - _armOrigin.X;
        double dy = pointer.Y - _armOrigin.Y;
        if ((dx * dx) + (dy * dy) >= limit * limit)
        {
            _armed = true;
            return pointer;
        }

        return _anchorPixels;
    }


    /// <summary>
    /// El cursor pasó a otro monitor con el radial abierto: el radial lo sigue (<see cref="IRadialPresenter.FollowCursor"/>) y queda ENTERO
    /// en el área de trabajo del monitor nuevo. El cursor sigue siendo la referencia: se pide al presentador que lo deje a los mismos DIP del
    /// centro que tenía (el DPI del monitor nuevo puede ser otro, así que el desfase viaja en DIP), y el origen del motor (<c>_anchorEngine</c>
    /// no cambia) pasa a ser el centro dibujado real. Dos casos:
    /// <list type="bullet">
    /// <item>El presentador lo dejó donde se pidió: el cursor conserva su posición relativa al radial, el motor ve exactamente lo mismo y la
    /// selección y las permanencias en curso no se enteran. Transición continua.</item>
    /// <item>Tuvo que desplazarlo (lo normal: el cursor acaba de entrar y está a unos píxeles del borde compartido, y el radial no cabe
    /// entero si se centra ahí): el cursor queda en otro sitio del dibujo, a menudo en el sector contrario, así que cualquier sector que
    /// resultara seleccionado sería un salto. Como al activar junto a un borde, no hay selección hasta que el cursor se mueve
    /// <see cref="ArmDistanceDip"/>: soltar el botón en ese instante no ejecuta nada.</item>
    /// </list>
    /// Solo se cruza cuando el cursor se aleja <see cref="MonitorFollow.MarginDip"/> del monitor del radial (histéresis: un temblor en el
    /// borde compartido no lo hace saltar de un lado a otro). Al confirmar no se sigue: lo que se ejecuta es lo que se veía.
    /// </summary>
    private void FollowMonitor(ScreenPoint pointer)
    {
        if (_monitors is null || _monitor is not { } current)
        {
            return;
        }

        if (_monitors.Find(pointer) is not { } atCursor)
        {
            // Una sola vez por gesto: si Windows deja de dar el monitor del cursor, es lo primero que hay que saber.
            if (!_traceLostCursorMonitor)
            {
                _traceLostCursorMonitor = true;
                Trace($"Windows no dio el monitor del cursor {Pt(pointer)}: no se puede seguir");
            }

            return;
        }

        if (atCursor.Bounds == current.Bounds)
        {
            _traceKey = null; // de vuelta en el monitor del radial: el siguiente cruce se vuelve a anotar
            return;
        }

        var marginPixels = (int)Math.Ceiling(MonitorFollow.MarginDip * _scale);
        if (!MonitorFollow.ShouldFollow(current, atCursor, pointer, marginPixels))
        {
            // El cursor ya está en otro monitor pero aún no lo bastante lejos: se anota una vez por monitor.
            var key = "wait|" + Rect(atCursor.Bounds);
            if (key != _traceKey && _traceWaits < MaxWaitTraces)
            {
                _traceKey = key;
                _traceWaits++;
                Trace($"el cursor {Pt(pointer)} está en el monitor {Rect(atCursor.Bounds)}, a {MonitorFollow.DistanceOutside(current.Bounds, pointer)} px del monitor del radial {Rect(current.Bounds)}; todavía no lo sigue (margen {marginPixels} px)");
            }

            return;
        }

        // El desfase actual del cursor respecto al centro dibujado, en DIP (es independiente de la escala del monitor).
        var offset = new PointD((pointer.X - _anchorPixels.X) / _scale, (pointer.Y - _anchorPixels.Y) / _scale);

        PresentedPlacement placed;
        try
        {
            placed = _presenter.FollowCursor(pointer, offset);
        }
        catch (Exception exception)
        {
            Trace($"FALLÓ al trasladar el radial a {Rect(atCursor.Bounds)} con el cursor en {Pt(pointer)}: {exception.GetType().Name}");
            Close();
            throw;
        }

        _scale = SanitizeScale(placed.Scale);
        _anchorPixels = placed.Center;
        _monitor = atCursor;
        _traceKey = null;

        // ¿Quedó donde se pidió (con 1 px de margen por el redondeo)?
        var ideal = OverlayPlacement.IdealCenter(pointer, offset.X, offset.Y, _scale);
        var displaced = Math.Abs(placed.Center.X - ideal.X) > 1 || Math.Abs(placed.Center.Y - ideal.Y) > 1;
        if (displaced || !_armed)
        {
            _armed = false;
            _armOrigin = pointer;
        }

        Trace($"el radial sigue al cursor {Pt(pointer)} al monitor {Rect(atCursor.Bounds)}: centro {Pt(placed.Center)}, escala {_scale:F2}; " +
            (displaced ? "desplazado por el borde: sin selección hasta que el cursor se mueva" : _armed ? "sin desplazar: la selección continúa" : "sin desplazar, pero sin selección hasta que el cursor se mueva"));
    }

    /// <summary>La primera línea del diagnóstico de cada gesto: dónde se mostró el radial y si va a poder seguir al cursor.</summary>
    private void TraceActivation(ScreenPoint cursor, ScreenPoint center)
    {
        if (_monitors is null)
        {
            Trace($"radial activado con el cursor en {Pt(cursor)}: NO hay localizador de monitores, el radial no seguirá al cursor a otro monitor");
        }
        else if (_monitor is not { } monitor)
        {
            Trace($"radial activado con el cursor en {Pt(cursor)}: Windows no dio el monitor, el radial no seguirá al cursor a otro monitor");
        }
        else
        {
            Trace($"radial activado con el cursor en {Pt(cursor)}, centro dibujado {Pt(center)}; monitor del radial {Rect(monitor.Bounds)}, área de trabajo {Rect(monitor.Work)}; seguimiento entre monitores: activo");
        }
    }

    /// <summary>Anota una línea del seguimiento entre monitores. Nunca lanza: un diagnóstico no puede romper el gesto.</summary>
    private void Trace(string line)
    {
        try
        {
            FollowTrace?.Invoke(line);
        }
        catch
        {
            // Quien recibe la línea falló: no hay dónde decirlo.
        }
    }

    private static string Pt(ScreenPoint p) => $"({p.X}, {p.Y})";

    private static string Rect(ScreenRect r) => $"({r.Left}, {r.Top})–({r.Right}, {r.Bottom})";

    /// <summary>
    /// La ventana se recolocó sola con el radial visible (cambio de DPI al pasar a otro monitor). Se re-ancla en el centro y la escala
    /// NUEVOS: el centro dibujado sigue siendo el origen del motor (<c>_anchorEngine</c> no cambia), así que la selección se sigue midiendo
    /// desde lo que se ve y con la escala del monitor donde está ahora. Si el centro se movió y el cursor todavía no se había movido
    /// <see cref="ArmDistanceDip"/>, el cursor puede haber quedado sobre un sector que antes no lo estaba: se vuelve a esperar a que se
    /// mueva. Si ya no está desplazado respecto al cursor de activación, la espera termina. Se ignora si no hay radial en pantalla.
    /// </summary>
    private void OnPlacementChanged(PresentedPlacement placed)
    {
        if (!_visible || !_engine.IsActive)
        {
            return;
        }

        if (placed.Center == _anchorPixels && Math.Abs(SanitizeScale(placed.Scale) - _scale) < 1e-9)
        {
            return; // la ventana se recolocó donde ya estaba (p. ej. el aviso de DPI que sigue a un seguimiento de monitor): nada cambia
        }

        _scale = SanitizeScale(placed.Scale);
        _anchorPixels = placed.Center;

        if (placed.Center == _armOrigin)
        {
            _armed = true;
        }
        else if (_armed)
        {
            var pointer = _pointerSource();
            var limit = ArmDistanceDip * _scale;
            double dx = pointer.X - _armOrigin.X;
            double dy = pointer.Y - _armOrigin.Y;
            _armed = (dx * dx) + (dy * dy) >= limit * limit;
        }
    }
    private static double SanitizeScale(double scale) => double.IsFinite(scale) && scale > 0 ? scale : 1.0;

    /// <summary>Cómo se relaciona la pila de menús del motor con la que muestra la ventana.</summary>
    internal enum NavigationStep
    {
        /// <summary>Es la misma: no hay que cambiar nada.</summary>
        None,

        /// <summary>El motor tiene un menú más (entró en un grupo): la pila mostrada es un prefijo de la del motor.</summary>
        Enter,

        /// <summary>El motor tiene un menú menos (volvió atrás): la pila del motor es un prefijo de la mostrada.</summary>
        Back,

        /// <summary>Cualquier otra cosa: pila vacía, otro menú, un salto de varios niveles.</summary>
        Inconsistent,
    }

    internal static NavigationStep Classify(IReadOnlyList<RadialMenu> presented, IReadOnlyList<RadialMenu> engine)
    {
        if (presented.Count == 0 || engine.Count == 0)
        {
            return NavigationStep.Inconsistent;
        }

        if (engine.Count == presented.Count)
        {
            return IsPrefix(engine, presented, engine.Count) ? NavigationStep.None : NavigationStep.Inconsistent;
        }

        if (engine.Count == presented.Count + 1)
        {
            return IsPrefix(presented, engine, presented.Count) ? NavigationStep.Enter : NavigationStep.Inconsistent;
        }

        if (engine.Count == presented.Count - 1)
        {
            return IsPrefix(engine, presented, engine.Count) ? NavigationStep.Back : NavigationStep.Inconsistent;
        }

        return NavigationStep.Inconsistent;
    }

    private static bool IsPrefix(IReadOnlyList<RadialMenu> shorter, IReadOnlyList<RadialMenu> longer, int count)
    {
        for (var index = 0; index < count; index++)
        {
            if (!ReferenceEquals(shorter[index], longer[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Abandona el motor y oculta la ventana si estaba visible (el motor ya puede estar inactivo tras confirmar).</summary>
    private void Close()
    {
        _engine.Cancel();
        _presented.Clear();
        _shown = null;
        _progress = default;
        _armed = true;
        if (_visible)
        {
            _visible = false;
            _presenter.PlacementChanged -= OnPlacementChanged; // el presentador es único y dura más que el coordinador: sin fugas
            _presenter.Hide();
        }
    }

    private RadialMenu RootMenu()
    {
        _configuration.TryGetMenu(_configuration.RootMenuId, out var root);
        return root!;
    }
}
