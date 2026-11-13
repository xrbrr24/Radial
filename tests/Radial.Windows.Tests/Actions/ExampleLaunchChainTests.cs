using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Windows.Actions;

namespace Radial.Windows.Tests.Actions;

/// <summary>
/// La cadena completa del ejemplo de la prueba manual, sin iniciar ningún proceso: del <c>config.json</c> de ejemplo a la acción
/// leída, de ella al lanzador (falso) y de la solicitud al <see cref="System.Diagnostics.ProcessStartInfo"/> que recibiría Windows.
/// Comprueba que un argumento con espacios llega SIEMPRE como un único elemento, sin shell ni línea de comandos compuesta a mano.
/// </summary>
public class ExampleLaunchChainTests
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

    private static List<LaunchApplicationAction> LaunchActionsOfTheExample() =>
        ConfigurationFile.Parse(File.ReadAllText(ExamplePath())).Configuration!.Actions
            .Select(a => a.Action)
            .OfType<LaunchApplicationAction>()
            .ToList();

    [Fact]
    public async Task TheSpacedFileOfTheExample_ReachesTheLauncherAsExactlyOneArgumentWithASpace()
    {
        var action = Assert.Single(LaunchActionsOfTheExample(), a => Assert.Single(a.Arguments!).Contains(' '));
        var launcher = new FakeProcessLauncher();

        var result = await new LaunchApplicationActionExecutor(launcher).ExecuteAsync(action);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(launcher.Requests);
        Assert.Equal("notepad.exe", request.Target);
        var argument = Assert.Single(request.Arguments!); // UN solo argumento, no dos mitades
        Assert.Equal(@"C:\Temp\prueba radial.txt", argument);
    }

    [Fact]
    public async Task EveryLaunchOfTheExample_BecomesAProcessStartInfoWithOneSeparateArgument_AndNoShell()
    {
        var actions = LaunchActionsOfTheExample();
        Assert.Equal(2, actions.Count);

        foreach (var action in actions)
        {
            var launcher = new FakeProcessLauncher();
            var result = await new LaunchApplicationActionExecutor(launcher).ExecuteAsync(action);
            Assert.True(result.IsSuccess);

            var info = ShellProcessLauncher.CreateStartInfo(Assert.Single(launcher.Requests));

            Assert.Equal("notepad.exe", info.FileName);
            Assert.Equal(Assert.Single(action.Arguments!), Assert.Single(info.ArgumentList)); // el mismo valor, sin partir ni comillas añadidas
            Assert.Equal(string.Empty, info.Arguments); // ninguna línea compuesta a mano: .NET aplica el entrecomillado al arrancar
            Assert.True(info.UseShellExecute);
            Assert.Equal(string.Empty, info.WorkingDirectory); // el ejemplo no fija directorio de trabajo
        }
    }

    [Fact]
    public void TheControlAction_UsesAFileThatExistsOnAnyWindows_AndHasNoSpaces()
    {
        var control = Assert.Single(LaunchActionsOfTheExample(), a => !Assert.Single(a.Arguments!).Contains(' '));

        Assert.Equal(@"C:\Windows\win.ini", Assert.Single(control.Arguments!));
    }
}
