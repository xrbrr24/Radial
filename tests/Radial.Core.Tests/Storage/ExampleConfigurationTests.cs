using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Storage;

namespace Radial.Core.Tests.Storage;

/// <summary>
/// El ejemplo de configuración de la documentación (<c>docs/examples/config-acciones-nuevas.json</c>) es una configuración real: se
/// puede copiar a <c>%LOCALAPPDATA%\Radial\config.json</c>. Si deja de ser válida, estas pruebas lo dicen.
/// </summary>
public class ExampleConfigurationTests
{
    private static string ExamplePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Radial.sln")))
            {
                return Path.Combine(directory.FullName, "docs", "examples", "config-acciones-nuevas.json");
            }
        }

        throw new FileNotFoundException("No se encontró Radial.sln subiendo desde " + AppContext.BaseDirectory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheExample_IsAValidConfiguration_WithoutErrorsOrWarnings_InStrictAndTolerantLoads(bool tolerant)
    {
        var result = ConfigurationFile.Parse(File.ReadAllText(ExamplePath()), salvageInvalidActions: tolerant);

        Assert.True(result.IsOk, string.Join(" | ", result.Issues.Select(i => $"{i.Code} {i.Path} {i.Message}")));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void TheExample_ContainsEveryNewKindOfAction_AndFillsTheEightSectors()
    {
        var configuration = ConfigurationFile.Parse(File.ReadAllText(ExamplePath())).Configuration!;
        var actions = configuration.Actions.Select(a => a.Action).ToList();

        Assert.Single(actions.OfType<TypeTextAction>());
        Assert.Equal<VolumeOperation>(
            new[] { VolumeOperation.Increase, VolumeOperation.Decrease, VolumeOperation.ToggleMute, VolumeOperation.SetLevel },
            actions.OfType<VolumeAction>().Select(v => v.Operation));
        Assert.Equal<WindowOperation>(
            new[] { WindowOperation.Minimize, WindowOperation.Maximize, WindowOperation.Close },
            actions.OfType<WindowControlAction>().Select(w => w.Operation));
        var launch = Assert.Single(actions.OfType<LaunchApplicationAction>());
        Assert.Equal<string>(new[] { @"C:\Windows\win.ini" }, launch.Arguments);

        var items = configuration.Menus.Single().Items;
        Assert.Equal(8, items.Count);
        Assert.Equal<RadialDirection>(Enum.GetValues<RadialDirection>().Order(), items.Select(i => i.Position).Order());
        Assert.All(items, item => Assert.False(item.IsUnsupported));
    }

    [Fact]
    public void TheExample_SurvivesAWriteAndReadWithoutChanges()
    {
        var first = ConfigurationFile.Parse(File.ReadAllText(ExamplePath())).Configuration!;

        var second = ConfigurationFile.Parse(ConfigurationFile.Serialize(first));

        Assert.True(second.IsOk);
        Assert.Equal(ConfigurationFile.Serialize(first), ConfigurationFile.Serialize(second.Configuration!));
    }
}
