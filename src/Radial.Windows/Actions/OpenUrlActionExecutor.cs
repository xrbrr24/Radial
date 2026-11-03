using System.Diagnostics.CodeAnalysis;
using Radial.Core.Execution;
using Radial.Core.Model;

namespace Radial.Windows.Actions;

/// <summary>
/// Abre una dirección web (<see cref="OpenUrlAction"/>) con el navegador predeterminado. Solo se lanzan los esquemas de
/// <see cref="OpenUrlAction.AllowedSchemes"/> (<c>http</c> y <c>https</c>; una única definición, en Core). Aunque
/// <c>GetProblems()</c> ya rechaza todo lo demás, la dirección se vuelve a analizar y comprobar aquí, de forma independiente,
/// justo antes del lanzamiento (defensa en profundidad: la configuración puede no ser de confianza). Lo que se lanza es la forma ya
/// analizada (<see cref="Uri.AbsoluteUri"/>), no el texto original.
/// </summary>
public sealed class OpenUrlActionExecutor : ProcessActionExecutor<OpenUrlAction>
{
    public OpenUrlActionExecutor(IProcessLauncher launcher)
        : base(launcher)
    {
    }

    protected override string Subject => "la dirección web";

    /// <summary>
    /// La dirección que se lanzaría, o falso si no es absoluta, no tiene host o su esquema no está en
    /// <see cref="OpenUrlAction.AllowedSchemes"/> (<c>javascript:</c>, <c>file:</c>, <c>data:</c>, <c>mailto:</c>, <c>ftp:</c>...).
    /// No depende de <c>GetProblems()</c>.
    /// </summary>
    internal static bool TryGetLaunchTarget(string? url, [NotNullWhen(true)] out string? target)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && OpenUrlAction.AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(uri.Host))
        {
            target = uri.AbsoluteUri;
            return true;
        }

        target = null;
        return false;
    }

    /// <summary>La comprobación defensiva y la construcción de la solicitud, sin pasar por <c>GetProblems()</c>.</summary>
    internal static bool TryCreateRequest(
        string? url,
        [NotNullWhen(true)] out ProcessLaunchRequest? request,
        [NotNullWhen(false)] out ActionExecutionResult? failure)
    {
        if (!TryGetLaunchTarget(url, out var target))
        {
            request = null;
            failure = ActionExecutionResult.Failure(
                ActionErrorCodes.UrlNotAllowed,
                $"Solo se pueden abrir direcciones {string.Join(" y ", OpenUrlAction.AllowedSchemes)}.");
            return false;
        }

        request = new ProcessLaunchRequest(target);
        failure = null;
        return true;
    }

    protected override bool TryPrepare(
        OpenUrlAction action,
        [NotNullWhen(true)] out ProcessLaunchRequest? request,
        [NotNullWhen(false)] out ActionExecutionResult? failure) =>
        TryCreateRequest(action.Url, out request, out failure);
}
