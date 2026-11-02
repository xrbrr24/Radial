using System.Reflection;

namespace Radial.Windows.Tests;

/// <summary>
/// Protege la regla de arquitectura: Radial.Windows depende de Radial.Core, pero no de WPF ni de Radial.App.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Windows = Assembly.Load("Radial.Windows");

    [Fact]
    public void Windows_DoesNotReferenceWpfOrApp()
    {
        var forbidden = new[] { "Radial.App", "PresentationFramework", "PresentationCore", "WindowsBase" };

        var referenced = Windows.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        Assert.DoesNotContain(referenced, name => forbidden.Any(f => name.StartsWith(f, StringComparison.Ordinal)));
    }
}
