using Radial.Core.Catalog;
using Radial.Core.Geometry;
using Radial.Core.Model;

namespace Radial.Windows.Storage;

/// <summary>
/// La configuración inicial: la biblioteca de acciones de fábrica completa (<see cref="FactoryLibrary"/>, también las acciones que
/// todavía no están colocadas) y un menú con las ocho acciones de las fases 4.1 y 4.2: cinco atajos de edición en el menú principal
/// y las tres de apertura (Carpeta, Web, Navegador) dentro del grupo «Abrir», así que el primer inicio ya enseña la navegación
/// entre menús. Cada acción se COLOCA con su propio identificador de elemento (<c>{menú}.{acción}</c>) y queda enlazada a su
/// definición. Los destinos salen de carpetas conocidas del sistema y no se inventan rutas. Es solo datos: se guarda en
/// <c>config.json</c> en el primer inicio y a partir de ahí manda el archivo.
/// </summary>
public static class DefaultConfiguration
{
    public const string RootMenuName = "main";
    public const string OpenMenuName = "open";
    public const string WikipediaUrl = FactoryPaths.DefaultWikipediaUrl;

    /// <summary>Configuración inicial con los destinos de este equipo.</summary>
    public static RadialConfiguration Create() => Create(FactoryPaths.FromEnvironment());

    /// <summary>Configuración inicial con destinos explícitos (pruebas): las demás carpetas cuelgan de la de documentos.</summary>
    public static RadialConfiguration Create(string browserPath, string folderPath, string url) =>
        Create(new FactoryPaths(
            browserPath,
            folderPath,
            Path.Combine(folderPath, "Descargas"),
            Path.Combine(folderPath, "Escritorio"),
            Path.Combine(folderPath, "Imagenes"),
            url));

    public static RadialConfiguration Create(FactoryPaths paths)
    {
        var library = FactoryLibrary.Create(paths);
        var catalog = new ActionCatalog(library);
        var root = new MenuId(RootMenuName);
        var open = new MenuId(OpenMenuName);

        return new RadialConfiguration
        {
            RootMenuId = root,
            Actions = library,
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
}
