using System.Text.RegularExpressions;

namespace Radial.Core.Diagnostics;

/// <summary>
/// Limpia el texto antes de escribirlo en el registro, para no guardar más de lo necesario del usuario: sustituye su carpeta de perfil
/// (que lleva su nombre de usuario) por <c>%USERPROFILE%</c>, recorta de las direcciones web todo lo que sigue a «?» o «#» (donde van
/// los tokens y los datos personales) y limita la longitud. Es una defensa adicional: lo primero es no pasarle datos del usuario a
/// <see cref="IRadialLog"/>. Es una clase pura, sin acceso al entorno: la carpeta de perfil la da quien la construye.
/// </summary>
public sealed partial class LogSanitizer
{
    public const int DefaultMaxLength = 2000;

    private readonly string? _profilePath;
    private readonly int _maxLength;

    /// <param name="userProfilePath">Carpeta de perfil del usuario (<c>C:\Users\nombre</c>); <c>null</c> o vacía para no sustituir nada.</param>
    /// <param name="maxLength">Longitud máxima del texto devuelto (antes de la nota de recorte).</param>
    public LogSanitizer(string? userProfilePath = null, int maxLength = DefaultMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 16);
        _profilePath = string.IsNullOrWhiteSpace(userProfilePath) ? null : userProfilePath.TrimEnd('\\', '/');
        _maxLength = maxLength;
    }

    /// <summary>Un limpiador sin carpeta de perfil conocida: solo recorta direcciones y longitud.</summary>
    public static LogSanitizer Default { get; } = new();

    /// <summary>Quita lo sensible y recorta; conserva los saltos de línea (para trazas de pila).</summary>
    public string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = text;
        if (_profilePath is { Length: > 0 })
        {
            result = result.Replace(_profilePath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            result = result.Replace(_profilePath.Replace('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        result = QueryOrFragment().Replace(result, "$1?…");
        if (result.Length > _maxLength)
        {
            result = result.Substring(0, _maxLength) + $"… [recortado: {result.Length - _maxLength} caracteres más]";
        }

        return result;
    }

    /// <summary>Como <see cref="Sanitize"/>, pero en una sola línea (los saltos de línea pasan a espacios).</summary>
    public string SanitizeOneLine(string? text) =>
        Sanitize(text).Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');

    // «https://sitio/ruta?token=abc#x» → «https://sitio/ruta?…»
    [GeneratedRegex("(https?://[^\\s?#\"'<>]+)[?#][^\\s\"'<>]*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex QueryOrFragment();
}
