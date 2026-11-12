using System.Diagnostics.CodeAnalysis;
using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Windows.Actions;

namespace Radial.Windows.Tests.Actions;

/// <summary>
/// Las comprobaciones de disco de un ejecutor (existencia de un archivo o carpeta, que en una ruta de red caída tardan segundos) no
/// pueden bloquear a quien llama: en la aplicación es el hilo de interfaz, que lee el gesto y dibuja el radial.
/// </summary>
public class ProcessExecutorThreadingTests
{
    private sealed class BlockingFolderExecutor(IProcessLauncher launcher, ManualResetEventSlim gate) : ProcessActionExecutor<OpenFolderAction>(launcher)
    {
        protected override string Subject => "la carpeta";

        protected override bool TryPrepare(
            OpenFolderAction action,
            [NotNullWhen(true)] out ProcessLaunchRequest? request,
            [NotNullWhen(false)] out ActionExecutionResult? failure)
        {
            gate.Wait(TimeSpan.FromSeconds(30));
            request = new ProcessLaunchRequest(action.Path);
            failure = null;
            return true;
        }
    }

    [Fact]
    public async Task ASlowDiskCheck_DoesNotBlockTheCaller_AndTheActionStillCompletes()
    {
        using var gate = new ManualResetEventSlim();
        var launcher = new FakeProcessLauncher();
        var executor = new BlockingFolderExecutor(launcher, gate);

        // Si la comprobación se hiciera en el hilo de quien llama, ExecuteAsync no volvería hasta abrir la puerta.
        // Se envuelve en object para que Task.Run no desenvuelva la tarea: interesa recibir la tarea pendiente, no su resultado.
        var call = Task.Run(() => (object)executor.ExecuteAsync(new OpenFolderAction(@"C:\Datos")));
        var first = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(ReferenceEquals(call, first), "ExecuteAsync bloqueó a quien llama mientras comprobaba el disco.");
        var pending = (Task<ActionExecutionResult>)await call;
        Assert.False(pending.IsCompleted);

        gate.Set();
        var result = await pending;

        Assert.True(result.IsSuccess);
        Assert.Single(launcher.Requests);
    }

    [Fact]
    public async Task CancellingDuringASlowDiskCheck_LaunchesNothing()
    {
        using var gate = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var launcher = new FakeProcessLauncher();
        var executor = new BlockingFolderExecutor(launcher, gate);

        var pending = executor.ExecuteAsync(new OpenFolderAction(@"C:\Datos"), cancellation.Token);
        await cancellation.CancelAsync();
        gate.Set();
        var result = await pending;

        Assert.Equal(ActionExecutionStatus.Cancelled, result.Status);
        Assert.Empty(launcher.Requests);
    }
}
