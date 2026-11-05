using Radial.Core.Catalog;
using Radial.Core.Geometry;
using Radial.Core.Model;

namespace Radial.Windows.Storage;

/// <summary>
/// La configuración inicial: las ocho acciones de las fases 4.1 y 4.2. Cinco atajos de edición en el menú principal y las tres
/// acciones de apertura (Carpeta, Web, Navegador) dentro del grupo «Abrir», así que el primer inicio ya demuestra la navegación
/// entre menús. Se genera a partir del repertorio de acciones (<see cref="ActionCatalog"/>): cada acción se COLOCA en un menú con
/// su propio identificador de elemento (<c>{menú}.{acción}</c>) y recuerda su definición. Los destinos salen de carpetas
/// conocidas del sistema y no se inventan rutas: si no existen en el equipo, la acción falla con <c>TargetNotFound</c> y se
/// comunica. Es solo datos: se guarda en <c>config.json</c> en el primer inicio y a partir de ahí manda el archivo.
/// </summary>
public static class DefaultConfiguration
{
    public const string RootMenuName = "main";
    public const string OpenMenuName = "open";
    public const string WikipediaUrl = "https://www.wikipedia.org";

    /// <summary>Configuración inicial con los destinos de este equipo.</summary>
    public static RadialConfiguration Create() => Create(EdgePath(), DocumentsPath(), WikipediaUrl);

    /// <summary>Configuración inicial con destinos explícitos (pruebas y casos en que el sistema no los da).</summary>
    public static RadialConfiguration Create(string browserPath, string folderPath, string url)
    {
        var catalog = CreateCatalog(browserPath, folderPath, url);
        var root = new MenuId(RootMenuName);
        var open = new MenuId(OpenMenuName);

        return new RadialConfiguration
        {
            RootMenuId = root,
            Menus =
            [
                new RadialMenu(
                    root,
                    "Principal",
                    [
                        catalog.Get("edit.copy").ToItem(root, RadialDirection.North),
                        catalog.Get("edit.paste").ToItem(root, RadialDirection.NorthEast),
                        catalog.Get("edit.undo").ToItem(root, RadialDirection.East),
                        catalog.Get("edit.select-all").ToItem(root, RadialDirection.SouthEast),
                        catalog.Get("edit.save").ToItem(root, RadialDirection.South),
                        new RadialItem(
                            new ItemId($"{RootMenuName}.{OpenMenuName}"),
                            "Abrir",
                            RadialDirection.West,
                            new OpenMenuAction(open),
                            new IconReference(IconSource.Glyph, "Apps")),
                    ]),
                new RadialMenu(
                    open,
                    "Abrir",
                    [
                        catalog.Get("files.folder").ToItem(open, RadialDirection.North),
                        catalog.Get("web.url").ToItem(open, RadialDirection.East),
                        catalog.Get("web.browser").ToItem(open, RadialDirection.South),
                    ]),
            ],
            Geometry = new GeometrySettings { DeadZoneRadius = 40, HysteresisDegrees = 6 },
        };
    }

    /// <summary>El repertorio de acciones de la configuración inicial: los atajos base y las tres aperturas.</summary>
    public static ActionCatalog CreateCatalog(string browserPath, string folderPath, string url) =>
        new([
            .. BuiltInShortcuts.All,
            new ActionDefinition(
                "web.browser",
                "Navegador",
                ActionCategory.Web,
                "Abre Microsoft Edge.",
                new IconReference(IconSource.Glyph, "Globe"),
                new LaunchApplicationAction(browserPath)),
            new ActionDefinition(
                "web.url",
                "Web",
                ActionCategory.Web,
                "Abre una dirección web en el navegador predeterminado.",
                new IconReference(IconSource.Glyph, "Link"),
                new OpenUrlAction(url)),
            new ActionDefinition(
                "files.folder",
                "Carpeta",
                ActionCategory.Files,
                "Abre una carpeta en el Explorador (la carpeta Documentos).",
                new IconReference(IconSource.Glyph, "Folder"),
                new OpenFolderAction(folderPath)),
        ]);

    /// <summary>Microsoft Edge, que Windows 11 incluye en <c>Archivos de programa (x86)</c>.</summary>
    private static string EdgePath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        }

        return Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe");
    }

    /// <summary>La carpeta Documentos del usuario (o, si el sistema no la da, la raíz de la unidad de Windows).</summary>
    private static string DocumentsPath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(documents) ? Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\" : documents;
    }
}
