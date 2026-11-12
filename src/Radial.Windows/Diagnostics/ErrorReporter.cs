using System.Globalization;
using Radial.Core.Diagnostics;
using Radial.Core.Timing;

namespace Radial.Windows.Diagnostics;

/// <summary>El veredicto sobre un error: cuánto compromete a Radial y si hay que terminar.</summary>
/// <param name="Impact">La clasificación (<see cref="ErrorClassifier"/>).</param>
/// <param name="Terminate">
/// <c>true</c> si Radial debe cerrarse: el error es fatal, o es un error recuperable de la interfaz que ha agotado el presupuesto
/// (<see cref="ErrorBudget"/>) porque se repite sin parar.
/// </param>
public readonly record struct ErrorDecision(ErrorImpact Impact, bool Terminate);

/// <summary>
/// El punto único por el que pasa todo error de Radial, venga de donde venga (hilo de interfaz, otro hilo, tarea sin observar,
/// arranque, cierre, acciones, almacenamiento, captura). Hace siempre, en este orden:
///
/// 1. CLASIFICA (<see cref="ErrorClassifier"/>) y decide si hay que terminar (fatal, o presupuesto de errores de la interfaz agotado).
/// 2. REGISTRA en el log persistente, con el origen, la operación en curso, el rastro de las últimas operaciones y la excepción
///    completa. Siempre, aunque el aviso al usuario falle; sin limitación (la retención del log acota el disco).
/// 3. AVISA al usuario, con un texto escrito para personas (nunca el de la excepción), salvo si el error es del propio aviso. Los
///    repetidos se agrupan (<see cref="NotificationThrottle"/>). Un error recuperable es un <see cref="UserNoticeKind.Notice"/>; uno que
///    compromete el funcionamiento o fuerza el cierre, un <see cref="UserNoticeKind.Problem"/>.
/// 4. Si hay que terminar, deja el marcador de cierre grave (<see cref="IFatalMarker"/>) y lo comunica UNA vez con
///    <see cref="TerminationRequested"/> (salvo si el error ocurrió en un hilo que el runtime ya está terminando).
///
/// Nada de esto lanza ni bloquea: ni el registro, ni el aviso, ni quien escucha <see cref="TerminationRequested"/> pueden provocar un
/// segundo fallo. Un fallo del propio reportero se cuenta (<see cref="SelfFailures"/>) y se intenta anotar.
/// </summary>
public sealed class ErrorReporter
{
    public const string DiagnosticHint = "Más detalles en la sección Diagnóstico y en la carpeta de registros.";

    private readonly IRadialLog _log;
    private readonly IUserNotifier _notifier;
    private readonly IFatalMarker _marker;
    private readonly OperationTrail? _trail;
    private readonly ErrorBudget _uiBudget;
    private readonly NotificationThrottle _throttle;
    private readonly Func<DateTimeOffset> _now;
    private int _terminating;
    private int _selfFailures;

    public ErrorReporter(
        IRadialLog log,
        IUserNotifier notifier,
        IClock clock,
        OperationTrail? trail = null,
        IFatalMarker? marker = null,
        ErrorBudget? uiBudget = null,
        NotificationThrottle? throttle = null,
        Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(clock);

        _log = log;
        _notifier = notifier;
        _trail = trail;
        _marker = marker ?? NullFatalMarker.Instance;
        _uiBudget = uiBudget ?? new ErrorBudget(clock);
        _throttle = throttle ?? new NotificationThrottle(clock);
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>
    /// Radial debe cerrarse (se produce una sola vez, y nunca para un error de un hilo que ya está terminando el proceso). Quien
    /// escucha hace el cierre ordenado (soltar la captura, cerrar). Un escuchador que lance se ignora.
    /// </summary>
    public event Action<ErrorDecision>? TerminationRequested;

    /// <summary>Veces que el propio reportero falló (registro, aviso o cálculo), sin propagarlo.</summary>
    public int SelfFailures => Volatile.Read(ref _selfFailures);

    /// <summary>Radial ya está terminando por un error: no se vuelve a pedir el cierre.</summary>
    public bool IsTerminating => Volatile.Read(ref _terminating) != 0;

    /// <summary>
    /// Registra y comunica una excepción. <paramref name="source"/> es el componente («CaptureHost», «App»...), <paramref name="operation"/>
    /// lo que se estaba haciendo (si es nulo se usa la operación en curso del flujo, si la hay) y <paramref name="userMessage"/> el texto
    /// para el usuario (si es nulo se usa uno genérico según el origen).
    /// </summary>
    public ErrorDecision Report(Exception exception, ErrorOrigin origin, string source, string? operation = null, string? userMessage = null)
    {
        if (exception is null)
        {
            return new ErrorDecision(ErrorImpact.Recoverable, false);
        }

        ErrorImpact impact;
        bool terminate;
        var escalated = false;
        try
        {
            impact = ErrorClassifier.Classify(exception, origin);
            terminate = impact == ErrorImpact.Fatal;
            if (!terminate && origin == ErrorOrigin.UiThread && _uiBudget.Register())
            {
                terminate = true;
                escalated = true;
            }
        }
        catch (Exception failure)
        {
            NoteSelfFailure("clasificar un error", failure);
            return new ErrorDecision(ErrorImpact.Recoverable, false);
        }

        var decision = new ErrorDecision(impact, terminate);
        var effectiveOperation = string.IsNullOrWhiteSpace(operation) ? _trail?.Current ?? "(operación no registrada)" : operation;

        var level = terminate ? LogLevel.Critical : LogLevel.Error;
        var what = escalated
            ? $"Demasiados errores seguidos en la interfaz ({origin}); se termina para no seguir en un estado inestable."
            : terminate
                ? $"Error fatal ({origin}, {exception.GetType().Name})."
                : $"Error {Describe(impact)} ({origin}, {exception.GetType().Name}).";
        SafeLog(level, source, what, exception, effectiveOperation);

        if (terminate)
        {
            RecordFatal(origin, exception, effectiveOperation);
        }

        if (origin != ErrorOrigin.Notification)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{origin}|{exception.GetType().Name}|{effectiveOperation}");
            Notify(impact, terminate, userMessage ?? DefaultMessage(impact, origin, terminate), key);
        }

        if (terminate && origin != ErrorOrigin.BackgroundThread)
        {
            RequestTermination(decision);
        }

        return decision;
    }

    /// <summary>
    /// Comunica un fallo que no es una excepción (una acción que devolvió un error, la captura que se detuvo, un hook que dejó de
    /// responder). <paramref name="logMessage"/> no debe llevar datos del usuario; <paramref name="userMessage"/> es lo que se le dice.
    /// <paramref name="key"/> agrupa los repetidos.
    /// </summary>
    public void ReportProblem(ErrorImpact impact, string source, string operation, string logMessage, string userMessage, string key, Exception? exception = null)
    {
        try
        {
            var level = impact == ErrorImpact.Recoverable ? LogLevel.Warning : LogLevel.Error;
            SafeLog(level, source, logMessage, exception, operation);
            Notify(impact, terminate: false, userMessage, key);
        }
        catch (Exception failure)
        {
            NoteSelfFailure("comunicar un fallo", failure);
        }
    }

    /// <summary>
    /// Deja constancia en el registro de un fallo que no es una excepción, SIN avisar al usuario por ningún canal (ni ventana ni
    /// bandeja). Para lo que otra parte de la interfaz ya comunica por su cuenta. <paramref name="logMessage"/> no debe llevar datos
    /// del usuario. No lanza.
    /// </summary>
    public void RecordProblem(ErrorImpact impact, string source, string operation, string logMessage)
    {
        try
        {
            SafeLog(impact == ErrorImpact.Recoverable ? LogLevel.Warning : LogLevel.Error, source, logMessage, null, operation);
        }
        catch (Exception failure)
        {
            NoteSelfFailure("registrar un fallo", failure);
        }
    }

    /// <summary>
    /// Ejecuta trabajo asíncrono que nadie espera (se lanza y se olvida) sin que un fallo se pierda: una excepción se registra y se
    /// comunica según <paramref name="origin"/>; una cancelación es normal. La tarea devuelta nunca falla.
    /// </summary>
    public async Task RunGuardedAsync(Func<Task> work, ErrorOrigin origin, string source, string operation)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Una cancelación pedida (cierre, nuevo gesto) no es un error.
        }
        catch (Exception exception)
        {
            Report(exception, origin, source, operation);
        }
    }

    // ───────────────────────── Internos ─────────────────────────

    private void Notify(ErrorImpact impact, bool terminate, string userMessage, string key)
    {
        try
        {
            var decision = _throttle.Evaluate(key);
            if (!decision.Show)
            {
                return;
            }

            var problem = terminate || impact != ErrorImpact.Recoverable;
            var message = decision.Suppressed > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{userMessage} (se repitió {decision.Suppressed} veces desde el último aviso)")
                : userMessage;
            if (problem)
            {
                message = $"{message} {DiagnosticHint}";
            }

            _notifier.Notify(new UserNotice(problem ? UserNoticeKind.Problem : UserNoticeKind.Notice, problem ? "Radial: problema" : "Radial", message, key));
        }
        catch (Exception failure)
        {
            // El aviso falló: el error ya está en el registro. No se avisa de que el aviso falló (no hay bucle).
            NoteSelfFailure("avisar al usuario", failure);
        }
    }

    private void RecordFatal(ErrorOrigin origin, Exception exception, string operation)
    {
        try
        {
            _marker.Record(_now(), $"{origin}, {exception.GetType().FullName}, durante: {operation}");
        }
        catch (Exception failure)
        {
            NoteSelfFailure("anotar el cierre por error grave", failure);
        }
    }

    private void RequestTermination(ErrorDecision decision)
    {
        if (Interlocked.Exchange(ref _terminating, 1) != 0)
        {
            return;
        }

        var handlers = TerminationRequested;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<ErrorDecision>>())
        {
            try
            {
                handler(decision);
            }
            catch (Exception failure)
            {
                NoteSelfFailure("pedir el cierre", failure);
            }
        }
    }

    private void SafeLog(LogLevel level, string source, string message, Exception? exception, string operation)
    {
        try
        {
            _log.Write(level, source, message, exception, operation, _trail?.Snapshot());
        }
        catch (Exception)
        {
            // El registro falló: se cuenta y no hay otro sitio donde anotarlo.
            Interlocked.Increment(ref _selfFailures);
        }
    }

    private void NoteSelfFailure(string what, Exception failure)
    {
        Interlocked.Increment(ref _selfFailures);
        try
        {
            _log.Write(LogLevel.Warning, nameof(ErrorReporter), $"El sistema de errores falló al {what}.", failure);
        }
        catch
        {
            // Sin más recursos: no se propaga.
        }
    }

    private static string Describe(ErrorImpact impact) => impact switch
    {
        ErrorImpact.Recoverable => "recuperable",
        ErrorImpact.Degraded => "que compromete una función",
        _ => "fatal",
    };

    private static string DefaultMessage(ErrorImpact impact, ErrorOrigin origin, bool terminate)
    {
        if (terminate)
        {
            return "Radial ha encontrado un error grave y se va a cerrar.";
        }

        return (impact, origin) switch
        {
            (_, ErrorOrigin.Storage) => "No se pudo leer o guardar la configuración. Los cambios podrían no guardarse.",
            (_, ErrorOrigin.Capture) => "La captura del gesto no funciona correctamente. El radial puede no responder hasta que reinicies Radial.",
            (ErrorImpact.Degraded, _) => "Radial no puede funcionar con normalidad: una función quedó comprometida.",
            (_, ErrorOrigin.Action) => "No se pudo completar la operación. Radial sigue funcionando.",
            _ => "Ha ocurrido un error inesperado, pero Radial sigue funcionando.",
        };
    }
}
