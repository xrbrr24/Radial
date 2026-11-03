namespace Radial.Core.Execution;

public enum ActionExecutionStatus
{
    /// <summary>La acción se llevó a cabo.</summary>
    Succeeded,

    /// <summary>La acción no se pudo llevar a cabo. <see cref="ActionExecutionResult.Error"/> dice por qué.</summary>
    Failed,

    /// <summary>Quien pidió la ejecución la canceló antes de que terminara. No es un error y no hay <see cref="ActionExecutionResult.Error"/>.</summary>
    Cancelled,
}

/// <summary>
/// Por qué falló una ejecución. <see cref="Code"/> es estable (ver <see cref="ActionErrorCodes"/>), está en inglés y es el que
/// usan las pruebas; <see cref="Message"/> está en español y es apto para mostrarlo al usuario. Ninguno depende de la interfaz.
/// </summary>
public sealed record ActionExecutionError(string Code, string Message);

/// <summary>
/// Resultado de <see cref="IActionExecutor.ExecuteAsync"/>. Solo se crea con <see cref="Success"/>, <see cref="Failure"/> o
/// <see cref="Cancelled"/>, así que <see cref="Error"/> existe si y solo si el estado es <see cref="ActionExecutionStatus.Failed"/>.
/// </summary>
public sealed record ActionExecutionResult
{
    private ActionExecutionResult(ActionExecutionStatus status, ActionExecutionError? error)
    {
        Status = status;
        Error = error;
    }

    public ActionExecutionStatus Status { get; }

    /// <summary>Solo tiene valor cuando <see cref="Status"/> es <see cref="ActionExecutionStatus.Failed"/>.</summary>
    public ActionExecutionError? Error { get; }

    public bool IsSuccess => Status == ActionExecutionStatus.Succeeded;

    public static ActionExecutionResult Success { get; } = new(ActionExecutionStatus.Succeeded, null);

    public static ActionExecutionResult Cancelled { get; } = new(ActionExecutionStatus.Cancelled, null);

    /// <exception cref="ArgumentException"><paramref name="code"/> o <paramref name="message"/> están vacíos.</exception>
    public static ActionExecutionResult Failure(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new ActionExecutionResult(ActionExecutionStatus.Failed, new ActionExecutionError(code, message));
    }
}
