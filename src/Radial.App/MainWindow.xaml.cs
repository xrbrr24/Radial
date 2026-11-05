using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Radial.App.Diagnostics;
using Radial.App.Overlay;
using Radial.App.Tray;
using Radial.Core.Engine;
using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Core.Timing;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.App;

/// <summary>
/// Ventana de diagnóstico de la captura global. No es el panel de ajustes definitivo.
/// Solo refleja lo que informa el servicio: si un hook falló o la captura está detenida, lo dice.
/// </summary>
public partial class MainWindow : Window
{
    private readonly GlobalInputService _service = new(new SystemEventsAdapter());
    private readonly GestureLog _log = new();
    private readonly AppObservations _observed = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly CancellationTokenSource _closing = new();
    private readonly Dictionary<string, TextBlock> _fields = [];
    private readonly string? _selfTestOutput;
    private readonly OverlayWindow _overlay = new();
    private readonly RadialOverlayCoordinator _coordinator;
    private readonly GestureEventReader _reader;
    private readonly DispatcherTimer _overlayPoll = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
    private CancellationTokenSource? _selfTestCancel;
    private bool _selfTestRunning;
    private bool _closeRequestedDuringSelfTest;
    private Task? _readerTask;
    private readonly GestureReaderSupervisor _supervisor;
    private GestureReaderEnd? _readerEnd => _supervisor.End;
    private readonly TrayIcon? _tray;
    private bool _exiting;
    private bool _hiddenHintShown;
    private string? _readerLastFailure;

    /// <param name="configuration">La configuración cargada (o la predeterminada): de ella sale el menú del radial.</param>
    /// <param name="loadNotices">Avisos de la carga de la configuración, que se muestran en el registro.</param>
    /// <param name="selfTestOutput">Si se indica, al cargar la ventana se ejecuta la autoprueba, se escribe el resultado en ese archivo y se cierra.</param>
    public MainWindow(RadialConfiguration configuration, IReadOnlyList<SettingsNotice> loadNotices, string? selfTestOutput = null)
    {
        _selfTestOutput = selfTestOutput;
        InitializeComponent();

        // Sin supervisión la ventana debe estar siempre encima: la autoprueba no inyecta nada si otra ventana tapa la zona de prueba.
        Topmost = selfTestOutput is not null;
        BuildStateGrid();

        // El overlay se crea una vez, ahora, y se reutiliza. El coordinador reacciona a los gestos reales; la autoprueba sintética
        // lo aísla (y sin supervisión nace aislado: no debe aparecer nada antes de que ella termine).
        _overlay.Prepare();
        _overlay.Diagnostic += line => AppendLine("[overlay] " + line);
        _coordinator = new RadialOverlayCoordinator(configuration, new MonotonicClock(), _overlay, () => _service.LatestPointer, executor: ActionExecutors.Create());
        _coordinator.Completed += result => AppendLine(
            result.Kind == RadialResultKind.Execute
                ? $"[radial] confirmado: «{result.Item!.Name}»"
                : $"[radial] confirmado sin selección ({result.Reason})");
        _coordinator.Aborted += cause => AppendLine($"[radial] cancelado ({cause})");
        _coordinator.MenuChanged += (menu, depth) => AppendLine($"[radial] menú «{menu.Name}» (nivel {depth})");
        _coordinator.UnsupportedNavigation += target => AppendLine($"[radial] cancelado: la pila de menús del motor («{target.Value}») no coincide con la mostrada; no se ejecutó nada");
        _coordinator.ActionFinished += OnActionFinished;
        _coordinator.SubscriberFailed += (eventName, exception) => AppendLine($"[radial] ERROR en un suscriptor de {eventName}: {exception.GetType().Name}: {exception.Message}");
        _reader = new GestureEventReader(_service.Events, ProcessGesture, OnReaderFailure);

        // Si el lector termina sin que se haya pedido, la captura se detiene como fallo (hooks fuera, overlay oculto): los hooks no
        // pueden seguir consumiendo entrada que nadie procesa.
        _supervisor = new GestureReaderSupervisor(_reader, reason => _service.Fail(reason), _coordinator.Dismiss);
        _supervisor.Failed += OnCaptureFailed;
        _overlayPoll.Tick += (_, _) => PollRadial();
        if (selfTestOutput is not null)
        {
            _coordinator.Isolate();
        }

        Set("Última acción", "ninguna");
        var styles = _overlay.Styles;
        Set("Overlay (estilos)", $"0x{styles.Raw:X8}  layered={styles.Layered}, click-through={styles.Transparent}, sin activar={styles.NoActivate}, herramienta={styles.ToolWindow}");

        // Lo que la aplicación recibe de verdad (para comprobar qué eventos se consumieron).
        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _observed.MiddleDowns++;
            }
        };
        PreviewMouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _observed.MiddleUps++;
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _observed.EscapeDowns++;
            }
        };

        _refresh.Tick += (_, _) => RefreshState();
        _refresh.Start();
        Closing += OnWindowClosing;

        // Bandeja del sistema (solo en uso normal): mostrar la ventana de diagnóstico y salir. Al cerrar la sesión de Windows se
        // permite cerrar de verdad: una ventana que se niega a cerrarse bloquearía el apagado.
        if (selfTestOutput is null)
        {
            _tray = new TrayIcon(ShowFromTray, RequestExit);
            Application.Current.SessionEnding += (_, _) => _exiting = true;
        }

        foreach (var notice in loadNotices)
        {
            AppendLine($"[config] {notice.Severity}: {notice.Message}");
        }

        Loaded += async (_, _) =>
        {
            _readerTask = SuperviseReaderAsync();
            if (_selfTestOutput is not null)
            {
                await RunUnattendedSelfTestAsync(_selfTestOutput);
            }
            else
            {
                // La configuración ya está cargada y el lector en marcha: la captura empieza sola.
                StartCapture();
            }
        };
        RefreshState();
    }

    private async Task RunUnattendedSelfTestAsync(string path)
    {
        var lines = new List<string> { $"Autoprueba sintética {DateTime.Now:yyyy-MM-dd HH:mm:ss}" };
        var exitCode = 1;
        try
        {
            await Task.Delay(800);
            var start = _service.Start();
            lines.Add($"Inicio de la captura: {(start.Success ? "correcto" : "FALLO: " + start.Message)}");
            if (start.Success)
            {
                var steps = await ExecuteSelfTestAsync();
                lines.AddRange(steps.Select(s => $"[{(s.Passed ? "OK" : "FALLO")}] {s.Name}: {s.Detail}"));
                exitCode = steps.All(s => s.Passed) ? 0 : 2;
                var d = _service.GetDiagnostics();
                lines.Add($"Callbacks teclado/ratón: {d.KeyboardCallbacks}/{d.MouseCallbacks}; máximo {d.MaxKeyboardCallbackMs:F3}/{d.MaxMouseCallbackMs:F3} ms; lentos (≥ 50 ms): {d.SlowCallbacks}; errores: {d.Errors.Count}");
                lines.AddRange(d.Errors.Select(e => "ERROR " + e));
            }
        }
        catch (Exception exception)
        {
            lines.Add("La autoprueba se interrumpió: " + exception);
        }

        System.IO.File.WriteAllLines(path, lines);
        Application.Current.Shutdown(exitCode);
    }

    private void BuildStateGrid()
    {
        string[] names =
        [
            "Servicio", "Hook de teclado", "Hook de ratón", "Ctrl izquierdo", "Ctrl derecho", "Botón central", "Gesto (chord)",
            "Último evento", "Cursor (según el hook)", "Callbacks (teclado / ratón)", "Gestos (activ. / conf. / cancel.)",
            "Callback máximo (teclado / ratón)", "Callbacks lentos (≥ 50 ms)", "Actividad del ratón", "Última cancelación",
            "Radial (overlay)", "Overlay (estilos)", "Última acción", "Lector de eventos",
        ];

        foreach (var name in names)
        {
            var row = StateGrid.RowDefinitions.Count;
            StateGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = name, Margin = new Thickness(0, 1, 8, 1), Foreground = System.Windows.Media.Brushes.DimGray };
            var value = new TextBlock { Margin = new Thickness(0, 1, 0, 1), FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            Grid.SetRow(label, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            StateGrid.Children.Add(label);
            StateGrid.Children.Add(value);
            _fields[name] = value;
        }
    }

    private void Set(string name, string value) => _fields[name].Text = value;

    private void RefreshState()
    {
        var d = _service.GetDiagnostics();
        var running = d.State is CaptureState.Running;

        Set("Servicio", d.State switch
        {
            CaptureState.Stopped => "Detenido",
            CaptureState.Starting => "Iniciando…",
            CaptureState.Running => "En marcha",
            CaptureState.Stopping => "Deteniendo…",
            _ => "CON PROBLEMAS (ver errores)",
        });
        Set("Hook de teclado", Describe(d.KeyboardHook));
        Set("Hook de ratón", Describe(d.MouseHook));

        // Con la captura detenida no se sabe nada del teclado ni del ratón: no se muestran valores inventados.
        Set("Ctrl izquierdo", running ? Pressed(d.LeftControl) : "—");
        Set("Ctrl derecho", running ? Pressed(d.RightControl) : "—");
        Set("Botón central", running ? Pressed(d.MiddleButton) + (d.ForeignMiddle ? "  (pulso foráneo: no se intercepta)" : string.Empty) : "—");
        Set("Gesto (chord)", running
            ? d.Phase switch
            {
                ChordPhase.Idle => "Inactivo",
                ChordPhase.Active => "ACTIVO",
                _ => "Cancelado: esperando a que se suelte el botón central",
            }
            : "—");

        Set("Último evento", d.HasLastEvent
            ? $"{d.LastEventKind}{(d.LastEventKey != InputKey.None ? " " + d.LastEventKey : string.Empty)}{(d.LastEventOwnSynthetic ? " (inyectado, marca de Radial)" : d.LastEventInjected ? " (inyectado)" : string.Empty)}  hace {d.LastEventAge.TotalMilliseconds:F0} ms"
            : "ninguno");
        Set("Cursor (según el hook)", d.HasPointer ? $"{d.Pointer.X}, {d.Pointer.Y}" : "sin datos");
        Set("Callbacks (teclado / ratón)", $"{d.KeyboardCallbacks} / {d.MouseCallbacks}");
        Set("Gestos (activ. / conf. / cancel.)", $"{d.Activations} / {d.Confirmations} / {d.Cancellations}");
        Set("Callback máximo (teclado / ratón)", $"{d.MaxKeyboardCallbackMs:F3} ms / {d.MaxMouseCallbackMs:F3} ms");
        Set("Callbacks lentos (≥ 50 ms)", d.SlowCallbacks.ToString());
        Set("Actividad del ratón", d.MouseActivity switch
        {
            MouseActivity.NotMonitored => "no se vigila",
            MouseActivity.Quiet => "sin actividad en el último intervalo",
            MouseActivity.Observed => "el hook recibe eventos",
            _ => "SIN EVENTOS PESE A MOVER EL CURSOR: el hook puede haber sido eliminado",
        });
        Set("Última cancelación", d.Cancellations == 0 ? "ninguna" : d.LastCancelCause.ToString());
        Set("Radial (overlay)", _coordinator.IsIsolated ? "aislado (autoprueba en curso)" : _coordinator.IsActive ? "visible" : "oculto");

        Set("Lector de eventos", DescribeReader());

        var readerEnd = _readerEnd;
        IEnumerable<string> readerProblem = readerEnd is { Reason: not GestureReaderEndReason.Cancelled } ? ["ERROR  " + ReaderStoppedMessage(readerEnd)] : [];
        MessagesBox.Text = string.Join(Environment.NewLine, d.Errors.Select(e => "ERROR  " + e).Concat(readerProblem).Concat(d.Notices.Select(n => "AVISO  " + n)));

        // Con el lector muerto no se puede volver a iniciar la captura (nadie procesaría los eventos): hay que reabrir la aplicación.
        var readerFailed = _supervisor.Failure is not null;
        StartButton.IsEnabled = d.State is CaptureState.Stopped or CaptureState.Faulted && !_selfTestRunning && !readerFailed;
        StopButton.IsEnabled = d.State != CaptureState.Stopped && !_selfTestRunning;
        CancelButton.IsEnabled = running;
        SelfTestButton.IsEnabled = running && !_selfTestRunning;
    }

    private static string Describe(HookStatusInfo hook) =>
        !hook.Attempted ? "no instalado"
        : hook.Installed ? "instalado (no se puede verificar que Windows no lo haya eliminado)"
        : hook.Win32Error != 0 ? $"NO instalado (error {hook.Win32Error})"
        : "no instalado";

    private static string Pressed(bool value) => value ? "pulsado" : "suelto";

    /// <summary>
    /// Supervisa la tarea del lector: se guarda en <see cref="_readerTask"/> y nunca termina en error (no queda sin observar).
    /// El fallo de un evento lo absorbe el propio lector; aquí solo se registra cómo termina. Una cancelación por el cierre de la
    /// ventana es el final esperado y no se registra como problema.
    /// </summary>
    private async Task SuperviseReaderAsync()
    {
        try
        {
            // Un final inesperado lo gestiona y comunica el propio supervisor (OnCaptureFailed); el de cierre es el esperado.
            await _supervisor.RunAsync(_closing.Token);
        }
        catch
        {
            // El estado ya está guardado en el supervisor y lo muestra el diagnóstico; esta tarea no puede fallar.
        }
    }

    /// <summary>
    /// El lector terminó sin que se pidiera y el supervisor ya detuvo la captura (hooks fuera, overlay oculto, estado «con
    /// problemas»). La ventana sigue abierta, sin captura. No se reinicia nada solo.
    /// </summary>
    private void OnCaptureFailed(CaptureFailure failure)
    {
        _overlayPoll.Stop();
        AppendLine("[captura] ERROR: " + failure.Reason);
        foreach (var problem in failure.CleanupProblems)
        {
            AppendLine("[captura] ERROR en la limpieza: " + problem);
        }

        AppendLine("[captura] La captura está detenida y no se reinicia sola: cierra y vuelve a abrir Radial.");
        RefreshState();
    }

    /// <summary>Procesa un evento del canal. El radial lo recibe aunque el registro falle: perder un «confirmado» dejaría el overlay visible.</summary>
    private void ProcessGesture(GestureEvent e)
    {
        try
        {
            _log.Append(e);
            AppendLine($"#{e.Sequence} {e.Kind} ({e.Point.X}, {e.Point.Y}){(e.Cause != CancelCause.None ? " causa=" + e.Cause : string.Empty)}");
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
        AppendLine("[lector] ERROR al procesar un evento (se continúa con el siguiente): " + _readerLastFailure);
    }

    private string DescribeReader()
    {
        if (_readerEnd is { } end)
        {
            return end.Reason == GestureReaderEndReason.Cancelled ? "detenido (cierre de la ventana)" : "DETENIDO: " + ReaderStoppedMessage(end);
        }

        if (_readerTask is null)
        {
            return "sin iniciar";
        }

        return _reader.TotalFailures == 0
            ? "en marcha"
            : $"en marcha; {_reader.TotalFailures} fallo(s) recuperado(s), último: {_readerLastFailure}";
    }

    private static string ReaderStoppedMessage(GestureReaderEnd end) => GestureReaderSupervisor.Describe(end);

    /// <summary>
    /// Reenvía el gesto al coordinador del radial. Un fallo del overlay se registra y se contiene: este bucle es el único lector
    /// del canal de eventos y no debe morir (el canal no tiene límite y dejaría de vaciarse).
    /// </summary>
    private void ShowRadial(GestureEvent e)
    {
        try
        {
            _coordinator.Handle(e);
        }
        catch (Exception exception)
        {
            AppendLine("[overlay] ERROR: " + exception.Message);
            _coordinator.Dismiss();
        }

        // Mientras haya un radial en pantalla se lee el cursor con el reloj de la interfaz.
        if (_coordinator.IsActive != _overlayPoll.IsEnabled)
        {
            _overlayPoll.IsEnabled = _coordinator.IsActive;
        }
    }

    /// <summary>
    /// Comunica el resultado de una acción. Hasta que existan la bandeja y las notificaciones (fases posteriores) el aviso va al
    /// registro y a la fila «Última acción» de esta ventana; no se abre ningún cuadro de mensaje: robaría el foco a la aplicación
    /// que recibe el atajo.
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

        AppendLine("[acción] " + text);
        Set("Última acción", $"{DateTime.Now:HH:mm:ss}  {text}");
    }

    private void PollRadial()
    {
        try
        {
            _coordinator.Poll();

            // Un sondeo puede terminar el radial (navegación no soportada): entonces ya no hay nada que sondear.
            if (!_coordinator.IsActive)
            {
                _overlayPoll.Stop();
            }
        }
        catch (Exception exception)
        {
            AppendLine("[overlay] ERROR: " + exception.Message);
            _coordinator.Dismiss();
            _overlayPoll.Stop();
        }
    }

    private void AppendLine(string line)
    {
        EventsBox.AppendText(line + Environment.NewLine);
        EventsBox.ScrollToEnd();
    }

    private void OnStart(object sender, RoutedEventArgs e) => StartCapture();

    private void StartCapture()
    {
        if (_supervisor.Failure is not null)
        {
            AppendLine("NO se inicia la captura: el lector de eventos terminó con un fallo y no hay quien procese los gestos. Cierra y vuelve a abrir Radial.");
            return;
        }

        var result = _service.Start();
        AppendLine(result.Success ? "Captura iniciada." : "NO se pudo iniciar: " + result.Message);
        RefreshState();
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _service.Stop();
        AppendLine("Captura detenida.");
        RefreshState();
    }

    private void OnQuery(object sender, RoutedEventArgs e)
    {
        var d = _service.GetDiagnostics();
        AppendLine($"[estado] {d.State}; teclado={Describe(d.KeyboardHook)}; ratón={Describe(d.MouseHook)}; fase={d.Phase}; cursor={d.Pointer}; " +
                   $"callbacks={d.KeyboardCallbacks}/{d.MouseCallbacks}; máx={d.MaxKeyboardCallbackMs:F3}/{d.MaxMouseCallbackMs:F3} ms; errores={d.Errors.Count}");
    }

    private void OnCancel(object sender, RoutedEventArgs e) =>
        AppendLine(_service.RequestCancel()
            ? "Cancelación manual: había un gesto activo."
            : "Cancelación manual: no hay ningún gesto activo. (Para probarla, la autoprueba la ejecuta con un gesto sintético.)");

    private async void OnSelfTest(object sender, RoutedEventArgs e)
    {
        AppendLine("=== Autoprueba con entrada SINTÉTICA (~12 s). No uses el ratón ni el teclado. Cerrar la ventana la cancela. ===");
        try
        {
            var steps = await ExecuteSelfTestAsync();
            foreach (var step in steps)
            {
                AppendLine($"[{(step.Passed ? "OK" : "FALLO")}] {step.Name}: {step.Detail}");
            }

            var failed = steps.Count(s => !s.Passed);
            AppendLine(failed == 0
                ? "=== Autoprueba: TODO CORRECTO (entrada sintética; falta la prueba manual con hardware) ==="
                : $"=== Autoprueba: {failed} paso(s) con FALLOS ===");
        }
        catch (Exception exception)
        {
            AppendLine("La autoprueba se interrumpió: " + exception.Message);
        }
    }

    private async Task<IReadOnlyList<SelfTestStep>> ExecuteSelfTestAsync()
    {
        // Esta función es la única dueña de _selfTestRunning: OnWindowClosing lo consulta para no cerrar a mitad de la prueba.
        using var cancel = new CancellationTokenSource();
        _selfTestCancel = cancel;
        _selfTestRunning = true;

        // El radial no reacciona a la entrada sintética de la autoprueba: ni se muestra el overlay (taparía la zona de prueba y
        // falsearía sus comprobaciones de ventana) ni se toca el motor.
        _coordinator.Isolate();
        _overlayPoll.Stop();
        try
        {
            Activate();
            try
            {
                await Task.Delay(300, cancel.Token);
            }
            catch (OperationCanceledException)
            {
                return [new SelfTestStep("Autoprueba cancelada", false, "Se cerró la ventana antes de empezar a inyectar: no se inyectó nada.")];
            }

            // La zona se mide al empezar: el ratón y el teclado no se tocan hasta que la autoprueba la ha comprobado.
            Func<ScreenPoint> zoneCenter = () =>
            {
                var zone = TestZone.PointToScreen(new Point(TestZone.ActualWidth / 2, TestZone.ActualHeight / 2));
                return new ScreenPoint((int)Math.Round(zone.X), (int)Math.Round(zone.Y));
            };
            var test = new InputSelfTest(_service, _log, _observed, zoneCenter);
            return await test.RunAsync(cancel.Token);
        }
        finally
        {
            // RunAsync ya liberó de forma síncrona todo lo que pulsó antes de volver aquí.
            _selfTestCancel = null;
            _selfTestRunning = false;

            // Los eventos que la autoprueba dejó sin leer en el canal se descartan por su marca de tiempo.
            _coordinator.Resume();
            if (_closeRequestedDuringSelfTest)
            {
                Close(); // continúa el cierre que se había aplazado
            }
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

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Cerrar durante la autoprueba: se aplaza el cierre, se cancela la prueba y su bloque final libera lo pulsado.
        // Cuando termina, ExecuteSelfTestAsync vuelve a llamar a Close().
        if (_selfTestRunning)
        {
            e.Cancel = true;
            _closeRequestedDuringSelfTest = true;
            _selfTestCancel?.Cancel();
            return;
        }

        // Con la bandeja, la X de la ventana la oculta: Radial sigue en segundo plano y se cierra con «Salir» del icono.
        if (!_exiting && _tray is not null)
        {
            e.Cancel = true;
            Hide();
            if (!_hiddenHintShown)
            {
                _hiddenHintShown = true;
                AppendLine("Radial sigue en la bandeja del sistema. Para cerrarlo del todo, usa «Salir de Radial» en el menú del icono.");
            }

            return;
        }

        _refresh.Stop();
        _overlayPoll.Stop();

        // El lector se detiene primero (su final por cancelación es el esperado y no se registra) y el coordinador deja de tener
        // una acción pendiente y oculta el overlay ANTES de quitar los hooks; así nada reacciona a lo que el cierre publique.
        // Lo que sigue va en finally porque el overlay es una ventana más de la aplicación (sin mostrarla cuenta): si no se cierra,
        // el proceso no termina, falle lo que falle al liberar el servicio.
        _closing.Cancel();
        try
        {
            _coordinator.Dismiss();
        }
        finally
        {
            try
            {
                // Cancela cualquier gesto activo y quita los hooks antes de salir.
                _service.Dispose();
            }
            finally
            {
                try
                {
                    _tray?.Dispose();
                }
                finally
                {
                    _overlay.Close();
                }
            }
        }
    }
}
