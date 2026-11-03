using Radial.Core.Model;

namespace Radial.Core.Execution;

/// <summary>
/// Ejecuta acciones del modelo. El motor solo <em>indica</em> qué acción corresponde (<c>RadialResult</c>); quien la lleva a cabo
/// implementa esta interfaz. Radial.Core define el contrato y no contiene ningún ejecutor: lanzar procesos, abrir URLs o enviar
/// teclas depende del sistema y vive en Radial.Windows.
///
/// Extensión: un ejecutor es una clase que implementa esta interfaz para uno o varios tipos de <see cref="RadialAction"/>; el
/// modelo, el motor y el validador no cambian. Elegir el ejecutor adecuado para cada acción es cosa de quien los compone.
///
/// Contrato de <see cref="ExecuteAsync"/>:
/// - Un fallo previsible (el programa no existe, la URL no es válida, un atajo vence...) NO lanza: devuelve
///   <see cref="ActionExecutionResult.Failure"/>. Las excepciones se reservan para errores de programación (argumento nulo).
/// - Si <c>cancellationToken</c> se cancela, devuelve <see cref="ActionExecutionResult.Cancelled"/> en lugar de lanzar
///   <see cref="OperationCanceledException"/>. Con el token ya cancelado al empezar, no hace nada.
/// - Una acción que <see cref="CanExecute"/> rechaza produce <see cref="ActionErrorCodes.UnsupportedAction"/>.
/// - Una acción con problemas (<see cref="RadialAction.GetProblems"/>) produce <see cref="ActionErrorCodes.InvalidAction"/> y no
///   se ejecuta nada: la configuración puede no ser de confianza.
/// - No requiere el hilo de interfaz ni conoce ninguna interfaz gráfica.
/// </summary>
public interface IActionExecutor
{
    /// <summary>¿Sabe este ejecutor ejecutar este tipo de acción? No ejecuta nada ni tiene efectos secundarios.</summary>
    bool CanExecute(RadialAction action);

    Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default);
}
