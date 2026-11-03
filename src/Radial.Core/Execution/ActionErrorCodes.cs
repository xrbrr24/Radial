namespace Radial.Core.Execution;

/// <summary>
/// Códigos de error estables de <see cref="ActionExecutionError.Code"/>. Aquí solo están los que define el propio contrato;
/// cada ejecutor concreto añadirá los suyos (p. ej. el vencimiento de un atajo) cuando exista.
/// </summary>
public static class ActionErrorCodes
{
    /// <summary>El ejecutor no sabe ejecutar este tipo de acción (<see cref="IActionExecutor.CanExecute"/> es falso).</summary>
    public const string UnsupportedAction = "UnsupportedAction";

    /// <summary>La acción tiene problemas según <see cref="Model.RadialAction.GetProblems"/>: no se ejecuta nada.</summary>
    public const string InvalidAction = "InvalidAction";
}
