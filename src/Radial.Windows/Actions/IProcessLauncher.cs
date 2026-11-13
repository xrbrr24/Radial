namespace Radial.Windows.Actions;

/// <summary>
/// Lo que hay que abrir. Destino, argumentos y directorio de trabajo van SEPARADOS: nunca se concatenan en una línea de
/// comandos ni se pasan por un intérprete. <see cref="Target"/> es una aplicación, un archivo, una carpeta o una dirección web
/// (lo que corresponda al ejecutor que la creó); <see cref="Arguments"/> es la LISTA de argumentos, uno por elemento, que se pasa a
/// <c>ProcessStartInfo.ArgumentList</c> (el propio .NET aplica el entrecomillado que corresponda; Radial no construye ninguna línea).
/// </summary>
public sealed record ProcessLaunchRequest(string Target, IReadOnlyList<string>? Arguments = null, string? WorkingDirectory = null);

public enum ProcessLaunchOutcome
{
    /// <summary>El sistema aceptó el lanzamiento. No dice nada de lo que ocurra después con el programa.</summary>
    Started,

    /// <summary>El sistema no encontró el destino.</summary>
    TargetNotFound,

    /// <summary>El sistema rechazó el lanzamiento por cualquier otro motivo previsible.</summary>
    Failed,
}

/// <param name="Outcome">Qué ocurrió.</param>
/// <param name="NativeErrorCode">Código de error del sistema cuando lo hay (0 si no); solo para el mensaje y el diagnóstico.</param>
public sealed record ProcessLaunchResult(ProcessLaunchOutcome Outcome, int NativeErrorCode = 0)
{
    public static ProcessLaunchResult Started { get; } = new(ProcessLaunchOutcome.Started);
}

/// <summary>
/// Abre un destino con el sistema operativo. Es lo único que los ejecutores necesitan del sistema, y lo que las pruebas
/// sustituyen para no iniciar ningún proceso real.
///
/// Contrato:
/// - Un fallo previsible (el destino no existe, el sistema lo rechaza) se DEVUELVE como <see cref="ProcessLaunchResult"/>; no lanza.
/// - La cancelación solo se atiende ANTES de iniciar el lanzamiento (lanza <see cref="OperationCanceledException"/>). Una vez
///   iniciado, se completa y se devuelve su resultado: no se informa de una cancelación si el sistema ya aceptó el lanzamiento.
/// - Que devuelva <see cref="ProcessLaunchOutcome.Started"/> significa que el sistema aceptó abrir el destino, no que el
///   programa llegara a mostrarse ni a terminar bien.
/// </summary>
public interface IProcessLauncher
{
    Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default);
}
