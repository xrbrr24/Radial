using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Tests.Support;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Model;

/// <summary>
/// Una dirección web solo es válida con los esquemas de <see cref="OpenUrlAction.AllowedSchemes"/>: http y https. Es la regla de
/// dominio; Radial.Windows la reutiliza (no la copia) en su comprobación defensiva antes de lanzar.
/// </summary>
public class OpenUrlActionValidationTests
{
    public static TheoryData<string> Accepted =>
    [
        "http://example.com",
        "https://example.com",
        "http://example.com/path?q=1#fragment",
        "https://example.com/path?q=1",
        "HTTP://EXAMPLE.COM",
        "HTTPS://EXAMPLE.COM/A",
        "http://localhost:8080/x",
        "https://example.com/con espacios",
        "https://usuario@example.com/",
        "http://192.168.0.1/",
    ];

    public static TheoryData<string> Mailto =>
    [
        "mailto:someone@example.com",
        "MAILTO:someone@example.com",
        "mailto:someone@example.com?subject=hola",
    ];

    public static TheoryData<string> DangerousSchemes =>
    [
        "javascript:alert(1)",
        "JavaScript:alert(document.cookie)",
        "file:///C:/Windows/System32/cmd.exe",
        "FILE:///C:/x",
        "data:text/html,<script>alert(1)</script>",
        "vbscript:msgbox(1)",
    ];

    public static TheoryData<string> UnknownOrOtherSchemes =>
    [
        "ftp://example.com/archivo",
        "ws://example.com/socket",
        "wss://example.com/socket",
        "ssh://usuario@example.com",
        "ms-settings:network",
        "steam://run/1",
        "foo://bar",
        "x-desconocido:cosa",
        "tel:+34600000000",
        "about:blank",
    ];

    public static TheoryData<string> Relative =>
    [
        "example.com",
        "www.example.com/ruta",
        "/ruta/relativa",
        "ruta/relativa.html",
        "//example.com/protocolo-relativo",
        "?q=1",
        "#ancla",
    ];

    public static TheoryData<string> LocalPaths =>
    [
        @"C:\Windows\System32\cmd.exe",
        @"\\servidor\recurso\programa.exe",
    ];

    public static TheoryData<string> Malformed =>
    [
        "http://",
        "https://",
        "http://exa mple.com",
        "https://[no-es-ipv6",
        "http//example.com",
        "://example.com",
        "",
        "   ",
        "a\0b",
        "http://example.com/\0",
    ];

    private static ValidationResult Validate(RadialAction action) =>
        ConfigurationValidator.Validate(TestConfigs.WithMenu(
            TestConfigs.Standard(),
            TestConfigs.Menu(TestConfigs.Work, TestConfigs.Item("x", RadialDirection.North, action))));

    [Fact]
    public void TheAllowedSchemes_AreExactlyHttpAndHttps_AndCannotBeModified()
    {
        Assert.Equal(["http", "https"], OpenUrlAction.AllowedSchemes.Order(StringComparer.Ordinal));

        var list = (IList<string>)OpenUrlAction.AllowedSchemes;
        Assert.Throws<NotSupportedException>(() => list.Add("mailto"));
        Assert.Equal(2, OpenUrlAction.AllowedSchemes.Count);
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void HttpAndHttps_AreValid(string url)
    {
        var action = new OpenUrlAction(url);

        Assert.Empty(action.GetProblems());
        Assert.True(Validate(action).IsValid);
    }

    [Theory]
    [MemberData(nameof(Mailto))]
    public void MailTo_IsRejected(string url)
    {
        AssertRejected(url);
    }

    [Theory]
    [MemberData(nameof(DangerousSchemes))]
    public void JavascriptFileDataAndScriptSchemes_AreRejected(string url)
    {
        AssertRejected(url);
    }

    [Theory]
    [MemberData(nameof(UnknownOrOtherSchemes))]
    public void UnknownAndOtherSchemes_AreRejected(string url)
    {
        AssertRejected(url);
    }

    [Theory]
    [MemberData(nameof(Relative))]
    public void RelativeAddresses_AreRejected(string url)
    {
        AssertRejected(url);
    }

    [Theory]
    [MemberData(nameof(LocalPaths))]
    public void LocalAndNetworkPaths_AreRejected(string url)
    {
        AssertRejected(url);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void MalformedAddresses_AreRejected(string url)
    {
        AssertRejected(url);
    }

    [Fact]
    public void ANullAddress_IsRejectedWithoutThrowing()
    {
        Assert.NotEmpty(new OpenUrlAction(null!).GetProblems());
    }

    [Fact]
    public void TheMessageForAForbiddenScheme_ListsTheAllowedOnes()
    {
        var problem = Assert.Single(new OpenUrlAction("mailto:someone@example.com").GetProblems());

        Assert.Contains("http, https", problem);
        Assert.Contains("mailto", problem);
    }

    private static void AssertRejected(string url)
    {
        var action = new OpenUrlAction(url);

        Assert.NotEmpty(action.GetProblems());
        Assert.Contains(ValidationCodes.ActionInvalid, Validate(action).Errors.Select(e => e.Code));
    }
}
