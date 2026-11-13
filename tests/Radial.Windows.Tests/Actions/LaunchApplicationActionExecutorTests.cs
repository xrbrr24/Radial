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
        var (result, launcher) = await Run(new LaunchApplicationAction(@"C:\Apps\editor.exe", ["--abrir", "mi doc.txt", "--modo", "rapido"], @"C:\Trabajo"));

        Assert.True(result.IsSuccess);
        var request = Assert.Single(launcher.Requests);
        Assert.Equal(@"C:\Apps\editor.exe", request.Target);
        Assert.Equal<string>(new[] { "--abrir", "mi doc.txt", "--modo", "rapido" }, request.Arguments);
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

        var (_, launcher) = await Run(new LaunchApplicationAction(@"C:\Apps\editor.exe", [hostile]));

        var request = Assert.Single(launcher.Requests);
        Assert.Equal(@"C:\Apps\editor.exe", request.Target);
        Assert.Equal<string>(new[] { hostile }, request.Arguments);
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
    [InlineData("ms-settings:")] // protocolo del sistema
    [InlineData("ms-settings:network")]
    [InlineData("shell:Startup")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("http://example.com/a.exe")]
    [InlineData(@"\\?\C:\Windows\notepad.exe")] // ruta de dispositivo
    [InlineData(@"\\.\pipe\algo")]
    [InlineData("//?/C:/Windows/notepad.exe")]
    [InlineData(@"\??\C:\Windows\notepad.exe")] // espacio de nombres del sistema
    [InlineData(@"\/?\C:\Windows\notepad.exe")] // mezcla de barras
    [InlineData(@"/\.\pipe\algo")]
    [InlineData(@"C:\Apps\a.exe:flujo")] // flujo de datos alternativo
    [InlineData(@"C:\Apps\a.exe::$DATA")]
    [InlineData(@"\\servidor\recurso\a.exe:flujo")]
    [InlineData(@".\programa.exe")] // dependerían del directorio de trabajo del proceso
    [InlineData(@"..\programa.exe")]
    [InlineData(@"subcarpeta\programa.exe")]
    [InlineData("subcarpeta/programa.exe")]
    [InlineData(@"\programa.exe")]
    [InlineData("C:programa.exe")] // unidad relativa
    [InlineData(".")]
    [InlineData("..")]
    public async Task ATargetThatIsNeitherAnAbsolutePathNorABareProgramName_IsNotAllowed_AndNothingIsLaunched(string target)
    {
        var (result, launcher) = await Run(new LaunchApplicationAction(target));

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.TargetNotAllowed, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Theory]
    [InlineData(@"C:\Apps\editor.exe")]
    [InlineData(@"\\servidor\recurso\editor.exe")] // ruta de red: absoluta; qué hacer con ellas es decisión de la configuración (fase 5)
    [InlineData("notepad.exe")]
    [InlineData("calc")]
    public async Task AnAbsolutePathOrABareProgramName_IsAllowed(string target)
    {
        var (result, launcher) = await Run(new LaunchApplicationAction(target));

        Assert.True(result.IsSuccess);
        Assert.Equal(target, Assert.Single(launcher.Requests).Target);
    }

    [Theory]
    [InlineData(@"relativo\carpeta")]
    [InlineData(".")]
    [InlineData(@"\\?\C:\Trabajo")]
    [InlineData("ms-settings:")]
    public async Task ARelativeOrDeviceWorkingDirectory_IsNotAllowed_AndNothingIsLaunched(string workingDirectory)
    {
        var (result, launcher) = await Run(new LaunchApplicationAction(@"C:\Apps\a.exe", WorkingDirectory: workingDirectory));

        Assert.Equal(ActionErrorCodes.TargetNotAllowed, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task ThePolicyMessage_DoesNotRepeatTheTarget()
    {
        const string secret = @"subcarpeta-secreta\programa.exe";

        var (result, _) = await Run(new LaunchApplicationAction(secret));

        Assert.DoesNotContain("secreta", result.Error!.Message);
    }

    [Fact]
    public async Task ThePolicy_IsCheckedAfterTheCoreValidation_SoAnInvalidActionStaysInvalid()
    {
        var (result, launcher) = await Run(new LaunchApplicationAction("ms-settings:\0"));

        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Empty(launcher.Requests);
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
        var (nullInArguments, first) = await Run(new LaunchApplicationAction(@"C:\Apps\a.exe", ["a\0b"]));
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
