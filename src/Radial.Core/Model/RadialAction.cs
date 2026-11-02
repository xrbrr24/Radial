namespace Radial.Core.Model;

/// <summary>
/// Datos de lo que hace un elemento. Punto de extensión: un tipo de acción nuevo es un record más que hereda de esta
/// clase y describe sus propios problemas; ni los modelos existentes ni el validador necesitan cambiar.
/// La ejecución real nunca ocurre en Radial.Core.
/// </summary>
public abstract record RadialAction
{
    public const int MaxTextLength = 32_767;

    /// <summary>Describe los datos obligatorios ausentes o inválidos. Vacío si la acción es correcta.</summary>
    public abstract IEnumerable<string> GetProblems();

    protected static IEnumerable<string> RequiredText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [$"{field} es obligatorio."];
        }

        return TextLimits(value, field);
    }

    protected static IEnumerable<string> OptionalText(string? value, string field) =>
        value is null ? [] : TextLimits(value, field);

    private static IEnumerable<string> TextLimits(string value, string field)
    {
        if (value.Length > MaxTextLength)
        {
            yield return $"{field} supera los {MaxTextLength} caracteres.";
        }

        if (value.Contains('\0'))
        {
            yield return $"{field} contiene caracteres nulos.";
        }
    }
}
