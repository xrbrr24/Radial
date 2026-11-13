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
    public void TheExample_ContainsEveryNewKindOfAction_WithTheDataTheManualTestNeeds()
    {
        var configuration = ConfigurationFile.Parse(File.ReadAllText(ExamplePath())).Configuration!;
        var actions = configuration.Actions.Select(a => a.Action).ToList();

        // El texto de prueba lleva tildes, ñ, un emoji (par sustituto) y un salto de línea.
        var text = Assert.Single(actions.OfType<TypeTextAction>()).Text;
        Assert.Contains('ñ', text);
        Assert.Contains('Ñ', text);
        Assert.Contains('é', text);
        Assert.Contains("\r\n", text);
        Assert.Contains(text, c => char.IsHighSurrogate(c));

        Assert.Equal<VolumeOperation>(
            new[] { VolumeOperation.Increase, VolumeOperation.Decrease, VolumeOperation.SetLevel, VolumeOperation.ToggleMute },
            actions.OfType<VolumeAction>().Select(v => v.Operation));
        Assert.Equal<WindowOperation>(
            new[] { WindowOperation.Minimize, WindowOperation.Maximize, WindowOperation.Restore, WindowOperation.Close },
            actions.OfType<WindowControlAction>().Select(w => w.Operation));

        // Notepad dos veces, siempre con UN solo argumento: el control (un archivo que existe en cualquier Windows) y el archivo
        // temporal con espacio que crea el usuario. Sin directorio de trabajo fijo y sin rutas personales.
        var launches = actions.OfType<LaunchApplicationAction>().ToDictionary(a => Assert.Single(a.Arguments!));
        Assert.Equal(2, launches.Count);
        Assert.All(launches.Values, launch =>
        {
            Assert.Equal("notepad.exe", launch.Path);
            Assert.Null(launch.WorkingDirectory);
        });
        Assert.Contains(@"C:\Windows\win.ini", launches.Keys);
        Assert.Contains(@"C:\Temp\prueba radial.txt", launches.Keys);
        Assert.DoesNotContain(@"\Users\", File.ReadAllText(ExamplePath()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryActionOfTheExample_IsReachableFromTheRootRadial_ThroughItsSubmenus()
    {
        var configuration = ConfigurationFile.Parse(File.ReadAllText(ExamplePath())).Configuration!;
        var menus = configuration.Menus.ToDictionary(m => m.Id);

        // Recorrido desde el menú raíz por los grupos (con la profundidad como en el motor: la raíz cuenta).
        var reachedDefinitions = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<MenuId>();
        var pending = new Stack<(MenuId Id, int Depth)>();
        pending.Push((configuration.RootMenuId, 1));
        while (pending.Count > 0)
        {
            var (id, depth) = pending.Pop();
            Assert.True(depth <= configuration.Navigation.MaxDepth, $"El menú {id.Value} queda a profundidad {depth}.");
            Assert.True(visited.Add(id), $"El menú {id.Value} se alcanza por dos caminos.");
            foreach (var item in menus[id].Items)
            {
                Assert.False(item.IsUnsupported);
                if (item.Action is OpenMenuAction group)
                {
                    pending.Push((group.TargetMenuId, depth + 1));
                }
                else
                {
                    reachedDefinitions.Add(Assert.IsType<string>(item.DefinitionId));
                }
            }
        }

        Assert.Equal(menus.Count, visited.Count); // ningún menú queda sin alcanzar
        Assert.Equal(
            configuration.Actions.Select(a => a.Id).Order(StringComparer.Ordinal),
            reachedDefinitions.Order(StringComparer.Ordinal)); // todas las acciones se pueden colocar en un sector y alcanzar
        Assert.Equal(4, menus[configuration.RootMenuId].Items.Count);
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
