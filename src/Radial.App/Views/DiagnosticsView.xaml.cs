using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Radial.App.Runtime;
using Radial.Windows.Input;

namespace Radial.App.Views;

/// <summary>
/// Sección de diagnóstico: el estado de la captura global, del gesto y del radial, y la autoprueba con entrada sintética. Solo refleja lo
/// que informa el servicio: si un hook falló o la captura está detenida, lo dice. Toda la lógica de ejecución vive en
/// <see cref="CaptureHost"/>; esta vista solo la muestra y lanza lo que el usuario pide.
/// </summary>
public partial class DiagnosticsView : UserControl
{
    private readonly Dictionary<string, TextBlock> _fields = [];
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CaptureHost? _host;
    private DiagnosticsPage? _page;
    private bool _started;

    public DiagnosticsView()
    {
        InitializeComponent();
        BuildStateGrid();
        _refresh.Tick += (_, _) => RefreshState();
    }

    private CaptureHost Host => _host ?? throw new InvalidOperationException("La vista de diagnóstico no tiene datos.");

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _page = DataContext as DiagnosticsPage;
        _host = _page?.Host;
        if (_host is null)
        {
            return;
        }

        _host.LineLogged += OnLine;
        EventsBox.Text = string.Join(Environment.NewLine, _host.Lines) + (_host.Lines.Count > 0 ? Environment.NewLine : string.Empty);
        EventsBox.ScrollToEnd();

        // Los eventos que la aplicación recibe de verdad (para comprobar qué eventos se consumieron).
        var window = Window.GetWindow(this);
        if (window is not null)
        {
            window.PreviewMouseDown += OnWindowMouseDown;
            window.PreviewMouseUp += OnWindowMouseUp;
            window.PreviewKeyDown += OnWindowKeyDown;
        }

        _refresh.Start();
        RefreshState();

        // La autoprueba desatendida (--selftest) arranca cuando esta vista ya está en pantalla: necesita la zona de prueba.
        if (_page?.SelfTestOutput is { } output && !_started)
        {
            _started = true;
            _ = RunUnattendedSelfTestAsync(output);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refresh.Stop();
        if (_host is not null)
        {
            _host.LineLogged -= OnLine;
        }

        var window = Window.GetWindow(this);
        if (window is not null)
        {
            window.PreviewMouseDown -= OnWindowMouseDown;
            window.PreviewMouseUp -= OnWindowMouseUp;
            window.PreviewKeyDown -= OnWindowKeyDown;
        }
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            Host.Observed.MiddleDowns++;
        }
    }

    private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            Host.Observed.MiddleUps++;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Host.Observed.EscapeDowns++;
        }
    }

    private void OnLine(string line)
    {
        EventsBox.AppendText(line + Environment.NewLine);
        EventsBox.ScrollToEnd();
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
            var label = new TextBlock { Text = name, Margin = new Thickness(0, 3, 12, 3), Style = (Style)FindResource("Text.Secondary") };
            var value = new TextBlock { Margin = new Thickness(0, 3, 0, 3), Style = (Style)FindResource("Text.Mono"), TextWrapping = TextWrapping.Wrap };
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
        if (_host is null)
        {
            return;
        }

        var service = Host.Service;
        var d = service.GetDiagnostics();
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
        Set("Radial (overlay)", Host.Coordinator.IsIsolated ? "aislado (autoprueba en curso)" : Host.Coordinator.IsActive ? "visible" : "oculto");
        var styles = Host.Overlay.Styles;
        Set("Overlay (estilos)", $"0x{styles.Raw:X8}  layered={styles.Layered}, click-through={styles.Transparent}, sin activar={styles.NoActivate}, herramienta={styles.ToolWindow}");
        Set("Última acción", Host.LastAction);
        Set("Lector de eventos", DescribeReader());

        var readerEnd = Host.Supervisor.End;
        IEnumerable<string> readerProblem = readerEnd is { Reason: not GestureReaderEndReason.Cancelled } ? ["ERROR  " + GestureReaderSupervisor.Describe(readerEnd)] : [];
        MessagesBox.Text = string.Join(Environment.NewLine, d.Errors.Select(e => "ERROR  " + e).Concat(readerProblem).Concat(d.Notices.Select(n => "AVISO  " + n)));

        // Con el lector muerto no se puede volver a iniciar la captura (nadie procesaría los eventos): hay que reabrir la aplicación.
        var readerFailed = Host.Supervisor.Failure is not null;
        StartButton.IsEnabled = d.State is CaptureState.Stopped or CaptureState.Faulted && !Host.SelfTestRunning && !readerFailed;
        StopButton.IsEnabled = d.State != CaptureState.Stopped && !Host.SelfTestRunning;
        CancelButton.IsEnabled = running;
        SelfTestButton.IsEnabled = running && !Host.SelfTestRunning;
    }

    private static string Describe(HookStatusInfo hook) =>
        !hook.Attempted ? "no instalado"
        : hook.Installed ? "instalado (no se puede verificar que Windows no lo haya eliminado)"
        : hook.Win32Error != 0 ? $"NO instalado (error {hook.Win32Error})"
        : "no instalado";

    private static string Pressed(bool value) => value ? "pulsado" : "suelto";

    private string DescribeReader()
    {
        if (Host.Supervisor.End is { } end)
        {
            return end.Reason == GestureReaderEndReason.Cancelled ? "detenido (cierre de la ventana)" : "DETENIDO: " + GestureReaderSupervisor.Describe(end);
        }

        if (!Host.ReaderStarted)
        {
            return "sin iniciar";
        }

        return Host.Reader.TotalFailures == 0
            ? "en marcha"
            : $"en marcha; {Host.Reader.TotalFailures} fallo(s) recuperado(s), último: {Host.ReaderLastFailure}";
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        Host.StartCapture();
        RefreshState();
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        Host.StopCapture();
        RefreshState();
    }

    private void OnQuery(object sender, RoutedEventArgs e)
    {
        var d = Host.Service.GetDiagnostics();
        Host.Log($"[estado] {d.State}; teclado={Describe(d.KeyboardHook)}; ratón={Describe(d.MouseHook)}; fase={d.Phase}; cursor={d.Pointer}; " +
                 $"callbacks={d.KeyboardCallbacks}/{d.MouseCallbacks}; máx={d.MaxKeyboardCallbackMs:F3}/{d.MaxMouseCallbackMs:F3} ms; errores={d.Errors.Count}");
    }

    private void OnCancel(object sender, RoutedEventArgs e) =>
        Host.Log(Host.Service.RequestCancel()
            ? "Cancelación manual: había un gesto activo."
            : "Cancelación manual: no hay ningún gesto activo. (Para probarla, la autoprueba la ejecuta con un gesto sintético.)");

    private async void OnSelfTest(object sender, RoutedEventArgs e)
    {
        Host.Log("=== Autoprueba con entrada SINTÉTICA (~12 s). No uses el ratón ni el teclado. Cerrar la ventana la cancela. ===");
        try
        {
            var steps = await Host.ExecuteSelfTestAsync(ZoneCenter, ActivateWindow);
            foreach (var step in steps)
            {
                Host.Log($"[{(step.Passed ? "OK" : "FALLO")}] {step.Name}: {step.Detail}");
            }

            var failed = steps.Count(s => !s.Passed);
            Host.Log(failed == 0
                ? "=== Autoprueba: TODO CORRECTO (entrada sintética; falta la prueba manual con hardware) ==="
                : $"=== Autoprueba: {failed} paso(s) con FALLOS ===");
        }
        catch (Exception exception)
        {
            Host.Log("La autoprueba se interrumpió: " + exception.Message);
        }
    }

    private async Task RunUnattendedSelfTestAsync(string path)
    {
        var lines = new List<string> { $"Autoprueba sintética {DateTime.Now:yyyy-MM-dd HH:mm:ss}" };
        var exitCode = 1;
        try
        {
            await Task.Delay(800);
            var start = Host.Service.Start();
            lines.Add($"Inicio de la captura: {(start.Success ? "correcto" : "FALLO: " + start.Message)}");
            if (start.Success)
            {
                var steps = await Host.ExecuteSelfTestAsync(ZoneCenter, ActivateWindow);
                lines.AddRange(steps.Select(s => $"[{(s.Passed ? "OK" : "FALLO")}] {s.Name}: {s.Detail}"));
                exitCode = steps.All(s => s.Passed) ? 0 : 2;
                var d = Host.Service.GetDiagnostics();
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

    /// <summary>La zona se mide al empezar: el ratón y el teclado no se tocan hasta que la autoprueba la ha comprobado.</summary>
    private ScreenPoint ZoneCenter()
    {
        var zone = TestZone.PointToScreen(new Point(TestZone.ActualWidth / 2, TestZone.ActualHeight / 2));
        return new ScreenPoint((int)Math.Round(zone.X), (int)Math.Round(zone.Y));
    }

    private void ActivateWindow() => Window.GetWindow(this)?.Activate();
}
