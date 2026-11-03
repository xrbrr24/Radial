using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Windows.Actions;

namespace Radial.Windows.Tests.Actions;

public class LaunchApplicationActionExecutorTests
{
    private static async Task<(ActionExecutionResult Result, FakeProcessLauncher Launcher)> Run(LaunchApplicationAction action, ProcessLaunchResult? launchResult = null)
    {
        var launcher = new FakeProcessLauncher();
        if (launchResult is not null)
        {
            launcher.Result = launchResult;
        }

        var result = await new LaunchApplicationActionExecutor(launcher).ExecuteAsync(action);
        return (result, launcher);
    }

    [Fact]
    public async Task TheRequest_CarriesTargetArgumentsAndWorkingDirectory_Separately()
    {
        var (result, launcher) = await Run(new LaunchApplicationAction(@"C:\Apps\editor.exe", "--abrir \"mi doc.txt\" --modo rapido", @"C:\Trabajo"));

        Assert.True(result.IsSuccess);
        var request = Assert.Single(launcher.Requests);
        Assert.Equal(@"C:\Apps\editor.exe", request.Target);
        Assert.Equal("--abrir \"mi doc.txt\" --modo rapido", request.Arguments);
        Assert.Equal(@"C:\Trabajo", request.WorkingDirectory);
    }

    [Fact]
    public async Task WithoutArgumentsOrWorkingDirectory_TheRequestHasNone()
    {
        var (_, launcher) = await Run(new LaunchApplicationAction(@"C:\Apps\editor.exe"));

        var request = Assert.Single(launcher.Requests);
        Assert.Null(request.Arguments);
        Assert.Null(request.WorkingDirectory);
    }

    [Fact]
    public async Task Arguments_AreNeverInterpretedOrCombinedWithTheTarget()
    {
        // Lo que parece un comando de terminal es solo texto: viaja intacto en su campo y no se añade al destino.
        const string hostile = "--x & del /q * | calc && powershell -c \"evil\"";

        var (_, launcher) = await Run(new LaunchApplicationAction(@"C:\Apps\editor.exe", hostile));

        var request = Assert.Single(launcher.Requests);
        Assert.Equal(@"C:\Apps\editor.exe", request.Target);
        Assert.Equal(hostile, request.Arguments);
    }

    [Fact]
    public async Task TheExecutor_DoesNotRequireTheTargetToExistOnDisk()
    {
        // Un nombre sin ruta ("notepad.exe") lo resuelve el sistema; si no lo encuentra, lo dice el lanzador.
        var (result, launcher) = await Run(new LaunchApplicationAction("programa-que-no-existe-en-ningun-sitio.exe"));

        Assert.True(result.IsSuccess);
        Assert.Single(launcher.Requests);
    }

    [Fact]
    public async Task ABareProgramName_IsPassedAsIs()
    {
        var (_, launcher) = await Run(new LaunchApplicationAction("notepad.exe"));

        Assert.Equal("notepad.exe", Assert.Single(launcher.Requests).Target);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\0b")]
    public async Task AnInvalidTarget_IsReportedAsInvalid_AndNothingIsLaunched(string path)
    {
        var (result, launcher) = await Run(new LaunchApplicationAction(path));

        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task InvalidArgumentsOrWorkingDirectory_AreReportedAsInvalid_AndNothingIsLaunched()
    {
        var (nullInArguments, first) = await Run(new LaunchApplicationAction(@"C:\Apps\a.exe", "a\0b"));
        var (nullInDirectory, second) = await Run(new LaunchApplicationAction(@"C:\Apps\a.exe", WorkingDirectory: "C:\\x\0"));

        Assert.Equal(ActionErrorCodes.InvalidAction, nullInArguments.Error!.Code);
        Assert.Equal(ActionErrorCodes.InvalidAction, nullInDirectory.Error!.Code);
        Assert.Empty(first.Requests);
        Assert.Empty(second.Requests);
    }

    [Fact]
    public async Task ATargetTheSystemCannotFind_IsATargetNotFoundFailure()
    {
        var (result, launcher) = await Run(
            new LaunchApplicationAction(@"C:\Apps\desaparecida.exe"),
            new ProcessLaunchResult(ProcessLaunchOutcome.TargetNotFound, 2));

        Assert.Equal(ActionErrorCodes.TargetNotFound, result.Error!.Code);
        Assert.Single(launcher.Requests);
    }

    [Fact]
    public async Task ARejectedLaunch_IsALaunchFailedFailure()
    {
        var (result, _) = await Run(
            new LaunchApplicationAction(@"C:\Apps\a.exe"),
            new ProcessLaunchResult(ProcessLaunchOutcome.Failed, 5));

        Assert.Equal(ActionErrorCodes.LaunchFailed, result.Error!.Code);
    }
}
