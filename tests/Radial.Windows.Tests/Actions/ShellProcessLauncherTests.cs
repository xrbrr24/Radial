using System.ComponentModel;
using System.Diagnostics;
using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Windows.Actions;

namespace Radial.Windows.Tests.Actions;

/// <summary>
/// El lanzador real, SIN iniciar ningún proceso: se le inyecta la función de arranque, así que solo se comprueba cómo traduce la
/// solicitud a <see cref="ProcessStartInfo"/> y los errores del sistema a resultados.
/// </summary>
public class ShellProcessLauncherTests
{
    private sealed class RecordingStart
    {
        public List<ProcessStartInfo> Infos { get; } = [];

        public Exception? Throw { get; set; }

        public Process? Start(ProcessStartInfo info)
        {
            Infos.Add(info);
            return Throw is null ? null : throw Throw;
        }
    }

    // ProcessStartInfo

    [Fact]
    public void TheStartInfo_UsesTheShell_AndKeepsTargetArgumentsAndDirectorySeparate()
    {
        var info = ShellProcessLauncher.CreateStartInfo(new ProcessLaunchRequest(@"C:\Apps\a.exe", ["--x", "y z"], @"C:\Trabajo"));

        Assert.True(info.UseShellExecute);
        Assert.Equal(@"C:\Apps\a.exe", info.FileName);
        Assert.Equal<string>(new[] { "--x", "y z" }, info.ArgumentList);
        Assert.Equal(string.Empty, info.Arguments); // nada de componer una línea: cada argumento va en su sitio
        Assert.Equal(@"C:\Trabajo", info.WorkingDirectory);
    }

    [Fact]
    public void WithOnlyATarget_ArgumentsAndDirectoryStayEmpty()
    {
        var info = ShellProcessLauncher.CreateStartInfo(new ProcessLaunchRequest("https://example.com/"));

        Assert.True(info.UseShellExecute);
        Assert.Equal("https://example.com/", info.FileName);
        Assert.Equal(string.Empty, info.Arguments);
        Assert.Equal(string.Empty, info.WorkingDirectory);
    }

    [Theory]
    [InlineData(@"C:\Docs\informe.docx")]
    [InlineData(@"C:\Docs\carpeta")]
    [InlineData("https://example.com/")]
    [InlineData("notepad.exe")]
    public void TheTarget_IsNeverWrappedInAShellOrAnInterpreter(string target)
    {
        var info = ShellProcessLauncher.CreateStartInfo(new ProcessLaunchRequest(target, ["arg & otro | tercero"]));

        Assert.Equal(target, info.FileName);
        Assert.DoesNotContain("cmd", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(target, info.ArgumentList); // el destino no se mezcla con los argumentos
        Assert.Equal<string>(new[] { "arg & otro | tercero" }, info.ArgumentList); // el destino no se mezcla con los argumentos
        Assert.False(info.RedirectStandardOutput);
        Assert.False(info.RedirectStandardInput);
        Assert.False(info.RedirectStandardError);
    }

    // Arranque y resultados

    [Fact]
    public async Task ASuccessfulStart_IsReportedAsStarted_EvenWhenTheSystemReturnsNoProcess()
    {
        var start = new RecordingStart();
        var launcher = new ShellProcessLauncher(start.Start);

        var result = await launcher.LaunchAsync(new ProcessLaunchRequest("https://example.com/"));

        Assert.Equal(ProcessLaunchOutcome.Started, result.Outcome);
        Assert.Equal(0, result.NativeErrorCode);
        Assert.Single(start.Infos);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FileOrPathNotFound_IsReportedAsTargetNotFound(int nativeError)
    {
        var start = new RecordingStart { Throw = new Win32Exception(nativeError) };

        var result = await new ShellProcessLauncher(start.Start).LaunchAsync(new ProcessLaunchRequest(@"C:\no.exe"));

        Assert.Equal(new ProcessLaunchResult(ProcessLaunchOutcome.TargetNotFound, nativeError), result);
    }

    [Theory]
    [InlineData(5)]    // acceso denegado
    [InlineData(193)]  // no es una aplicación válida
    [InlineData(1155)] // sin aplicación asociada
    [InlineData(1223)] // el usuario canceló la elevación
    public async Task OtherSystemErrors_AreReportedAsFailedWithTheirCode(int nativeError)
    {
        var start = new RecordingStart { Throw = new Win32Exception(nativeError) };

        var result = await new ShellProcessLauncher(start.Start).LaunchAsync(new ProcessLaunchRequest(@"C:\x.exe"));

        Assert.Equal(new ProcessLaunchResult(ProcessLaunchOutcome.Failed, nativeError), result);
    }

    [Fact]
    public async Task AFileNotFoundException_IsReportedAsTargetNotFound()
    {
        var start = new RecordingStart { Throw = new FileNotFoundException() };

        var result = await new ShellProcessLauncher(start.Start).LaunchAsync(new ProcessLaunchRequest(@"C:\x.exe"));

        Assert.Equal(ProcessLaunchOutcome.TargetNotFound, result.Outcome);
    }

    [Fact]
    public async Task ARequestProcessCannotHandle_IsReportedAsFailed_NotThrown()
    {
        var start = new RecordingStart { Throw = new InvalidOperationException() };

        var result = await new ShellProcessLauncher(start.Start).LaunchAsync(new ProcessLaunchRequest(" "));

        Assert.Equal(ProcessLaunchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task AnUnexpectedException_IsAProgrammingError_AndIsNotSwallowed()
    {
        var start = new RecordingStart { Throw = new ArgumentException("fallo inesperado") };

        await Assert.ThrowsAsync<ArgumentException>(() => new ShellProcessLauncher(start.Start).LaunchAsync(new ProcessLaunchRequest(@"C:\x.exe")));
    }

    // Cancelación y argumentos

    [Fact]
    public async Task ACancellationBeforeStarting_StartsNothing_AndThrowsOperationCanceled()
    {
        var start = new RecordingStart();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ShellProcessLauncher(start.Start).LaunchAsync(new ProcessLaunchRequest(@"C:\x.exe"), cancellation.Token));

        Assert.Empty(start.Infos);
    }

    [Fact]
    public async Task ANullRequest_IsAProgrammingError()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ShellProcessLauncher(new RecordingStart().Start).LaunchAsync(null!));
    }

    // De extremo a extremo, sin ningún proceso real

    [Fact]
    public async Task AFullChain_ExecutorToStartInfo_UsesTheShellWithTheRightFields()
    {
        var start = new RecordingStart();
        var executor = new LaunchApplicationActionExecutor(new ShellProcessLauncher(start.Start));

        var result = await executor.ExecuteAsync(new LaunchApplicationAction(@"C:\Apps\a.exe", ["--x"], @"C:\Apps"));

        Assert.True(result.IsSuccess);
        var info = Assert.Single(start.Infos);
        Assert.True(info.UseShellExecute);
        Assert.Equal(@"C:\Apps\a.exe", info.FileName);
        Assert.Equal<string>(new[] { "--x" }, info.ArgumentList);
        Assert.Equal(@"C:\Apps", info.WorkingDirectory);
    }

    [Fact]
    public async Task AFullChain_AMissingApplication_BecomesATargetNotFoundFailure()
    {
        var start = new RecordingStart { Throw = new Win32Exception(2) };
        var executor = new LaunchApplicationActionExecutor(new ShellProcessLauncher(start.Start));

        var result = await executor.ExecuteAsync(new LaunchApplicationAction(@"C:\Apps\desaparecida.exe"));

        Assert.Equal(ActionErrorCodes.TargetNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task AFullChain_AUrl_ReachesTheShellAsASingleTarget()
    {
        var start = new RecordingStart();
        var executor = new OpenUrlActionExecutor(new ShellProcessLauncher(start.Start));

        var result = await executor.ExecuteAsync(new OpenUrlAction("https://example.com/a?b=c"));

        Assert.True(result.IsSuccess);
        var info = Assert.Single(start.Infos);
        Assert.Equal("https://example.com/a?b=c", info.FileName);
        Assert.Equal(string.Empty, info.Arguments);
    }
}
