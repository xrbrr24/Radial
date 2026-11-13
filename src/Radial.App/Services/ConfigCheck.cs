using System.IO;
using Radial.Core.Storage;
using Radial.Windows.Storage;

namespace Radial.App.Services;

/// <summary>
/// Herramienta de desarrollo y de comprobación de la versión portátil: <c>Radial.App.exe --config-check &lt;informe.txt&gt; --root &lt;carpeta&gt;</c>.
/// Carga la configuración (<c>config.json</c> de la carpeta indicada, o la crea si no existe) con el mismo almacén que usa la
/// aplicación, la vuelve a cargar para comprobar que lo escrito es válido, escribe un informe y termina. NO instala hooks, NO abre
/// ventanas, NO ejecuta ninguna acción y NO toca la configuración personal: <c>--root</c> es OBLIGATORIO y nunca se usa
/// <c>%LOCALAPPDATA%\Radial</c>. Código de salida: 0 = se cargó y se pudo volver a cargar; 1 = fallo; 2 = uso incorrecto.
/// </summary>
internal static class ConfigCheck
{
    public static int Run(string reportPath, string? root)
    {
        var lines = new List<string>();
        var exit = 1;
        try
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                lines.Add("ERROR: falta --root <carpeta>. Esta herramienta nunca usa la configuración personal.");
                exit = 2;
            }
            else
            {
                var fullRoot = Path.GetFullPath(root);
                var store = new FileSettingsStore(fullRoot);
                var first = store.LoadOrCreate();
                var second = new FileSettingsStore(fullRoot).LoadOrCreate();

                lines.Add($"root={fullRoot}");
                lines.Add($"configExists={File.Exists(store.ConfigPath)}");
                lines.Add($"firstLoad={first.Source} canSave={first.CanSave}");
                lines.Add($"secondLoad={second.Source} canSave={second.CanSave}");
                lines.Add($"menus={second.Configuration.Menus.Count} actions={second.Configuration.Actions.Count}");

                foreach (var group in second.Configuration.Actions.GroupBy(a => a.Action.GetType().Name).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    lines.Add($"actionType {group.Key}={group.Count()}");
                }

                foreach (var notice in first.Notices.Concat(second.Notices))
                {
                    lines.Add($"notice {notice.Severity}: {notice.Message}");
                }

                exit = second.Source == SettingsSource.Loaded && File.Exists(store.ConfigPath) ? 0 : 1;
            }
        }
        catch (Exception exception)
        {
            lines.Add("ERROR: " + exception.GetType().Name);
            exit = 1;
        }

        lines.Add($"exit={exit}");
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(reportPath, lines);
        }
        catch (Exception)
        {
            exit = 1;
        }

        return exit;
    }
}
