using Radial.Windows.Actions;

namespace Radial.Windows.Tests.Actions;

/// <summary>
/// Lanzador falso: registra las solicitudes y devuelve lo que la prueba le diga. Nunca inicia un proceso, así que estas pruebas
/// no dependen de qué programas o asociaciones tenga el equipo.
/// </summary>
internal sealed class FakeProcessLauncher : IProcessLauncher
{
    private readonly TaskCompletionSource _launched = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<ProcessLaunchRequest> Requests { get; } = [];

    /// <summary>
    /// Se completa al recibir la primera solicitud. Los ejecutores comprueban el destino en el pool antes de pedir el lanzamiento, así que
    /// una prueba que mira <see cref="Requests"/> justo después de llamar a <c>ExecuteAsync</c> debe esperar antes a esta tarea.
    /// </summary>
    public Task Launched => _launched.Task;

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
        _launched.TrySetResult();
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
