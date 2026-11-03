using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Windows.Actions;

namespace Radial.Windows.Tests.Actions;

public class OpenFolderActionExecutorTests : IClassFixture<TempFileSystem>
{
    private readonly TempFileSystem _fs;

    public OpenFolderActionExecutorTests(TempFileSystem fs)
    {
        _fs = fs;
    }

    private static async Task<(ActionExecutionResult Result, FakeProcessLauncher Launcher)> Run(string path)
    {
        var launcher = new FakeProcessLauncher();
        var result = await new OpenFolderActionExecutor(launcher).ExecuteAsync(new OpenFolderAction(path));
        return (result, launcher);
    }

    [Fact]
    public async Task AnExistingFolder_IsOpenedWithTheSystem()
    {
        var (result, launcher) = await Run(_fs.ExistingFolder);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(launcher.Requests);
        Assert.Equal(_fs.ExistingFolder, request.Target);
        Assert.Null(request.Arguments);
        Assert.Null(request.WorkingDirectory);
    }

    [Fact]
    public async Task AFolderWithATrailingSeparator_IsStillAFolder()
    {
        var (result, launcher) = await Run(_fs.ExistingFolder + Path.DirectorySeparatorChar);

        Assert.True(result.IsSuccess);
        Assert.Single(launcher.Requests);
    }

    [Fact]
    public async Task AMissingFolder_IsReportedAsNotFound_AndNothingIsLaunched()
    {
        var (result, launcher) = await Run(_fs.MissingPath);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.TargetNotFound, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task AFile_IsNotTreatedAsAFolder()
    {
        var (result, launcher) = await Run(_fs.ExistingFile);

        Assert.Equal(ActionErrorCodes.TargetNotFound, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Theory]
    [InlineData("<>|?*")]
    [InlineData("%USERPROFILE%")] // no se expanden variables de entorno
    [InlineData("ruta\\relativa\\que\\no\\existe")]
    public async Task APathThatCannotNameAnExistingFolder_IsReportedAsNotFound_WithoutThrowing(string path)
    {
        var (result, launcher) = await Run(path);

        Assert.Equal(ActionErrorCodes.TargetNotFound, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a\0b")]
    public async Task AnEmptyOrMalformedPath_IsReportedAsInvalid_AndNothingIsLaunched(string path)
    {
        var (result, launcher) = await Run(path);

        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task ASystemRefusal_IsALaunchFailedFailure()
    {
        var launcher = new FakeProcessLauncher { Result = new ProcessLaunchResult(ProcessLaunchOutcome.Failed, 5) };

        var result = await new OpenFolderActionExecutor(launcher).ExecuteAsync(new OpenFolderAction(_fs.ExistingFolder));

        Assert.Equal(ActionErrorCodes.LaunchFailed, result.Error!.Code);
    }
}
