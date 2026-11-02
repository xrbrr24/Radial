using System.Reflection;
using System.Runtime.Versioning;

namespace Radial.Core.Tests;

/// <summary>
/// Protege la regla de arquitectura: Radial.Core no depende de Windows, WPF ni de otros proyectos de Radial.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Core = Assembly.Load("Radial.Core");

    [Fact]
    public void Core_TargetsPlatformNeutralFramework()
    {
        var framework = Core.GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.NotNull(framework);
        Assert.Equal(".NETCoreApp,Version=v10.0", framework.FrameworkName);
    }

    [Fact]
    public void Core_DoesNotReferenceWindowsOrWpfAssemblies()
    {
        var forbidden = new[] { "Radial.Windows", "Radial.App", "PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows" };

        var referenced = Core.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        Assert.DoesNotContain(referenced, name => forbidden.Any(f => name.StartsWith(f, StringComparison.Ordinal)));
    }
}
