using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Windows.Actions;

namespace Radial.Windows.Tests.Actions;

/// <summary>
/// El contrato de <see cref="IActionExecutor"/> en los cuatro ejecutores que abren algo con el sistema, siempre con un lanzador
/// falso. Cada prueba se repite para aplicación, archivo, carpeta y URL.
/// </summary>
public class ProcessExecutorContractTests : IClassFixture<TempFileSystem>
{
    public static TheoryData<string> Kinds => ["application", "file", "folder", "url"];

    private readonly TempFileSystem _fs;

    public ProcessExecutorContractTests(TempFileSystem fs)
    {
        _fs = fs;
    }

    // Todo lo que cambia entre ejecutores: cómo crearlo, una acción válida, una inválida y el texto del destino (para
    // comprobar que los mensajes no lo reproducen).
    private sealed record Case(Func<IProcessLauncher, IActionExecutor> Create, RadialAction Valid, RadialAction Invalid, string Target);

    private Case For(string kind) => kind switch
    {
        "application" => new Case(l => new LaunchApplicationActionExecutor(l), new LaunchApplicationAction(@"C:\Apps\secreto.exe", "--x", @"C:\Apps"), new LaunchApplicationAction(""), @"C:\Apps\secreto.exe"),
        "file" => new Case(l => new OpenFileActionExecutor(l), new OpenFileAction(_fs.ExistingFile), new OpenFileAction("   "), _fs.ExistingFile),
        "folder" => new Case(l => new OpenFolderActionExecutor(l), new OpenFolderAction(_fs.ExistingFolder), new OpenFolderAction(""), _fs.ExistingFolder),
        "url" => new Case(l => new OpenUrlActionExecutor(l), new OpenUrlAction("https://example.com/privado"), new OpenUrlAction("no es una url"), "example.com/privado"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private IEnumerable<RadialAction> EveryKindOfAction() =>
    [
        new LaunchApplicationAction(@"C:\Apps\a.exe"),
        new OpenFileAction(_fs.ExistingFile),
        new OpenFolderAction(_fs.ExistingFolder),
        new OpenUrlAction("https://example.com"),
        new SendKeysAction(new KeyChord(KeyModifiers.Control, "T")),
        new OpenMenuAction(new MenuId("otro")),
    ];

    private IEnumerable<RadialAction> OtherKinds(Case c) => EveryKindOfAction().Where(a => a.GetType() != c.Valid.GetType());

    // CanExecute y tipos no admitidos

    [Theory]
    [MemberData(nameof(Kinds))]
    public void CanExecute_AcceptsOnlyItsOwnActionType(string kind)
    {
        var c = For(kind);
        var executor = c.Create(new FakeProcessLauncher());

        Assert.True(executor.CanExecute(c.Valid));
        Assert.True(executor.CanExecute(c.Invalid)); // el tipo es el correcto aunque los datos no lo sean
        Assert.All(OtherKinds(c), other => Assert.False(executor.CanExecute(other)));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task AnUnsupportedActionType_IsReportedAsUnsupported_AndNothingIsLaunched(string kind)
    {
        var c = For(kind);
        var launcher = new FakeProcessLauncher();
        var executor = c.Create(launcher);

        foreach (var other in OtherKinds(c))
        {
            var result = await executor.ExecuteAsync(other);

            Assert.Equal(ActionExecutionStatus.Failed, result.Status);
            Assert.Equal(ActionErrorCodes.UnsupportedAction, result.Error!.Code);
        }

        Assert.Empty(launcher.Requests);
    }

    // Validación (GetProblems) y lanzamiento correcto

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task AnInvalidAction_IsReportedAsInvalid_AndNothingIsLaunched(string kind)
    {
        var c = For(kind);
        var launcher = new FakeProcessLauncher();

        var result = await c.Create(launcher).ExecuteAsync(c.Invalid);

        Assert.NotEmpty(c.Invalid.GetProblems()); // la acción de la prueba es de verdad inválida según Core
        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task AValidAction_IsLaunchedExactlyOnce_AndSucceeds(string kind)
    {
        var c = For(kind);
        var launcher = new FakeProcessLauncher();

        var result = await c.Create(launcher).ExecuteAsync(c.Valid);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Single(launcher.Requests);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ASuccess_IsNotReported_UntilTheLauncherConfirmsIt(string kind)
    {
        var c = For(kind);
        var confirmation = new TaskCompletionSource<ProcessLaunchResult>();
        var launcher = new FakeProcessLauncher { Pending = confirmation.Task };

        var execution = c.Create(launcher).ExecuteAsync(c.Valid);

        Assert.Single(launcher.Requests); // ya se pidió el lanzamiento...
        Assert.False(execution.IsCompleted); // ...pero no se da por hecho

        confirmation.SetResult(ProcessLaunchResult.Started);

        Assert.True((await execution).IsSuccess);
    }

    // Fallos del lanzador

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ATargetTheSystemCannotFind_BecomesATargetNotFoundFailure(string kind)
    {
        var c = For(kind);
        var launcher = new FakeProcessLauncher { Result = new ProcessLaunchResult(ProcessLaunchOutcome.TargetNotFound, 2) };

        var result = await c.Create(launcher).ExecuteAsync(c.Valid);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.TargetNotFound, result.Error!.Code);
        Assert.Single(launcher.Requests);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ARejectedLaunch_BecomesALaunchFailedFailure_WithTheNativeCode(string kind)
    {
        var c = For(kind);
        var launcher = new FakeProcessLauncher { Result = new ProcessLaunchResult(ProcessLaunchOutcome.Failed, 1155) };

        var result = await c.Create(launcher).ExecuteAsync(c.Valid);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.LaunchFailed, result.Error!.Code);
        Assert.Contains("1155", result.Error.Message);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ARejectedLaunchWithoutNativeCode_StillFails_WithoutAnEmptyCodeInTheMessage(string kind)
    {
        var c = For(kind);
        var launcher = new FakeProcessLauncher { Result = new ProcessLaunchResult(ProcessLaunchOutcome.Failed) };

        var result = await c.Create(launcher).ExecuteAsync(c.Valid);

        Assert.Equal(ActionErrorCodes.LaunchFailed, result.Error!.Code);
        Assert.DoesNotContain("error 0", result.Error.Message);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task FailureMessages_NeverReproduceThePathOrTheAddress(string kind)
    {
        var c = For(kind);
        var failures = new List<ActionExecutionResult>();

        foreach (var outcome in new[] { ProcessLaunchOutcome.TargetNotFound, ProcessLaunchOutcome.Failed })
        {
            var launcher = new FakeProcessLauncher { Result = new ProcessLaunchResult(outcome, 5) };
            failures.Add(await c.Create(launcher).ExecuteAsync(c.Valid));
        }

        failures.Add(await c.Create(new FakeProcessLauncher()).ExecuteAsync(c.Invalid));
        failures.Add(await c.Create(new FakeProcessLauncher()).ExecuteAsync(EveryKindOfAction().First(a => a is OpenMenuAction)));

        Assert.All(failures, failure =>
        {
            Assert.Equal(ActionExecutionStatus.Failed, failure.Status);
            Assert.DoesNotContain(c.Target, failure.Error!.Message);
            Assert.DoesNotContain(_fs.Root, failure.Error.Message);
        });
    }

    // Cancelación

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ACancellationBeforeLaunching_ReturnsCancelled_AndNothingIsLaunched(string kind)
    {
        var c = For(kind);
        var launcher = new FakeProcessLauncher();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await c.Create(launcher).ExecuteAsync(c.Valid, cancellation.Token);

        Assert.Equal(ActionExecutionStatus.Cancelled, result.Status);
        Assert.Null(result.Error);
        Assert.Empty(launcher.Requests);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ACancellationThatArrivesBeforeTheProcessStarts_ReturnsCancelled(string kind)
    {
        var c = For(kind);
        using var cancellation = new CancellationTokenSource();
        var launcher = new FakeProcessLauncher { OnLaunch = cancellation.Cancel, ThrowIfCancelled = true };

        var result = await c.Create(launcher).ExecuteAsync(c.Valid, cancellation.Token);

        Assert.Equal(ActionExecutionStatus.Cancelled, result.Status);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ACancellationAfterTheLaunchStarted_DoesNotTurnTheSuccessIntoACancellation(string kind)
    {
        var c = For(kind);
        using var cancellation = new CancellationTokenSource();
        var launcher = new FakeProcessLauncher { OnLaunch = cancellation.Cancel, ThrowIfCancelled = false };

        var result = await c.Create(launcher).ExecuteAsync(c.Valid, cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Single(launcher.Requests);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task AnOperationCanceledFromTheLauncher_WithoutACancelledToken_IsNotSwallowed(string kind)
    {
        // Solo se interpreta como cancelación si quien llama la pidió; si no, es un error del programa y se propaga.
        var c = For(kind);
        var launcher = new FakeProcessLauncher { ThrowOnLaunch = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() => c.Create(launcher).ExecuteAsync(c.Valid));
    }

    // Argumentos

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ANullAction_IsAProgrammingError(string kind)
    {
        var executor = For(kind).Create(new FakeProcessLauncher());

        await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ExecuteAsync(null!));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ANullLauncher_IsRejectedAtConstruction(string kind)
    {
        Assert.Throws<ArgumentNullException>(() => For(kind).Create(null!));
    }
}
