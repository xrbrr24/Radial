using System.Windows;
using System.Windows.Threading;
using Radial.App.Diagnostics;
using Radial.App.Overlay;
using Radial.Core.Engine;
using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.App.Runtime;

/// <summary>
/// Todo lo que está en marcha mientras Radial está abierto, sin interfaz: la captura global, el lector de eventos y su supervisor,
/// el overlay y el coordinador del radial, la acción pendiente y la autoprueba. Lo usa la ventana principal (que solo es el centro
/// de control) y la sección de diagnóstico. Las reglas de seguridad de la captura (el supervisor detiene la captura si el lector
/// muere, el coordinador se aísla durante la autoprueba, el cierre ordenado) son las de siempre: aquí solo se han movido de sitio.
/// </summary>
internal sealed class CaptureHost
{
    private const int MaxLines = 600;

    private readonly Queue<string> _lines = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly DispatcherTimer _overlayPoll = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly bool _selfTestMode;
    private RadialConfiguration _configuration;
    private RadialConfiguration? _pendingConfiguration;
    private CancellationTokenSource? _selfTestCancel;
    private Task? _readerTask;
    private string? _readerLastFailure;
    private bool _shutDown;

    public CaptureHost(RadialConfiguration configuration, bool selfTestMode)
    {
        _configuration = configuration;
        _selfTestMode = selfTestMode;

        Service = new GlobalInputService(new SystemEventsAdapter());
        Overlay = new OverlayWindow();

        // El overlay se crea una vez, ahora, y se reutiliza.
        Overlay.Prepare();
        Overlay.Diagnostic += line => Log("[overlay] " + line);

        Coordinator = CreateCoordinator(configuration);
        Reader = new GestureEventReader(Service.Events, ProcessGesture, OnReaderFailure);

        // Si el lector termina sin que se haya pedido, la captura se detiene como fallo (hooks fuera, overlay oculto): los hooks no
        // pueden seguir consumiendo entrada que nadie procesa. El supervisor llama al coordinador VIGENTE (se recrea al guardar).
        Supervisor = new GestureReaderSupervisor(Reader, reason => Service.Fail(reason), () => Coordinator.Dismiss());
        Supervisor.Failed += OnCaptureFailed;
        _overlayPoll.Tick += (_, _) => PollRadial();
        if (selfTestMode)
        {
            Coordinator.Isolate();
        }
    }

    public GlobalInputService Service { get; }

    public GestureLog GestureLog { get; } = new();

    public AppObservations Observed { get; } = new();

    public OverlayWindow Overlay { get; }

    public RadialOverlayCoordinator Coordinator { get; private set; }

    public GestureEventReader Reader { get; }

    public GestureReaderSupervisor Supervisor { get; }

    public bool SelfTestRunning { get; private set; }

    public bool CloseRequestedDuringSelfTest { get; private set; }

    /// <summary>La última acción ejecutada, para la sección de diagnóstico.</summary>
    public string LastAction { get; private set; } = "ninguna";

    /// <summary>Un fallo del lector de eventos (si lo hubo), para la sección de diagnóstico.</summary>
    public string? ReaderLastFailure => _readerLastFailure;

    public bool ReaderStarted => _readerTask is not null;

    /// <summary>Se añadió una línea al registro.</summary>
    public event Action<string>? LineLogged;

    /// <summary>Una acción terminó (con su resultado en texto), para avisar al usuario.</summary>
    public event Action<string, ActionExecutionResult>? ActionCompleted;

    /// <summary>Se terminó la autoprueba aplazada y hay que continuar el cierre.</summary>
    public event Action? CloseContinues;

    public IReadOnlyList<string> Lines => [.. _lines];

    public void Log(string line)
    {
        var stamped = line;
        _lines.Enqueue(stamped);
        while (_lines.Count > MaxLines)
        {
            _lines.Dequeue();
        }

        LineLogged?.Invoke(stamped);
    }

    // ───────────────────────── Puesta en marcha ─────────────────────────

    /// <summary>Empieza a leer los eventos del canal (una sola vez). La captura se inicia aparte con <see cref="StartCapture"/>.</summary>
    public void StartReader() => _readerTask ??= SuperviseReaderAsync();

    public void StartCapture()
    {
        if (Supervisor.Failure is not null)
        {
            Log("NO se inicia la captura: el lector de eventos terminó con un fallo y no hay quien procese los gestos. Cierra y vuelve a abrir Radial.");
            return;
        }

        var result = Service.Start();
        Log(result.Success ? "Captura iniciada." : "NO se pudo iniciar: " + result.Message);
    }

    public void StopCapture()
    {
        Service.Stop();
        Log("Captura detenida.");
    }

    // ───────────────────────── Configuración ─────────────────────────

    /// <summary>
    /// Hace que el radial use la configuración guardada. El coordinador se recrea con ella (la configuración del motor no cambia
    /// en caliente): se oculta el overlay, se cancela la acción pendiente y se empieza con el coordinador nuevo. Durante la
    /// autoprueba no se toca nada: se aplica al terminar.
    /// </summary>
    public void ApplyConfiguration(RadialConfiguration configuration)
    {
        if (_shutDown)
        {
            return;
        }

        if (SelfTestRunning)
        {
            _pendingConfiguration = configuration;
            return;
        }

        _configuration = configuration;
        var old = Coordinator;
        var wasIsolated = old.IsIsolated;
        try
        {
            old.Dismiss();
        }
        finally
        {
            Detach(old);
        }

        _overlayPoll.Stop();
        Coordinator = CreateCoordinator(configuration);
        if (wasIsolated)
        {
            Coordinator.Isolate();
        }

        Log("[config] El radial usa la configuración guardada.");
    }

    private RadialOverlayCoordinator CreateCoordinator(RadialConfiguration configuration)
    {
        var coordinator = new RadialOverlayCoordinator(configuration, new MonotonicClock(), Overlay, () => Service.LatestPointer, executor: ActionExecutors.Create());
        coordinator.Completed += OnCompleted;
        coordinator.Aborted += OnAborted;
        coordinator.MenuChanged += OnMenuChanged;
        coordinator.UnsupportedNavigation += OnUnsupportedNavigation;
        coordinator.ActionFinished += OnActionFinished;
        coordinator.SubscriberFailed += OnSubscriberFailed;
        return coordinator;
    }

    private void Detach(RadialOverlayCoordinator coordinator)
    {
        coordinator.Completed -= OnCompleted;
        coordinator.Aborted -= OnAborted;
        coordinator.MenuChanged -= OnMenuChanged;
        coordinator.UnsupportedNavigation -= OnUnsupportedNavigation;
        coordinator.ActionFinished -= OnActionFinished;
        coordinator.SubscriberFailed -= OnSubscriberFailed;
    }

    private void OnCompleted(RadialResult result) => Log(
        result.Kind == RadialResultKind.Execute
            ? $"[radial] confirmado: «{result.Item!.Name}»"
            : $"[radial] confirmado sin selección ({result.Reason})");

    private void OnAborted(CancelCause cause) => Log($"[radial] cancelado ({cause})");

    private void OnMenuChanged(RadialMenu menu, int depth) => Log($"[radial] menú «{menu.Name}» (nivel {depth})");

    private void OnUnsupportedNavigation(MenuId target) =>
        Log($"[radial] cancelado: la pila de menús del motor («{target.Value}») no coincide con la mostrada; no se ejecutó nada");

    private void OnSubscriberFailed(string eventName, Exception exception) =>
        Log($"[radial] ERROR en un suscriptor de {eventName}: {exception.GetType().Name}: {exception.Message}");

    /// <summary>
    /// Comunica el resultado de una acción. No se abre ningún cuadro de mensaje: robaría el foco a la aplicación que recibe el
    /// atajo. Va al registro y a quien escuche <see cref="ActionCompleted"/> (un aviso dentro de la ventana de Radial, si está abierta).
    /// </summary>
    private void OnActionFinished(RadialItem item, ActionExecutionResult result)
    {
        var text = result.Status switch
        {
            // «Aceptada por Windows», no «hecha»: un atajo hacia una aplicación elevada puede descartarse sin aviso (UIPI) y un
            // lanzamiento solo confirma que el sistema lo aceptó. Radial no puede comprobar el efecto real.
            ActionExecutionStatus.Succeeded => $"«{item.Name}»: aceptada por Windows (no se comprueba que la aplicación la procese)",
            ActionExecutionStatus.Cancelled => $"«{item.Name}»: cancelada",
            _ => $"«{item.Name}»: FALLÓ ({result.Error!.Code}) {result.Error.Message}",
        };

        Log("[acción] " + text);
        LastAction = $"{DateTime.Now:HH:mm:ss}  {text}";
        ActionCompleted?.Invoke(item.Name, result);
    }

    // ───────────────────────── Lector y gestos ─────────────────────────

    /// <summary>
    /// Supervisa la tarea del lector: se guarda y nunca termina en error (no queda sin observar). El fallo de un evento lo absorbe
    /// el propio lector; un final inesperado lo gestiona el supervisor.
    /// </summary>
    private async Task SuperviseReaderAsync()
    {
        try
        {
            await Supervisor.RunAsync(_closing.Token);
        }
        catch
        {
            // El estado ya está guardado en el supervisor y lo muestra el diagnóstico; esta tarea no puede fallar.
        }
    }

    private void OnCaptureFailed(CaptureFailure failure)
    {
        _overlayPoll.Stop();
        Log("[captura] ERROR: " + failure.Reason);
        foreach (var problem in failure.CleanupProblems)
        {
            Log("[captura] ERROR en la limpieza: " + problem);
        }

        Log("[captura] La captura está detenida y no se reinicia sola: cierra y vuelve a abrir Radial.");
    }

    /// <summary>Procesa un evento del canal. El radial lo recibe aunque el registro falle: perder un «confirmado» dejaría el overlay visible.</summary>
    private void ProcessGesture(GestureEvent e)
    {
        try
        {
            GestureLog.Append(e);
            Log($"#{e.Sequence} {e.Kind} ({e.Point.X}, {e.Point.Y}){(e.Cause != CancelCause.None ? " causa=" + e.Cause : string.Empty)}");
        }
        finally
        {
            ShowRadial(e);
        }
    }

    private void OnReaderFailure(GestureEvent e, Exception exception)
    {
        // Primero el estado (no puede fallar) y después el registro (puede ser justo lo que falla; el lector lo tolera).
        _readerLastFailure = $"{DateTime.Now:HH:mm:ss} evento #{e.Sequence} {e.Kind}: {exception.GetType().Name}: {exception.Message}";
        Log("[lector] ERROR al procesar un evento (se continúa con el siguiente): " + _readerLastFailure);
    }

    /// <summary>
    /// Reenvía el gesto al coordinador del radial. Un fallo del overlay se registra y se contiene: este bucle es el único lector
    /// del canal de eventos y no debe morir (el canal no tiene límite y dejaría de vaciarse).
    /// </summary>
    private void ShowRadial(GestureEvent e)
    {
        try
        {
            Coordinator.Handle(e);
        }
        catch (Exception exception)
        {
            Log("[overlay] ERROR: " + exception.Message);
            Coordinator.Dismiss();
        }

        // Mientras haya un radial en pantalla se lee el cursor con el reloj de la interfaz.
        if (Coordinator.IsActive != _overlayPoll.IsEnabled)
        {
            _overlayPoll.IsEnabled = Coordinator.IsActive;
        }
    }

    private void PollRadial()
    {
        try
        {
            Coordinator.Poll();

            // Un sondeo puede terminar el radial (navegación no soportada): entonces ya no hay nada que sondear.
            if (!Coordinator.IsActive)
            {
                _overlayPoll.Stop();
            }
        }
        catch (Exception exception)
        {
            Log("[overlay] ERROR: " + exception.Message);
            Coordinator.Dismiss();
            _overlayPoll.Stop();
        }
    }

    // ───────────────────────── Autoprueba ─────────────────────────

    /// <summary>
    /// Ejecuta la autoprueba con entrada sintética. Esta función es la única dueña de <see cref="SelfTestRunning"/>: el cierre de
    /// la ventana lo consulta para no cerrar a mitad de la prueba. <paramref name="activateWindow"/> trae la ventana al primer plano.
    /// </summary>
    public async Task<IReadOnlyList<SelfTestStep>> ExecuteSelfTestAsync(Func<ScreenPoint> zoneCenter, Action activateWindow)
    {
        using var cancel = new CancellationTokenSource();
        _selfTestCancel = cancel;
        SelfTestRunning = true;

        // El radial no reacciona a la entrada sintética de la autoprueba: ni se muestra el overlay (taparía la zona de prueba y
        // falsearía sus comprobaciones de ventana) ni se toca el motor.
        Coordinator.Isolate();
        _overlayPoll.Stop();
        try
        {
            activateWindow();
            try
            {
                await Task.Delay(300, cancel.Token);
            }
            catch (OperationCanceledException)
            {
                return [new SelfTestStep("Autoprueba cancelada", false, "Se cerró la ventana antes de empezar a inyectar: no se inyectó nada.")];
            }

            var test = new InputSelfTest(Service, GestureLog, Observed, zoneCenter);
            return await test.RunAsync(cancel.Token);
        }
        finally
        {
            // RunAsync ya liberó de forma síncrona todo lo que pulsó antes de volver aquí.
            _selfTestCancel = null;
            SelfTestRunning = false;

            // Los eventos que la autoprueba dejó sin leer en el canal se descartan por su marca de tiempo.
            Coordinator.Resume();
            if (_pendingConfiguration is { } pending)
            {
                _pendingConfiguration = null;
                ApplyConfiguration(pending);
            }

            if (CloseRequestedDuringSelfTest)
            {
                CloseContinues?.Invoke(); // continúa el cierre que se había aplazado
            }
        }
    }

    /// <summary>
    /// Pide cerrar durante la autoprueba: se aplaza el cierre y se cancela la prueba; al terminar, <see cref="CloseContinues"/> lo continúa.
    /// Devuelve <c>true</c> si había una autoprueba en curso (el cierre se ha aplazado).
    /// </summary>
    public bool DeferCloseIfSelfTestRunning()
    {
        if (!SelfTestRunning)
        {
            return false;
        }

        CloseRequestedDuringSelfTest = true;
        _selfTestCancel?.Cancel();
        return true;
    }

    // ───────────────────────── Cierre ─────────────────────────

    /// <summary>
    /// Cierre ordenado: el lector se detiene primero (su final por cancelación es el esperado), el coordinador deja de tener una acción
    /// pendiente y oculta el overlay ANTES de quitar los hooks; así nada reacciona a lo que el cierre publique. Lo demás va en
    /// <c>finally</c> porque el overlay es una ventana más de la aplicación: si no se cierra, el proceso no termina.
    /// </summary>
    public void Shutdown(Action? beforeOverlayClose = null)
    {
        _shutDown = true;
        _overlayPoll.Stop();
        _closing.Cancel();
        try
        {
            Coordinator.Dismiss();
        }
        finally
        {
            try
            {
                // Cancela cualquier gesto activo y quita los hooks antes de salir.
                Service.Dispose();
            }
            finally
            {
                try
                {
                    beforeOverlayClose?.Invoke();
                }
                finally
                {
                    Overlay.Close();
                }
            }
        }
    }

    public bool SelfTestMode => _selfTestMode;

    public RadialConfiguration Configuration => _configuration;
}
