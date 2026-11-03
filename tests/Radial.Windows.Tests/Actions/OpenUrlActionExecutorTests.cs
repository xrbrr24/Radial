using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Windows.Actions;

namespace Radial.Windows.Tests.Actions;

public class OpenUrlActionExecutorTests
{
    private static async Task<(ActionExecutionResult Result, FakeProcessLauncher Launcher)> Run(string url)
    {
        var launcher = new FakeProcessLauncher();
        var result = await new OpenUrlActionExecutor(launcher).ExecuteAsync(new OpenUrlAction(url));
        return (result, launcher);
    }

    // Direcciones permitidas

    [Theory]
    [InlineData("http://example.com/path?q=1", "http://example.com/path?q=1")]
    [InlineData("https://example.com/path?q=1", "https://example.com/path?q=1")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("HTTPS://EXAMPLE.COM/A", "https://example.com/A")]
    [InlineData("http://localhost:8080/x", "http://localhost:8080/x")]
    public async Task HttpAndHttps_AreLaunched_UsingTheParsedForm(string url, string expectedTarget)
    {
        var (result, launcher) = await Run(url);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(launcher.Requests);
        Assert.Equal(expectedTarget, request.Target);
        Assert.Null(request.Arguments);
        Assert.Null(request.WorkingDirectory);
    }

    [Fact]
    public async Task WhatIsLaunched_IsTheValidatedParsedAddress_NotTheOriginalText()
    {
        var (_, launcher) = await Run("https://example.com/con espacios");

        var target = Assert.Single(launcher.Requests).Target;
        Assert.DoesNotContain(' ', target);
        Assert.Equal("https://example.com/con%20espacios", target);
    }

    // Direcciones rechazadas

    [Theory]
    [InlineData("example.com")]
    [InlineData("www.example.com/ruta")]
    [InlineData("/ruta/relativa")]
    [InlineData("ruta/relativa.html")]
    [InlineData("//example.com/protocolo-relativo")]
    public async Task ARelativeAddress_IsRejected_AndNothingIsLaunched(string url)
    {
        var (result, launcher) = await Run(url);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(document.cookie)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("ftp://example.com/archivo")]
    [InlineData("ms-settings:network")]
    [InlineData("steam://run/1")]
    [InlineData("ssh://usuario@example.com")]
    [InlineData("mailto:alguien@example.com")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"\\servidor\recurso\programa.exe")]
    public async Task AnUnauthorizedScheme_IsRejected_AndNothingIsLaunched(string url)
    {
        var (result, launcher) = await Run(url);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code); // ya lo rechaza GetProblems() de Core
        Assert.Empty(launcher.Requests);
    }

    [Theory]
    [InlineData("http://")]
    [InlineData("https://")]
    [InlineData("http://exa mple.com")]
    [InlineData("https://[no-es-ipv6")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\0b")]
    public async Task AMalformedAddress_IsRejected_AndNothingIsLaunched(string url)
    {
        var (result, launcher) = await Run(url);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Empty(launcher.Requests);
    }

    // Revalidación justo antes del lanzamiento

    [Fact]
    public async Task MailTo_IsRejectedByTheModelValidation_BeforeAnythingIsLaunched()
    {
        // Desde la fase 4, paso 3 (ajuste), el modelo solo admite http y https: mailto: ya no valida en Core, así que no hay
        // incoherencia entre validar y ejecutar.
        var action = new OpenUrlAction("mailto:alguien@example.com");
        Assert.NotEmpty(action.GetProblems());

        var launcher = new FakeProcessLauncher();
        var result = await new OpenUrlActionExecutor(launcher).ExecuteAsync(action);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public void TheExecutor_UsesTheSchemesOfCore_NotItsOwnList()
    {
        // Cada esquema permitido por el dominio se lanza; si Core cambiara su lista, el ejecutor la seguiría.
        foreach (var scheme in OpenUrlAction.AllowedSchemes)
        {
            Assert.True(OpenUrlActionExecutor.TryGetLaunchTarget($"{scheme}://example.com/x", out _), scheme);
        }
    }

    [Theory]
    [InlineData("mailto:alguien@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("data:text/html,hola")]
    [InlineData("ftp://example.com/x")]
    [InlineData("example.com")]
    [InlineData("http://")]
    [InlineData("")]
    public void TheDefensiveCheck_RejectsWithUrlNotAllowed_EvenWithoutGoingThroughTheModelValidation(string url)
    {
        // Es la comprobación que se repite justo antes de lanzar, independiente de GetProblems().
        var prepared = OpenUrlActionExecutor.TryCreateRequest(url, out var request, out var failure);

        Assert.False(prepared);
        Assert.Null(request);
        Assert.Equal(ActionExecutionStatus.Failed, failure!.Status);
        Assert.Equal(ActionErrorCodes.UrlNotAllowed, failure.Error!.Code);
        // La dirección rechazada no debe aparecer en el mensaje (comparación ordinal). Con "" no hay nada que filtrar:
        // toda cadena contiene la vacía, así que solo se comprueba cuando hay texto.
        if (url.Length > 0)
        {
            Assert.DoesNotContain(url, failure.Error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("HTTP://EXAMPLE.COM", "http://example.com/")]
    public void TheDefensiveCheck_BuildsTheRequestFromTheParsedAddress(string url, string expectedTarget)
    {
        var prepared = OpenUrlActionExecutor.TryCreateRequest(url, out var request, out var failure);

        Assert.True(prepared);
        Assert.Null(failure);
        Assert.Equal(new ProcessLaunchRequest(expectedTarget), request);
    }

    [Theory]
    [InlineData("http://example.com", true)]
    [InlineData("https://example.com/a?b=c#d", true)]
    [InlineData("HTTP://EXAMPLE.COM", true)]
    [InlineData("mailto:alguien@example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/x", false)]
    [InlineData("data:text/plain,hola", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("example.com", false)]
    [InlineData("/relativa", false)]
    [InlineData("http://", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TheLaunchCheck_IsIndependentOfTheModelValidation(string? url, bool allowed)
    {
        Assert.Equal(allowed, OpenUrlActionExecutor.TryGetLaunchTarget(url, out var target));
        Assert.Equal(allowed, target is not null);
    }

    [Fact]
    public void TheLaunchCheck_AcceptsExactlyTheSchemesHttpAndHttps()
    {
        var accepted = new[] { "http", "https", "ftp", "file", "data", "javascript", "mailto", "ws", "wss", "gopher", "ldap", "news", "telnet", "tel", "sms", "urn", "blob", "about" }
            .Where(scheme => OpenUrlActionExecutor.TryGetLaunchTarget($"{scheme}://example.com/x", out _))
            .ToList();

        Assert.Equal(["http", "https"], accepted);
    }

    // Fallos del sistema

    [Fact]
    public async Task ASystemRefusal_IsALaunchFailedFailure_ForExampleWithNoBrowser()
    {
        var launcher = new FakeProcessLauncher { Result = new ProcessLaunchResult(ProcessLaunchOutcome.Failed, 1155) };

        var result = await new OpenUrlActionExecutor(launcher).ExecuteAsync(new OpenUrlAction("https://example.com"));

        Assert.Equal(ActionErrorCodes.LaunchFailed, result.Error!.Code);
    }
}
