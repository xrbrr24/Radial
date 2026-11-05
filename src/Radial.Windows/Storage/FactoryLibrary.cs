using Radial.Core.Catalog;
using Radial.Core.Model;

namespace Radial.Windows.Storage;

/// <summary>Los destinos del equipo que usan las acciones de fábrica que abren algo. Nada se inventa: salen de carpetas conocidas del sistema.</summary>
public sealed record FactoryPaths(string Browser, string Documents, string Downloads, string Desktop, string Pictures, string WikipediaUrl)
{
    public const string DefaultWikipediaUrl = "https://www.wikipedia.org";

    /// <summary>Los destinos de este equipo. Si el sistema no da una carpeta se usa la raíz de la unidad de Windows.</summary>
    public static FactoryPaths FromEnvironment()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string OrRoot(string path) => string.IsNullOrWhiteSpace(path) ? root : path;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        }

        return new FactoryPaths(
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            OrRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            string.IsNullOrWhiteSpace(profile) ? root : Path.Combine(profile, "Downloads"),
            OrRoot(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            OrRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            DefaultWikipediaUrl);
    }
}

/// <summary>
/// La biblioteca de acciones de fábrica: los atajos de <see cref="FactoryActions"/> (no dependen del equipo) más las acciones que
/// abren programas y carpetas, que sí dependen de él (<see cref="FactoryPaths"/>). Son definiciones como cualquier otra: se pueden
/// editar y borrar, y las que falten se pueden volver a añadir. Un programa con nombre sin ruta (<c>notepad.exe</c>) lo resuelve
/// el sistema; si no existe, la acción falla al ejecutarse con <c>TargetNotFound</c> y se comunica.
/// </summary>
public static class FactoryLibrary
{
    /// <summary>La biblioteca de fábrica con los destinos de este equipo.</summary>
    public static IReadOnlyList<ActionDefinition> Create() => Create(FactoryPaths.FromEnvironment());

    public static IReadOnlyList<ActionDefinition> Create(FactoryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return
        [
            .. FactoryActions.Shortcuts,
            Make("web.browser", "Navegador", ActionCategory.Browser, "Globe", "Abre Microsoft Edge.", new LaunchApplicationAction(paths.Browser), "edge"),
            Make("web.url", "Web", ActionCategory.Browser, "Link", "Abre una dirección web en el navegador predeterminado.", new OpenUrlAction(paths.WikipediaUrl)),
            Make("files.folder", "Carpeta", ActionCategory.Files, "Folder", "Abre la carpeta Documentos en el Explorador.", new OpenFolderAction(paths.Documents), "documentos"),
            Make("files.downloads", "Descargas", ActionCategory.Files, "Download", "Abre la carpeta Descargas.", new OpenFolderAction(paths.Downloads)),
            Make("files.desktop", "Escritorio", ActionCategory.Files, "Desktop", "Abre la carpeta del escritorio.", new OpenFolderAction(paths.Desktop)),
            Make("files.pictures", "Imágenes", ActionCategory.Files, "Pictures", "Abre la carpeta Imágenes.", new OpenFolderAction(paths.Pictures), "fotos"),
            Make("app.notepad", "Bloc de notas", ActionCategory.Applications, "Edit", "Abre el Bloc de notas.", new LaunchApplicationAction("notepad.exe")),
            Make("app.calculator", "Calculadora", ActionCategory.Applications, "Calculator", "Abre la Calculadora.", new LaunchApplicationAction("calc.exe")),
            Make("app.paint", "Paint", ActionCategory.Applications, "Color", "Abre Paint.", new LaunchApplicationAction("mspaint.exe")),
            Make("app.explorer", "Explorador", ActionCategory.Applications, "Folder", "Abre el Explorador de archivos.", new LaunchApplicationAction("explorer.exe")),
            Make("app.snipping-tool", "Recortes", ActionCategory.Applications, "Camera", "Abre la herramienta Recortes.", new LaunchApplicationAction("SnippingTool.exe"), "captura"),
        ];
    }

    private static ActionDefinition Make(string id, string name, ActionCategory category, string glyph, string description, RadialAction action, params string[] tags) =>
        new(id, name, category, description, new IconReference(IconSource.Glyph, glyph), action, tags.Length == 0 ? null : tags, IsBuiltIn: true);
}
