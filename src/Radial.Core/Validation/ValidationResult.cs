namespace Radial.Core.Validation;

public enum ValidationSeverity
{
    /// <summary>Hace la configuración inutilizable.</summary>
    Error,

    /// <summary>La configuración funciona, pero probablemente no es lo que se pretendía.</summary>
    Warning,
}

/// <summary>
/// Un problema encontrado. <see cref="Code"/> es estable y apto para pruebas y para localizar mensajes;
/// <see cref="Path"/> localiza el problema, p. ej. <c>menus[main].items[browser]</c>.
/// </summary>
public sealed record ValidationIssue(ValidationSeverity Severity, string Code, string Message, string Path);

public sealed class ValidationResult
{
    public ValidationResult(IEnumerable<ValidationIssue> issues)
    {
        Issues = issues.ToList().AsReadOnly();
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public IEnumerable<ValidationIssue> Errors => Issues.Where(i => i.Severity == ValidationSeverity.Error);

    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == ValidationSeverity.Warning);

    /// <summary>Verdadero si no hay errores; las advertencias no invalidan.</summary>
    public bool IsValid => !Errors.Any();
}

public sealed class InvalidConfigurationException : Exception
{
    public InvalidConfigurationException(ValidationResult result)
        : base(BuildMessage(result))
    {
        Result = result;
    }

    public ValidationResult Result { get; }

    private static string BuildMessage(ValidationResult result) =>
        "La configuración no es válida:" + string.Concat(result.Errors.Select(e => $"{Environment.NewLine}- {e.Path}: {e.Message}"));
}
