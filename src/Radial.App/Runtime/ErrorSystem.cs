using System.IO;
using System.Windows;
using Radial.Core.Diagnostics;
using Radial.Core.Timing;
using Radial.Windows.Diagnostics;
using Radial.Windows.Storage;

namespace Radial.App.Runtime;

/// <summary>
/// La raíz de composición del sistema de errores de Radial (registro persistente, rastro de operaciones, reportero, avisos al usuario) y
/// de su captura global. Se crea lo PRIMERO en el arranque, antes que cualquier componente que pueda fallar, y se libera lo último
/// (vacía el registro). El diseño completo está en el documento técnico, sección 20.
///
/// Qué se captura y qué se hace (la clasificación está en <see cref="ErrorClassifier"/>):
/// - Excepción no controlada en el hilo de interfaz: se marca como controlada para decidir nosotros, se registra y se avisa; Radial
///   sigue, salvo que el error sea de un tipo fatal o se repita sin parar (presupuesto de errores), en cuyo caso se hace un cierre
///   ordenado (se sueltan los hooks ANTES de terminar, para no dejar el botón central consumido).
/// - Excepción no controlada en otro hilo: el runtime termina el proceso de todas formas; solo se registra (con vaciado a disco) y se
///   deja el marcador de cierre grave. No se intenta un cierre ordenado desde un hilo arbitrario con el estado posiblemente
///   corrupto (podría bloquear el cierre).
/// - Tarea con fallo que nadie observó: se registra, se avisa y se marca como observada; el runtime no termina por esto.
/// - Cierre grave: queda un marcador en la carpeta de registros y en el siguiente arranque se avisa al usuario (cuando en el momento del
///   fallo no había nadie a quien decírselo).
/// </summary>
internal sealed class ErrorSystem : IDisposable
{
    private readonly Application? _application;
    private Action? _emergencyShutdown;
    private int _terminationStarted;
    private bool _installed;

    public ErrorSystem(string logDirectory, Application? application = null)
    {
        _application = application;
        LogDirectory = Path.GetFullPath(logDirectory);

        var sanitizer = new LogSanitizer(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Log = new FileRadialLog(LogDirectory, sanitizer);
        Trail = new OperationTrail();
        Marker = new FileFatalMarker(LogDirectory, Log);

        var dispatcher = (application ?? Application.Current)?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Presence = new WindowPresence();
        InAppSink = new ShellNoticeSink(dispatcher, Log);
        TraySink = new TrayNoticeSink(dispatcher, Log);
        Router = new NotificationRouter(Log, Presence, InAppSink, TraySink);
        Reporter = new ErrorReporter(Log, Router, new MonotonicClock(), Trail, Marker);
    }

    /// <summary>La carpeta de registros: <c>%LOCALAPPDATA%\Radial\logs</c>.</summary>
    public static string DefaultLogDirectory => Path.Combine(FileSettingsStore.DefaultRootDirectory, "logs");

    public string LogDirectory { get; }

    public FileRadialLog Log { get; }

    public OperationTrail Trail { get; }

    public FileFatalMarker Marker { get; }

    public ErrorReporter Reporter { get; }

    public NotificationRouter Router { get; }

    public WindowPresence Presence { get; }

    public ShellNoticeSink InAppSink { get; }

    public TrayNoticeSink TraySink { get; }

    /// <summary>Instala la captura global. Una sola vez, lo primero que se hace con la aplicación.</summary>
    public void Install()
    {
        if (_installed || _application is null)
        {
            return;
        }

        _installed = true;
        _application.DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;
        Reporter.TerminationRequested += OnTerminationRequested;
        Log.Info(nameof(ErrorSystem), "Sistema de errores activo.", "arranque");
    }

    /// <summary>
    /// Qué hay que hacer para soltar la captura y cerrar con orden si Radial tiene que terminar por un error. Lo registra quien tiene
    /// los hooks (el anfitrión de la captura y, después, la ventana principal).
    /// </summary>
    public void SetEmergencyShutdown(Action shutdown) => _emergencyShutdown = shutdown;

    /// <summary>Si la ejecución anterior terminó por un error grave, avisa ahora al usuario (una sola vez).</summary>
    public void AnnouncePreviousFatal()
    {
        var previous = Marker.TakePending();
        if (previous is null)
        {
            return;
        }

        Log.Write(LogLevel.Warning, nameof(ErrorSystem), "La ejecución anterior de Radial terminó por un error grave.", operation: "arranque", context: previous);
        Reporter.ReportProblem(
            ErrorImpact.Degraded,
            nameof(ErrorSystem),
            "arranque",
            "Aviso de cierre grave de la ejecución anterior.",
            "Radial se cerró por un error inesperado la última vez que se usó.",
            "previous-fatal");
    }

    public void Dispose()
    {
        if (_installed && _application is not null)
        {
            _application.DispatcherUnhandledException -= OnDispatcherUnhandled;
            AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandled;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTask;
            Reporter.TerminationRequested -= OnTerminationRequested;
        }

        Log.Info(nameof(ErrorSystem), "Sistema de errores detenido.", "cierre");
        Log.Dispose(); // vacía la cola: no se pierden los últimos registros
    }

    // ───────────────────────── Captura global ─────────────────────────

    private void OnDispatcherUnhandled(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Se marca como controlada SIEMPRE: la decisión (seguir o cerrar con orden) es del reportero. Dejarla sin controlar mataría el
        // proceso sin soltar la captura global ni escribir nada.
        e.Handled = true;
        Reporter.Report(e.Exception, ErrorOrigin.UiThread, "Interfaz");
    }

    private void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new InvalidOperationException("Excepción no controlada que no deriva de Exception.");
        Reporter.Report(exception, ErrorOrigin.BackgroundThread, "AppDomain");
    }

    private void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Reporter.Report(e.Exception, ErrorOrigin.UnobservedTask, "TaskScheduler");
    }

    private void OnTerminationRequested(ErrorDecision decision)
    {
        var application = _application;
        if (application is null || Interlocked.Exchange(ref _terminationStarted, 1) != 0)
        {
            return;
        }

        // El cierre ordenado corre en el hilo de interfaz, que es donde viven la ventana y el servicio de captura.
        if (application.Dispatcher.CheckAccess())
        {
            Terminate(application);
        }
        else
        {
            application.Dispatcher.BeginInvoke(() => Terminate(application));
        }
    }

    private void Terminate(Application application)
    {
        try
        {
            _emergencyShutdown?.Invoke();
        }
        catch (Exception exception)
        {
            // Un fallo al soltar la captura es importante pero no impide terminar.
            Log.Write(LogLevel.Error, nameof(ErrorSystem), "Falló el cierre ordenado tras un error grave.", exception, "cierre por error grave");
        }

        try
        {
            application.Shutdown(1);
        }
        catch (Exception exception)
        {
            Log.Write(LogLevel.Error, nameof(ErrorSystem), "Falló Application.Shutdown tras un error grave.", exception, "cierre por error grave");
            Environment.Exit(1);
        }
    }
}
