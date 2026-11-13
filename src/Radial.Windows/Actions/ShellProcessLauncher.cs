using System.ComponentModel;
using System.Diagnostics;

namespace Radial.Windows.Actions;

/// <summary>
/// <see cref="IProcessLauncher"/> real: <see cref="Process.Start(ProcessStartInfo)"/> con <c>UseShellExecute = true</c>, que es
/// el mecanismo normal de Windows para abrir un destino con su aplicación asociada. Nunca pasa por <c>cmd.exe</c>,
/// PowerShell ni scripts: el destino, los argumentos y el directorio de trabajo van en campos distintos de
/// <see cref="ProcessStartInfo"/>. No expande variables de entorno ni resuelve rutas relativas por su cuenta.
///
/// El lanzamiento se hace en un hilo del pool: <c>ShellExecute</c> puede bloquear (rutas de red, asociaciones lentas) y quien
/// llama puede ser el hilo de interfaz.
/// </summary>
public sealed class ShellProcessLauncher : IProcessLauncher
{
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    private readonly Func<ProcessStartInfo, Process?> _start;

    public ShellProcessLauncher()
        : this(Process.Start)
    {
    }

    /// <summary>Permite probar la traducción de solicitudes y errores sin iniciar ningún proceso.</summary>
    internal ShellProcessLauncher(Func<ProcessStartInfo, Process?> start)
    {
        _start = start;
    }

    public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Con el token cancelado antes de que el delegado empiece, Task.Run no lo ejecuta y lanza OperationCanceledException.
        // Una vez dentro, el lanzamiento se completa y se informa de su resultado.
        return Task.Run(() => Launch(request), cancellationToken);
    }

    internal static ProcessStartInfo CreateStartInfo(ProcessLaunchRequest request)
    {
        var info = new ProcessStartInfo
        {
            FileName = request.Target,
            UseShellExecute = true,
        };

        // La API de argumentos del proceso: una entrada por argumento, sin componer ni interpretar ninguna cadena.
        if (request.Arguments is { Count: > 0 } arguments)
        {
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }
        }

        if (request.WorkingDirectory is not null)
        {
            info.WorkingDirectory = request.WorkingDirectory;
        }

        return info;
    }

    internal ProcessLaunchResult Launch(ProcessLaunchRequest request)
    {
        try
        {
            // ShellExecute puede devolver null (p. ej. si la acción se resolvió en un proceso que ya estaba abierto): es éxito.
            using var process = _start(CreateStartInfo(request));
            return ProcessLaunchResult.Started;
        }
        catch (Win32Exception exception)
        {
            return exception.NativeErrorCode is ErrorFileNotFound or ErrorPathNotFound
                ? new ProcessLaunchResult(ProcessLaunchOutcome.TargetNotFound, exception.NativeErrorCode)
                : new ProcessLaunchResult(ProcessLaunchOutcome.Failed, exception.NativeErrorCode);
        }
        catch (FileNotFoundException)
        {
            return new ProcessLaunchResult(ProcessLaunchOutcome.TargetNotFound, ErrorFileNotFound);
        }
        catch (InvalidOperationException)
        {
            // Solicitud que Process no puede procesar (p. ej. destino vacío): un rechazo, no un error del programa.
            return new ProcessLaunchResult(ProcessLaunchOutcome.Failed);
        }
    }
}
