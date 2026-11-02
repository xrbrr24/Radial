using Radial.Core.Geometry;
using Radial.Core.Model;

namespace Radial.Core.Tests.Support;

internal static class TestConfigs
{
    public static readonly MenuId Main = new("main");
    public static readonly MenuId Work = new("work");
    public static readonly MenuId Deep = new("deep");
    public static readonly MenuId Media = new("media");

    public static RadialItem Item(string id, RadialDirection position, RadialAction action) =>
        new(new ItemId(id), $"Item {id}", position, action);

    public static RadialMenu Menu(MenuId id, params RadialItem[] items) => new(id, $"Menu {id}", items);

    /// <summary>
    /// main: N app, NE url, E grupo work, S carpeta, W teclas, SW grupo media (SE y NO vacíos).
    /// work: N archivo, E grupo deep. deep: N carpeta. media: N app. Profundidad máxima alcanzada: 3.
    /// </summary>
    public static RadialConfiguration Standard() => new()
    {
        RootMenuId = Main,
        Geometry = new GeometrySettings { DeadZoneRadius = 30, HysteresisDegrees = 5 },
        Navigation = new NavigationSettings
        {
            SubmenuDwell = TimeSpan.FromMilliseconds(500),
            BackDwell = TimeSpan.FromMilliseconds(400),
            MaxDepth = 3,
        },
        Menus =
        [
            Menu(
                Main,
                Item("browser", RadialDirection.North, new LaunchApplicationAction(@"C:\Apps\browser.exe")),
                Item("docs", RadialDirection.NorthEast, new OpenUrlAction("https://example.com")),
                Item("work", RadialDirection.East, new OpenMenuAction(Work)),
                Item("folder", RadialDirection.South, new OpenFolderAction(@"C:\Users\Test")),
                Item("copy", RadialDirection.West, new SendKeysAction(new KeyChord(KeyModifiers.Control, "C"))),
                Item("media", RadialDirection.SouthWest, new OpenMenuAction(Media))),
            Menu(
                Work,
                Item("notes", RadialDirection.North, new OpenFileAction(@"C:\Docs\notes.txt")),
                Item("deep", RadialDirection.East, new OpenMenuAction(Deep))),
            Menu(Deep, Item("log", RadialDirection.North, new OpenFolderAction(@"C:\Logs"))),
            Menu(Media, Item("player", RadialDirection.North, new LaunchApplicationAction(@"C:\Apps\player.exe"))),
        ],
    };

    /// <summary>Copia de <paramref name="configuration"/> con el menú de igual id sustituido.</summary>
    public static RadialConfiguration WithMenu(RadialConfiguration configuration, RadialMenu replacement) =>
        configuration with { Menus = configuration.Menus.Select(m => m.Id == replacement.Id ? replacement : m).ToList() };

    /// <summary>Copia con una lista de menús distinta.</summary>
    public static RadialConfiguration WithMenus(RadialConfiguration configuration, params RadialMenu[] menus) =>
        configuration with { Menus = menus };
}
