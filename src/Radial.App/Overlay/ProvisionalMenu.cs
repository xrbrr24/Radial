using System.IO;
using Radial.Core.Catalog;
using Radial.Core.Geometry;
using Radial.Core.Model;

namespace Radial.App.Overlay;

/// <summary>
/// Destinos PROVISIONALES de las acciones que abren algo. Están aquí, en un único sitio y con nombre propio, porque la
/// configuración en disco y el editor son de fases posteriores (5 y 6). No se inventa ninguna ruta: cada una sale de una
/// carpeta conocida del sistema y, si no existe en este equipo, el ejecutor lo comprueba al ejecutar y avisa del fallo
/// (<c>TargetNotFound</c>), no lo silencia.
/// </summary>
internal static class ProvisionalTargets
{
    /// <summary>Microsoft Edge, que Windows 11 incluye en <c>Archivos de programa (x86)</c>. Sustituir por el navegador elegido.</summary>
    public static string BrowserPath => Path.Combine(KnownFolder(Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe");

    /// <summary>La carpeta Documentos del usuario (o, si el sistema no la da, la raíz de la unidad de Windows).</summary>
    public static string FolderPath
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(documents) ? Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\" : documents;
        }
    }

    public const string Url = "https://www.wikipedia.org";

    private static string KnownFolder(Environment.SpecialFolder preferred, Environment.SpecialFolder fallback)
    {
        var path = Environment.GetFolderPath(preferred);
        return string.IsNullOrWhiteSpace(path) ? Environment.GetFolderPath(fallback) : path;
    }
}

/// <summary>
/// Menú PROVISIONAL de ocho elementos fijos (fases 4.1 y 4.2). No viene de ningún archivo: la configuración en disco, el editor y los
/// submenús son de fases posteriores. Los elementos salen de un <see cref="ActionCatalog"/> (atajos del repertorio base y tres
/// acciones de apertura con destinos provisionales), así que cada acción tiene identificador estable, categoría y descripción, y
/// distintas acciones comparten ejecutor. Se sustituye por la configuración cargada de <c>%LOCALAPPDATA%\Radial\config.json</c>
/// cuando exista (fase 5).
/// </summary>
internal static class ProvisionalMenu
{
    public static RadialConfiguration Create()
    {
        var catalog = CreateCatalog();
        var root = new MenuId("main");
        return new RadialConfiguration
        {
            RootMenuId = root,
            Menus = [new RadialMenu(root, "Principal", Items(catalog))],
            Geometry = new GeometrySettings { DeadZoneRadius = 40, HysteresisDegrees = 6 },
        };
    }

    public static ActionCatalog CreateCatalog() =>
        new([
            .. BuiltInShortcuts.All,
            new ActionDefinition(
                "web.browser",
                "Navegador",
                ActionCategory.Web,
                "Abre Microsoft Edge (ruta provisional).",
                new IconReference(IconSource.Glyph, "Globe"),
                new LaunchApplicationAction(ProvisionalTargets.BrowserPath)),
            new ActionDefinition(
                "web.url",
                "Web",
                ActionCategory.Web,
                "Abre una dirección web en el navegador predeterminado (dirección provisional).",
                new IconReference(IconSource.Glyph, "Link"),
                new OpenUrlAction(ProvisionalTargets.Url)),
            new ActionDefinition(
                "files.folder",
                "Carpeta",
                ActionCategory.Files,
                "Abre una carpeta en el Explorador (carpeta provisional: Documentos).",
                new IconReference(IconSource.Glyph, "Folder"),
                new OpenFolderAction(ProvisionalTargets.FolderPath)),
        ]);

    // Posiciones, en sentido horario desde el norte: edición arriba y a la derecha, guardar abajo, aperturas a la izquierda.
    private static List<RadialItem> Items(ActionCatalog catalog) =>
    [
        catalog.Get("edit.copy").ToItem(RadialDirection.North),
        catalog.Get("edit.paste").ToItem(RadialDirection.NorthEast),
        catalog.Get("edit.undo").ToItem(RadialDirection.East),
        catalog.Get("edit.select-all").ToItem(RadialDirection.SouthEast),
        catalog.Get("edit.save").ToItem(RadialDirection.South),
        catalog.Get("files.folder").ToItem(RadialDirection.SouthWest),
        catalog.Get("web.url").ToItem(RadialDirection.West),
        catalog.Get("web.browser").ToItem(RadialDirection.NorthWest),
    ];
}
