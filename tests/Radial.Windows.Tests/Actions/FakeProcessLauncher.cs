using Radial.Windows.Actions;

namespace Radial.Windows.Tests.Actions;

/// <summary>
/// Lanzador falso: registra las solicitudes y devuelve lo que la prueba le diga. Nunca inicia un proceso, así que estas pruebas
/// no dependen de qué programas o asociaciones tenga el equipo.
/// </summary>
internal sealed class FakeProcessLauncher : IProcessLauncher
{
    public List<ProcessLaunchRequest> Requests { get; } = [];

    public ProcessLaunchResult Result { get; set; } = ProcessLaunchResult.Started;

    /// <summary>Si se indica, el lanzamiento no termina hasta que se complete esta tarea.</summary>
    public Task<ProcessLaunchResult>? Pending { get; set; }

    public Exception? ThrowOnLaunch { get; set; }

    /// <summary>Se ejecuta al recibir la solicitud, antes de decidir el resultado (p. ej. para cancelar a mitad).</summary>
    public Action? OnLaunch { get; set; }

    /// <summary>Imita al lanzador real: si el token se canceló antes de iniciar, lanza <see cref="OperationCanceledException"/>.</summary>
    public bool ThrowIfCancelled { get; set; }

    public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        OnLaunch?.Invoke();

        if (ThrowOnLaunch is { } exception)
        {
            throw exception;
        }

        if (ThrowIfCancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Pending ?? Task.FromResult(Result);
    }
}
