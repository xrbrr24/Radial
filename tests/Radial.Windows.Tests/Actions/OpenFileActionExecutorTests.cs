using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Windows.Actions;

namespace Radial.Windows.Tests.Actions;

public class OpenFileActionExecutorTests : IClassFixture<TempFileSystem>
{
    private readonly TempFileSystem _fs;

    public OpenFileActionExecutorTests(TempFileSystem fs)
    {
        _fs = fs;
    }

    private static async Task<(ActionExecutionResult Result, FakeProcessLauncher Launcher)> Run(string path)
    {
        var launcher = new FakeProcessLauncher();
        var result = await new OpenFileActionExecutor(launcher).ExecuteAsync(new OpenFileAction(path));
        return (result, launcher);
    }

    [Fact]
    public async Task AnExistingFile_IsOpenedThroughTheSystemAssociation()
    {
        var (result, launcher) = await Run(_fs.ExistingFile);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(launcher.Requests);
        Assert.Equal(_fs.ExistingFile, request.Target);
        Assert.Null(request.Arguments);
        Assert.Null(request.WorkingDirectory);
    }

    [Fact]
    public async Task AMissingFile_IsReportedAsNotFound_AndNothingIsLaunched()
    {
        var (result, launcher) = await Run(Path.Combine(_fs.MissingPath, "no.txt"));

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.TargetNotFound, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task ADevicePath_IsNotAllowed_EvenIfItNamesAnExistingFile()
    {
        var (result, launcher) = await Run(@"\\?\" + _fs.ExistingFile);

        Assert.Equal(ActionErrorCodes.TargetNotAllowed, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Theory]
    [InlineData(@"\??\")]
    [InlineData(@"\/?\")]
    [InlineData("//./")]
    public async Task EveryFormOfDevicePrefix_IsNotAllowed(string prefix)
    {
        var (result, launcher) = await Run(prefix + _fs.ExistingFile);

        Assert.Equal(ActionErrorCodes.TargetNotAllowed, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task AnAlternateDataStream_IsNotAllowed_EvenOnAnExistingFile()
    {
        var (result, launcher) = await Run(_fs.ExistingFile + ":flujo");

        Assert.Equal(ActionErrorCodes.TargetNotAllowed, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Theory]
    [InlineData("<>|?*.txt")]
    [InlineData("::")]
    [InlineData("CON:\\\\:")]
    [InlineData("%USERPROFILE%\\documento.txt")] // no se expanden variables de entorno
    [InlineData("ruta\\relativa\\que\\no\\existe\\a.txt")]
    public async Task APathThatCannotNameAnExistingFile_IsReportedAsNotFound_WithoutThrowing(string path)
    {
        var (result, launcher) = await Run(path);

        Assert.Equal(ActionErrorCodes.TargetNotFound, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a\0b")]
    public async Task AnEmptyOrMalformedPath_IsReportedAsInvalid_AndNothingIsLaunched(string path)
    {
        var (result, launcher) = await Run(path);

        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task AFolder_IsNotAFile()
    {
        var (result, launcher) = await Run(_fs.ExistingFolder);

        Assert.Equal(ActionErrorCodes.TargetNotFound, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task AScriptFile_IsStillJustAFilePath_TheExtensionIsNotInterpretedAsACommand()
    {
        var (result, launcher) = await Run(_fs.ExistingScript);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(launcher.Requests);
        Assert.Equal(_fs.ExistingScript, request.Target);
        Assert.Null(request.Arguments); // nada de "cmd /c guion.bat"
        Assert.Null(request.WorkingDirectory);
    }

    [Fact]
    public async Task ASystemRefusal_IsALaunchFailedFailure_ForExampleWhenNoApplicationIsAssociated()
    {
        var launcher = new FakeProcessLauncher { Result = new ProcessLaunchResult(ProcessLaunchOutcome.Failed, 1155) };

        var result = await new OpenFileActionExecutor(launcher).ExecuteAsync(new OpenFileAction(_fs.ExistingFile));

        Assert.Equal(ActionErrorCodes.LaunchFailed, result.Error!.Code);
        Assert.Contains("1155", result.Error.Message);
    }

    [Fact]
    public async Task TheFileIsCheckedAtExecutionTime_NotWhenTheExecutorIsCreated()
    {
        var path = Path.Combine(_fs.Root, "aparece-despues.txt");
        var launcher = new FakeProcessLauncher();
        var executor = new OpenFileActionExecutor(launcher);

        var before = await executor.ExecuteAsync(new OpenFileAction(path));
        await File.WriteAllTextAsync(path, "x");
        var after = await executor.ExecuteAsync(new OpenFileAction(path));

        Assert.Equal(ActionErrorCodes.TargetNotFound, before.Error!.Code);
        Assert.True(after.IsSuccess);
        Assert.Single(launcher.Requests);
    }
}
