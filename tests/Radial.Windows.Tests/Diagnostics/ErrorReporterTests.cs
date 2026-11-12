using Radial.Core.Diagnostics;
using Radial.Windows.Diagnostics;

namespace Radial.Windows.Tests.Diagnostics;

/// <summary>
/// El punto único de los errores: clasifica, registra, avisa (agrupando repetidos) y decide si hay que terminar. Todo con registro,
/// avisos, marcador y reloj falsos: sin disco, sin bandeja y sin esperas reales.
/// </summary>
public class ErrorReporterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 10, 14, 0, 0, TimeSpan.Zero);

    private sealed class Rig
    {
        public Rig(IRadialLog? log = null, IUserNotifier? notifier = null, IFatalMarker? marker = null)
        {
            Log = log ?? Logged;
            Notifier = notifier ?? Notices;
            Marker = marker ?? Markers;
            Reporter = new ErrorReporter(Log, Notifier, Clock, Trail, Marker, now: () => FixedNow);
        }

        public ListLog Logged { get; } = new();

        public RecordingNotifier Notices { get; } = new();

        public RecordingMarker Markers { get; } = new();

        public DiagClock Clock { get; } = new();

        public OperationTrail Trail { get; } = new(() => FixedNow);

        public IRadialLog Log { get; }

        public IUserNotifier Notifier { get; }

        public IFatalMarker Marker { get; }

        public ErrorReporter Reporter { get; }

        public List<ErrorDecision> Terminations { get; } = [];

        public Rig ListenForTermination()
        {
            Reporter.TerminationRequested += Terminations.Add;
            return this;
        }
    }

    // ───────────────────────── Registro y aviso ─────────────────────────

    [Fact]
    public void ARecoverableError_IsLoggedInFull_AndNotifiedAsANoticeWithoutTechnicalText()
    {
        var rig = new Rig();
        var exception = new InvalidOperationException(@"detalle secreto C:\Users\x\a.txt");

        var decision = rig.Reporter.Report(exception, ErrorOrigin.Action, "Origen", "guardar algo");

        Assert.Equal(new ErrorDecision(ErrorImpact.Recoverable, false), decision);
        var entry = Assert.Single(rig.Logged.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Origen", entry.Source);
        Assert.Equal("guardar algo", entry.Operation);
        Assert.Same(exception, entry.Exception);
        var notice = Assert.Single(rig.Notices.Notices);
        Assert.Equal(UserNoticeKind.Notice, notice.Kind);
        Assert.DoesNotContain("secreto", notice.Message);
        Assert.DoesNotContain("InvalidOperationException", notice.Message);
        Assert.DoesNotContain(ErrorReporter.DiagnosticHint, notice.Message); // un aviso recuperable no manda a los registros
    }

    [Fact]
    public void WithoutAnExplicitOperation_TheOneInProgressInTheFlowIsUsed_AndTheTrailIsAttached()
    {
        var rig = new Rig();
        using (rig.Trail.Begin("procesar gesto #4"))
        {
            rig.Reporter.Report(new InvalidOperationException(), ErrorOrigin.Action, "Origen");
        }

        var entry = Assert.Single(rig.Logged.Entries);
        Assert.Equal("procesar gesto #4", entry.Operation);
        Assert.Contains("procesar gesto #4", entry.Context);
    }

    [Fact]
    public void WithoutAnyOperation_ItSaysSoInsteadOfLeavingItBlank()
    {
        var rig = new Rig();

        rig.Reporter.Report(new InvalidOperationException(), ErrorOrigin.Action, "Origen");

        Assert.Equal("(operación no registrada)", Assert.Single(rig.Logged.Entries).Operation);
    }

    [Fact]
    public void ADegradedError_IsAProblem_WithTheDiagnosticHint_AndItDoesNotTerminate()
    {
        var rig = new Rig().ListenForTermination();

        var decision = rig.Reporter.Report(new IOException("disco"), ErrorOrigin.Storage, "Almacén", "guardar la configuración");

        Assert.Equal(new ErrorDecision(ErrorImpact.Degraded, false), decision);
        var notice = Assert.Single(rig.Notices.Notices);
        Assert.Equal(UserNoticeKind.Problem, notice.Kind);
        Assert.Contains(ErrorReporter.DiagnosticHint, notice.Message);
        Assert.Empty(rig.Terminations);
        Assert.Empty(rig.Markers.Records);
    }

    [Fact]
    public void ACustomUserMessage_ReplacesTheGenericOne()
    {
        var rig = new Rig();

        rig.Reporter.Report(new InvalidOperationException(), ErrorOrigin.Action, "Origen", "op", "Mensaje a medida.");

        Assert.Equal("Mensaje a medida.", Assert.Single(rig.Notices.Notices).Message);
    }

    // ───────────────────────── Errores fatales y presupuesto ─────────────────────────

    [Fact]
    public void AFatalError_RequestsTermination_OnlyOnce_LeavesTheMarker_AndIsLoggedAsCritical()
    {
        var rig = new Rig().ListenForTermination();

        var first = rig.Reporter.Report(new OutOfMemoryException("sin memoria"), ErrorOrigin.UiThread, "Interfaz", "pintar");
        var second = rig.Reporter.Report(new AccessViolationException(), ErrorOrigin.UiThread, "Interfaz", "pintar");

        Assert.Equal(new ErrorDecision(ErrorImpact.Fatal, true), first);
        Assert.True(second.Terminate); // el veredicto sigue siendo «terminar»...
        Assert.Single(rig.Terminations); // ...pero el cierre se pide una sola vez
        Assert.True(rig.Reporter.IsTerminating);
        Assert.All(rig.Logged.Entries, entry => Assert.Equal(LogLevel.Critical, entry.Level));
        Assert.Equal(2, rig.Markers.Records.Count);
        var record = rig.Markers.Records[0];
        Assert.Equal(FixedNow, record.When);
        Assert.Contains("OutOfMemoryException", record.Summary);
        Assert.Contains("pintar", record.Summary);
        Assert.DoesNotContain("sin memoria", record.Summary); // el marcador nunca guarda el mensaje de la excepción
        Assert.Equal(UserNoticeKind.Problem, rig.Notices.Notices[0].Kind);
    }

    [Fact]
    public void AnErrorOnAnotherThread_IsFatal_ButNoOrderedShutdownIsRequestedFromThere()
    {
        var rig = new Rig().ListenForTermination();

        var decision = rig.Reporter.Report(new InvalidOperationException("en un hilo"), ErrorOrigin.BackgroundThread, "AppDomain", "tarea");

        Assert.Equal(new ErrorDecision(ErrorImpact.Fatal, true), decision);
        Assert.Empty(rig.Terminations);
        Assert.Single(rig.Markers.Records);
        Assert.Equal(LogLevel.Critical, Assert.Single(rig.Logged.Entries).Level);
    }

    [Fact]
    public void AStartupError_IsFatal_AndRequestsTermination()
    {
        var rig = new Rig().ListenForTermination();

        var decision = rig.Reporter.Report(new InvalidOperationException(), ErrorOrigin.Startup, "App", "arranque");

        Assert.True(decision.Terminate);
        Assert.Single(rig.Terminations);
        Assert.Single(rig.Markers.Records);
    }

    [Fact]
    public void RepeatedInterfaceErrors_ExhaustTheBudget_AndEscalateToTermination()
    {
        var rig = new Rig().ListenForTermination();

        for (var count = 1; count <= 5; count++)
        {
            Assert.False(rig.Reporter.Report(new InvalidOperationException(), ErrorOrigin.UiThread, "Interfaz", "bucle").Terminate);
        }

        var sixth = rig.Reporter.Report(new InvalidOperationException(), ErrorOrigin.UiThread, "Interfaz", "bucle");

        Assert.True(sixth.Terminate);
        Assert.Equal(ErrorImpact.Recoverable, sixth.Impact); // el error en sí era recuperable: lo que se agotó fue la paciencia
        Assert.Single(rig.Terminations);
        var last = rig.Logged.Entries[^1];
        Assert.Equal(LogLevel.Critical, last.Level);
        Assert.Contains("Demasiados errores", last.Message);
        Assert.Single(rig.Markers.Records);
    }

    [Fact]
    public void InterfaceErrorsSpreadOverTime_NeverExhaustTheBudget()
    {
        var rig = new Rig().ListenForTermination();

        for (var count = 0; count < 40; count++)
        {
            Assert.False(rig.Reporter.Report(new InvalidOperationException(), ErrorOrigin.UiThread, "Interfaz", "esporádico").Terminate);
            rig.Clock.Advance(TimeSpan.FromSeconds(20));
        }

        Assert.Empty(rig.Terminations);
    }

    [Fact]
    public void OnlyInterfaceErrorsCountAgainstTheBudget()
    {
        var rig = new Rig().ListenForTermination();

        for (var count = 0; count < 30; count++)
        {
            Assert.False(rig.Reporter.Report(new InvalidOperationException(), ErrorOrigin.Action, "Origen", "acción").Terminate);
        }

        Assert.Empty(rig.Terminations);
    }

    // ───────────────────────── Tormentas de avisos ─────────────────────────

    [Fact]
    public void TheSameError_RepeatedManyTimes_IsLoggedEveryTime_ButNotifiedOnce_UntilTheWindowPasses()
    {
        var rig = new Rig();

        for (var count = 0; count < 4; count++)
        {
            rig.Reporter.Report(new InvalidOperationException("otra vez"), ErrorOrigin.Action, "Origen", "operación repetida");
        }

        Assert.Equal(4, rig.Logged.Entries.Count); // el registro lo guarda todo
        Assert.Single(rig.Notices.Notices);

        rig.Clock.Advance(TimeSpan.FromSeconds(31));
        rig.Reporter.Report(new InvalidOperationException("otra vez"), ErrorOrigin.Action, "Origen", "operación repetida");

        Assert.Equal(2, rig.Notices.Notices.Count);
        Assert.Contains("se repitió 3 veces", rig.Notices.Notices[1].Message);
    }

    [Fact]
    public void ManyDifferentErrors_AreCappedPerMinute_ButAllAreLogged()
    {
        var rig = new Rig();

        for (var index = 0; index < 10; index++)
        {
            rig.Reporter.Report(new InvalidOperationException(), ErrorOrigin.Action, "Origen", "operación " + index);
        }

        Assert.Equal(10, rig.Logged.Entries.Count);
        Assert.Equal(NotificationThrottle.DefaultMaxPerWindow, rig.Notices.Notices.Count);
    }

    // ───────────────────────── Fallos del propio sistema ─────────────────────────

    [Fact]
    public void ABrokenLog_NeverPropagates_AndTheNoticeStillGoesOut()
    {
        var log = new ThrowingLog();
        var rig = new Rig(log: log);

        var thrown = Record.Exception(() => rig.Reporter.Report(new InvalidOperationException(), ErrorOrigin.Action, "Origen", "op"));

        Assert.Null(thrown);
        Assert.True(log.Calls >= 1);
        Assert.Single(rig.Notices.Notices);
        Assert.True(rig.Reporter.SelfFailures >= 1);
    }

    [Fact]
    public void ABrokenNotifier_NeverPropagates_AndTheErrorIsAlreadyInTheLog()
    {
        var notifier = new ThrowingNotifier();
        var rig = new Rig(notifier: notifier);

        var thrown = Record.Exception(() => rig.Reporter.Report(new InvalidOperationException("x"), ErrorOrigin.Action, "Origen", "op"));

        Assert.Null(thrown);
        Assert.Equal(1, notifier.Calls);
        Assert.Contains(rig.Logged.Entries, entry => entry.Source == "Origen" && entry.Level == LogLevel.Error);
        Assert.Contains(rig.Logged.Entries, entry => entry.Source == nameof(ErrorReporter) && entry.Message.Contains("avisar al usuario", StringComparison.Ordinal));
        Assert.True(rig.Reporter.SelfFailures >= 1);
    }

    [Fact]
    public void ABrokenLogAndNotifierTogether_StillDoNotPropagate()
    {
        var rig = new Rig(log: new ThrowingLog(), notifier: new ThrowingNotifier());

        var thrown = Record.Exception(() =>
        {
            rig.Reporter.Report(new OutOfMemoryException(), ErrorOrigin.UiThread, "Interfaz", "op");
            rig.Reporter.ReportProblem(ErrorImpact.Degraded, "Origen", "op", "log", "usuario", "clave");
        });

        Assert.Null(thrown);
    }

    [Fact]
    public void AnErrorInTheNotificationItself_IsLogged_ButNeverNotifiedAgain()
    {
        var rig = new Rig();

        var decision = rig.Reporter.Report(new OutOfMemoryException(), ErrorOrigin.Notification, "Bandeja", "mostrar un globo");

        Assert.Equal(ErrorImpact.Recoverable, decision.Impact);
        Assert.False(decision.Terminate);
        Assert.Single(rig.Logged.Entries);
        Assert.Empty(rig.Notices.Notices);
    }

    [Fact]
    public void AMarkerThatCannotBeWritten_DoesNotStopTheTermination()
    {
        var rig = new Rig(marker: new ThrowingMarker()).ListenForTermination();

        var decision = rig.Reporter.Report(new OutOfMemoryException(), ErrorOrigin.UiThread, "Interfaz", "op");

        Assert.True(decision.Terminate);
        Assert.Single(rig.Terminations);
        Assert.True(rig.Reporter.SelfFailures >= 1);
    }

    [Fact]
    public void ATerminationHandlerThatThrows_IsIgnored_AndTheOthersStillRun()
    {
        var rig = new Rig();
        var reached = false;
        rig.Reporter.TerminationRequested += _ => throw new InvalidOperationException("manejador roto (prueba)");
        rig.Reporter.TerminationRequested += _ => reached = true;

        var thrown = Record.Exception(() => rig.Reporter.Report(new OutOfMemoryException(), ErrorOrigin.UiThread, "Interfaz", "op"));

        Assert.Null(thrown);
        Assert.True(reached);
    }

    [Fact]
    public void ANullException_IsIgnored()
    {
        var rig = new Rig();

        var decision = rig.Reporter.Report(null!, ErrorOrigin.Action, "Origen", "op");

        Assert.Equal(ErrorImpact.Recoverable, decision.Impact);
        Assert.Empty(rig.Logged.Entries);
    }

    // ───────────────────────── Fallos que no son excepciones ─────────────────────────

    [Fact]
    public void ReportProblem_UsesWarningForRecoverable_ErrorForDegraded_AndTheMatchingNoticeKind()
    {
        var rig = new Rig();

        rig.Reporter.ReportProblem(ErrorImpact.Recoverable, "Origen", "ejecutar una acción", "La acción X falló (código)", "«X» no se pudo ejecutar.", "k1");
        rig.Reporter.ReportProblem(ErrorImpact.Degraded, "Origen", "iniciar la captura", "No se pudo iniciar", "La captura no funciona.", "k2");

        Assert.Equal(LogLevel.Warning, rig.Logged.Entries[0].Level);
        Assert.Equal(LogLevel.Error, rig.Logged.Entries[1].Level);
        Assert.Equal(UserNoticeKind.Notice, rig.Notices.Notices[0].Kind);
        Assert.Equal("«X» no se pudo ejecutar.", rig.Notices.Notices[0].Message);
        Assert.Equal(UserNoticeKind.Problem, rig.Notices.Notices[1].Kind);
        Assert.StartsWith("La captura no funciona.", rig.Notices.Notices[1].Message);
        Assert.Contains(ErrorReporter.DiagnosticHint, rig.Notices.Notices[1].Message);
    }

    [Fact]
    public void RecordProblem_LogsWithoutNotifyingAnyChannel()
    {
        var rig = new Rig();

        rig.Reporter.RecordProblem(ErrorImpact.Recoverable, "Origen", "ejecutar una acción", "La acción x falló con el código TargetNotFound.");
        rig.Reporter.RecordProblem(ErrorImpact.Degraded, "Origen", "otra operación", "Algo más grave.");

        Assert.Equal(2, rig.Logged.Entries.Count);
        Assert.Equal(LogLevel.Warning, rig.Logged.Entries[0].Level);
        Assert.Equal(LogLevel.Error, rig.Logged.Entries[1].Level);
        Assert.Empty(rig.Notices.Notices);
    }

    [Fact]
    public void RecordProblem_NeverThrows_EvenIfTheLogFails()
    {
        var rig = new Rig(log: new ThrowingLog());

        var thrown = Record.Exception(() => rig.Reporter.RecordProblem(ErrorImpact.Recoverable, "Origen", "op", "mensaje"));

        Assert.Null(thrown);
    }

    [Fact]
    public void ReportProblem_GroupsRepeatsByItsKey()
    {
        var rig = new Rig();

        for (var count = 0; count < 3; count++)
        {
            rig.Reporter.ReportProblem(ErrorImpact.Recoverable, "Origen", "op", "log", "usuario", "misma-clave");
        }

        Assert.Equal(3, rig.Logged.Entries.Count);
        Assert.Single(rig.Notices.Notices);
    }

    // ───────────────────────── Trabajo en segundo plano ─────────────────────────

    [Fact]
    public async Task ABackgroundTaskThatFails_IsReportedAndCompletesNormally()
    {
        var rig = new Rig();

        await rig.Reporter.RunGuardedAsync(
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("falló en segundo plano");
            },
            ErrorOrigin.Capture,
            "Lector",
            "supervisar el lector");

        var entry = Assert.Single(rig.Logged.Entries);
        Assert.Equal("supervisar el lector", entry.Operation);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Equal(UserNoticeKind.Problem, Assert.Single(rig.Notices.Notices).Kind); // Capture es degradado
    }

    [Fact]
    public async Task ACancelledBackgroundTask_IsNotAnError()
    {
        var rig = new Rig();

        await rig.Reporter.RunGuardedAsync(
            async () =>
            {
                await Task.Yield();
                throw new OperationCanceledException();
            },
            ErrorOrigin.Action,
            "Origen",
            "op");

        Assert.Empty(rig.Logged.Entries);
        Assert.Empty(rig.Notices.Notices);
    }

    [Fact]
    public async Task ASuccessfulBackgroundTask_IsLeftAlone()
    {
        var rig = new Rig();
        var ran = false;

        await rig.Reporter.RunGuardedAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        }, ErrorOrigin.Action, "Origen", "op");

        Assert.True(ran);
        Assert.Empty(rig.Logged.Entries);
    }

    [Fact]
    public async Task ManyThreadsReportingAtOnce_AreAllLogged_WithoutErrors()
    {
        var rig = new Rig();

        await Task.WhenAll(Enumerable.Range(0, 40).Select(index => Task.Run(() =>
            rig.Reporter.Report(new InvalidOperationException(), ErrorOrigin.Action, "Hilo", "operación " + index))));

        Assert.Equal(40, rig.Logged.Entries.Count);
        Assert.Equal(0, rig.Reporter.SelfFailures);
    }

    [Fact]
    public void TheConstructorNeedsItsCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() => new ErrorReporter(null!, new RecordingNotifier(), new DiagClock()));
        Assert.Throws<ArgumentNullException>(() => new ErrorReporter(new ListLog(), null!, new DiagClock()));
        Assert.Throws<ArgumentNullException>(() => new ErrorReporter(new ListLog(), new RecordingNotifier(), null!));
    }
}
