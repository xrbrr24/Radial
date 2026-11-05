using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Windows.Storage;

namespace Radial.Windows.Tests.Storage;

/// <summary>Una carpeta temporal propia para el almacén de configuración; se borra entera al terminar.</summary>
internal sealed class StorageTempDirectory : IDisposable
{
    public StorageTempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "radial-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>La carpeta de datos de Radial dentro de la temporal (todavía sin crear).</summary>
    public string Root => System.IO.Path.Combine(Path, "Radial");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Un reloj que avanza un segundo en cada lectura: las marcas de tiempo de las copias son únicas y están ordenadas.</summary>
internal sealed class TickingClock
{
    private DateTimeOffset _now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Next()
    {
        _now = _now.AddSeconds(1);
        return _now;
    }
}

internal static class StorageSamples
{
    /// <summary>La configuración inicial con destinos explícitos (no dependen del equipo).</summary>
    public static RadialConfiguration Defaults() => DefaultConfiguration.Create(@"C:\Apps\Edge\msedge.exe", @"C:\Users\prueba\Documents", "https://www.wikipedia.org");

    /// <summary>Una configuración mínima distinta de la predeterminada, con el nombre del menú raíz que se indique.</summary>
    public static RadialConfiguration Custom(string rootName) => new()
    {
        RootMenuId = new MenuId("main"),
        Menus =
        [
            new RadialMenu(
                new MenuId("main"),
                rootName,
                [new RadialItem(new ItemId("main.folder"), "Carpeta", RadialDirection.North, new OpenFolderAction(@"C:\Temp"))]),
        ],
    };

    public static string CustomJson(string rootName) => ConfigurationFile.Serialize(Custom(rootName));

    public static string Text(RadialConfiguration configuration) => ConfigurationFile.Serialize(configuration);

    public static FileSettingsStore Store(StorageTempDirectory directory, TickingClock? clock = null) =>
        new(directory.Root, (clock ?? new TickingClock()).Next, Defaults);
}
