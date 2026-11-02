using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Radial.App.Diagnostics;
using Radial.Windows.Input;

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
    private CancellationTokenSource? _selfTestCancel;
    private bool _selfTestRunning;
    private bool _closeRequestedDuringSelfTest;

    /// <param name="selfTestOutput">Si se indica, al cargar la ventana se ejecuta la autoprueba, se escribe el resultado en ese archivo y se cierra.</param>
    public MainWindow(string? selfTestOutput = null)
    {
        _selfTestOutput = selfTestOutput;
        InitializeComponent();

        // Sin supervisión la ventana debe estar siempre encima: la autoprueba no inyecta nada si otra ventana tapa la zona de prueba.
        Topmost = selfTestOutput is not null;
        BuildStateGrid();

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
        Loaded += async (_, _) =>
        {
            _ = ReadEventsAsync();
            if (_selfTestOutput is not null)
            {
                await RunUnattendedSelfTestAsync(_selfTestOutput);
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

        MessagesBox.Text = string.Join(Environment.NewLine, d.Errors.Select(e => "ERROR  " + e).Concat(d.Notices.Select(n => "AVISO  " + n)));

        StartButton.IsEnabled = d.State is CaptureState.Stopped or CaptureState.Faulted && !_selfTestRunning;
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

    private async Task ReadEventsAsync()
    {
        try
        {
            await foreach (var e in _service.Events.ReadAllAsync(_closing.Token))
            {
                _log.Append(e);
                AppendLine($"#{e.Sequence} {e.Kind} ({e.Point.X}, {e.Point.Y}){(e.Cause != CancelCause.None ? " causa=" + e.Cause : string.Empty)}");
            }
        }
        catch (OperationCanceledException)
        {
            // Cierre de la ventana.
        }
    }

    private void AppendLine(string line)
    {
        EventsBox.AppendText(line + Environment.NewLine);
        EventsBox.ScrollToEnd();
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
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
            if (_closeRequestedDuringSelfTest)
            {
                Close(); // continúa el cierre que se había aplazado
            }
        }
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

        _refresh.Stop();
        _closing.Cancel();

        // Cancela cualquier gesto activo y quita los hooks antes de salir.
        _service.Dispose();
    }
}
