using Radial.Core.Catalog;
using Radial.Core.Editing;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Storage;

namespace Radial.Core.Tests.Editing;

/// <summary>Búsqueda y filtrado de la biblioteca, y la sesión de edición (cambios sin guardar, guardado y descarte).</summary>
public class ActionSearchAndSessionTests
{
    private static ActionDefinition Def(string id, string name, RadialAction action, ActionCategory? category = null, string description = "", IReadOnlyList<string>? tags = null, bool builtIn = false) =>
        new(id, name, category ?? ActionCategory.Custom, description, new IconReference(IconSource.Glyph, "Apps"), action, tags, builtIn);

    private static ActionDefinition Chord(string id, string name, KeyModifiers modifiers, string key, ActionCategory? category = null, string description = "", IReadOnlyList<string>? tags = null) =>
        Def(id, name, new SendKeysAction(new KeyChord(modifiers, key)), category, description, tags);

    private static readonly IReadOnlyList<ActionDefinition> Library =
    [
        .. FactoryActions.Shortcuts,
        Def("custom.wiki", "Wikipedia", new OpenUrlAction("https://www.wikipedia.org"), ActionCategory.Browser, "Enciclopedia en línea", ["leer"]),
        Def("custom.bloc", "Bloc de notas", new LaunchApplicationAction("notepad.exe"), ActionCategory.Applications),
        Def("custom.docs", "Mis documentos", new OpenFolderAction(@"C:\Users\yo\Documentos"), ActionCategory.Files),
        Def("custom.informe", "Informe", new OpenFileAction(@"C:\Trabajo\informe.docx"), ActionCategory.Files),
        Chord("custom.enye", "Escribir eñe", KeyModifiers.Control, "Oem3"),
        Def("custom.future", "Futura", new UnknownAction("futureAction", "{\"type\":\"futureAction\"}")),
    ];

    private static string? Spanish(string key) => key switch
    {
        "Oem3" => "Ñ",
        "Delete" => "Supr",
        _ => null,
    };

    private static string CategoryName(ActionCategory category) => category.Id switch
    {
        "editing" => "Edición",
        "browser" => "Navegador",
        "multimedia" => "Multimedia",
        "applications" => "Aplicaciones",
        "files" => "Archivos",
        "system" => "Sistema",
        _ => "Personalizadas",
    };

    private static IReadOnlyList<ActionDefinition> Search(string? text, ActionFilter? filter = null, ISet<string>? placed = null) =>
        ActionSearch.Query(Library, text, filter, Spanish, CategoryName, placed);

    private static string[] Ids(IEnumerable<ActionDefinition> results) => results.Select(d => d.Id).ToArray();

    // Búsqueda

    [Fact]
    public void ByName_TheExactAndPrefixMatchesComeFirst()
    {
        var results = Search("copiar");

        Assert.Equal("edit.copy", results[0].Id);
    }

    [Theory]
    [InlineData("pestana", "browser.new-tab")]
    [InlineData("PESTAÑA", "browser.new-tab")]
    [InlineData("seleccionar", "edit.select-all")]
    [InlineData("administrador", "system.task-manager")]
    [InlineData("escribir ene", "custom.enye")]
    public void TheSearch_IgnoresCaseAndAccents(string query, string expectedId)
    {
        Assert.Contains(expectedId, Ids(Search(query)));
    }

    [Fact]
    public void ByDescription_AndByTag()
    {
        Assert.Contains("custom.wiki", Ids(Search("enciclopedia")));
        var byTag = Ids(Search("portapapeles"));
        Assert.Contains("edit.copy", byTag);
        Assert.Contains("edit.paste", byTag);
        Assert.Contains("custom.wiki", Ids(Search("leer")));
    }

    [Fact]
    public void ByCategory_IdAndDisplayName()
    {
        Assert.Contains("media.mute", Ids(Search("multimedia")));
        Assert.Contains("edit.undo", Ids(Search("edicion"))); // el nombre que se muestra, sin tilde
        Assert.Contains("custom.bloc", Ids(Search("aplicaciones")));
    }

    [Fact]
    public void ByTarget_ProgramPathOrAddress()
    {
        Assert.Contains("custom.wiki", Ids(Search("wikipedia.org")));
        Assert.Contains("custom.bloc", Ids(Search("notepad")));
        Assert.Contains("custom.informe", Ids(Search("informe.docx")));
    }

    [Theory]
    [InlineData("ctrl+s")]
    [InlineData("ctrl s")]
    [InlineData("Ctrl+S")]
    [InlineData("ctrls")]
    public void ByKeyCombination_InAnyWayOfWritingIt(string query)
    {
        var results = Search(query);

        Assert.Equal("edit.save", results[0].Id); // la coincidencia exacta de la combinación va primero
    }

    [Fact]
    public void ByKeyCombination_WithSeveralModifiers()
    {
        Assert.Equal("browser.reopen-tab", Search("ctrl+shift+t")[0].Id);
        Assert.Equal("browser.reopen-tab", Search("ctrl mayus t")[0].Id); // con el nombre de la tecla Mayús en español
        Assert.Equal("system.screenshot", Search("win shift s")[0].Id);
        Assert.Equal("media.play-pause", Ids(Search("MediaPlayPause")).First());
    }

    [Fact]
    public void ByTheNameOfTheKeyOnTheActiveLayout()
    {
        // Ctrl+Oem3 se ve como Ctrl+Ñ con el teclado español: se encuentra por lo que se lee en la tecla.
        Assert.Contains("custom.enye", Ids(Search("ctrl ñ")));
        Assert.Contains("custom.enye", Ids(Search("ctrl+n")));
        Assert.Contains("edit.delete", Ids(Search("supr")));
    }

    [Fact]
    public void NoMatch_ReturnsAnEmptyList_AndWhitespaceReturnsEverything()
    {
        Assert.Empty(Search("zzzz-no-existe"));
        Assert.Equal(Library.Count, Search("   ").Count);
        Assert.Equal(Library.Count, Search(null).Count);
    }

    [Fact]
    public void EveryWord_MustMatchSomewhere()
    {
        Assert.NotEmpty(Search("pestaña nueva"));
        Assert.Empty(Search("pestaña zzz"));
    }

    [Fact]
    public void WithoutAQuery_TheOrderIsByCategoryThenName()
    {
        var results = Search(null);

        var firstCategories = results.Select(d => d.Category.Id).Distinct().Take(3).ToArray();
        Assert.Equal(new[] { "editing", "browser", "applications" }, firstCategories);
        Assert.Equal("custom", results[^1].Category.Id);
    }

    // Filtros

    [Fact]
    public void FilterByCategory_AndByKind_AndTheirCombination()
    {
        Assert.All(Search(null, new ActionFilter(Category: ActionCategory.Multimedia)), d => Assert.Equal(ActionCategory.Multimedia, d.Category));
        Assert.Equal(new[] { "custom.docs", "custom.informe" }, Ids(Search(null, new ActionFilter(Category: ActionCategory.Files))).Order().ToArray());
        Assert.Equal(new[] { "custom.wiki" }, Ids(Search(null, new ActionFilter(Kind: ActionKind.Url))));
        Assert.Equal(new[] { "custom.bloc" }, Ids(Search(null, new ActionFilter(Kind: ActionKind.Application))));
        Assert.Equal(new[] { "custom.docs" }, Ids(Search(null, new ActionFilter(Kind: ActionKind.Folder))));
        Assert.Equal(new[] { "custom.informe" }, Ids(Search(null, new ActionFilter(Kind: ActionKind.File))));
        Assert.Equal(new[] { "custom.future" }, Ids(Search(null, new ActionFilter(Kind: ActionKind.Unsupported))));
        Assert.Empty(Search(null, new ActionFilter(Category: ActionCategory.Files, Kind: ActionKind.Shortcut)));
    }

    [Fact]
    public void FilterByOrigin_BuiltInOrCustom()
    {
        Assert.All(Search(null, new ActionFilter(BuiltIn: true)), d => Assert.True(d.IsBuiltIn));
        Assert.All(Search(null, new ActionFilter(BuiltIn: false)), d => Assert.False(d.IsBuiltIn));
        Assert.Equal(Library.Count, Search(null, new ActionFilter(BuiltIn: true)).Count + Search(null, new ActionFilter(BuiltIn: false)).Count);
    }

    [Fact]
    public void FilterByPlacement_UsesTheSetOfPlacedIds()
    {
        ISet<string> placed = new HashSet<string> { "edit.copy", "custom.wiki" };

        Assert.Equal(new[] { "custom.wiki", "edit.copy" }, Ids(Search(null, new ActionFilter(Placed: true), placed)).Order().ToArray());
        Assert.DoesNotContain("edit.copy", Ids(Search(null, new ActionFilter(Placed: false), placed)));
        Assert.Empty(Search(null, new ActionFilter(Placed: true))); // sin conjunto de colocadas, ninguna lo está
    }

    [Fact]
    public void SearchAndFilter_CombineAndStayFastWithAHugeLibrary()
    {
        var huge = Enumerable.Range(0, 5000)
            .Select(n => Def($"gen.{n}", $"Acción generada {n}", new OpenUrlAction($"https://example.com/{n}"), ActionCategory.Custom, "Una descripción más o menos larga para buscar", ["gen"]))
            .Concat(Library)
            .ToList();

        var results = ActionSearch.Query(huge, "generada 4999", new ActionFilter(Kind: ActionKind.Url), Spanish, CategoryName);

        Assert.Equal("gen.4999", results[0].Id);
        Assert.Equal(huge.Count, ActionSearch.Query(huge, null).Count);
    }

    [Fact]
    public void Normalize_StripsAccentsAndCase_WithoutDependingOnTheCulture()
    {
        Assert.Equal("nandu", ActionSearch.Normalize("Ñandú"));
        Assert.Equal(string.Empty, ActionSearch.Normalize(null));
        Assert.Equal("cafe", ActionSearch.Normalize("CAFÉ"));
    }

    [Fact]
    public void ActionKinds_AreDerivedFromTheActionType()
    {
        Assert.Equal(ActionKind.Shortcut, ActionKinds.Of(new SendKeysAction(new KeyChord(KeyModifiers.None, "A"))));
        Assert.Equal(ActionKind.Unsupported, ActionKinds.Of(new OpenMenuAction(new MenuId("x"))));
        Assert.Equal(ActionKind.Unsupported, ActionKinds.Of(new UnknownAction("x", "{}")));
    }

    // Sesión de edición

    private sealed class FakeStore : ISettingsStore
    {
        public List<RadialConfiguration> Saved { get; } = [];

        public bool Fail { get; set; }

        public SettingsLoadResult LoadOrCreate() => throw new NotSupportedException("La sesión no vuelve a cargar.");

        public SettingsSaveResult Save(RadialConfiguration configuration)
        {
            if (Fail)
            {
                return SettingsSaveResult.Failed("Disco lleno.");
            }

            Saved.Add(configuration);
            return SettingsSaveResult.Saved;
        }
    }

    private static RadialConfiguration BaseConfiguration()
    {
        var copy = FactoryActions.Shortcuts.Single(d => d.Id == "edit.copy");
        var main = new MenuId("main");
        return new RadialConfiguration
        {
            RootMenuId = main,
            Actions = [copy, FactoryActions.Shortcuts.Single(d => d.Id == "edit.cut")],
            Menus = [new RadialMenu(main, "Principal", [copy.ToItem(main, RadialDirection.North)])],
        };
    }

    private static ConfigurationSession Session(FakeStore store, bool canSave = true) =>
        new(store, new SettingsLoadResult(BaseConfiguration(), SettingsSource.Loaded, [], canSave));

    [Fact]
    public void ANewSession_HasNoPendingChanges()
    {
        var session = Session(new FakeStore());

        Assert.False(session.IsDirty);
        Assert.Null(session.LastSaveError);
    }

    [Fact]
    public void AnEdit_MarksTheSessionDirty_AndSavingCleansIt()
    {
        var store = new FakeStore();
        var session = Session(store);
        RadialConfiguration? savedEvent = null;
        session.Saved += c => savedEvent = c;

        Assert.True(session.Editor.PlaceDefinition(new MenuId("main"), RadialDirection.South, "edit.cut").Success);
        Assert.True(session.IsDirty);

        var result = session.Save();

        Assert.True(result.Success);
        Assert.False(session.IsDirty);
        Assert.Single(store.Saved);
        Assert.Same(session.Current, savedEvent);
    }

    [Fact]
    public void SavingWithoutChanges_DoesNotTouchTheStore()
    {
        var store = new FakeStore();
        var session = Session(store);

        var result = session.Save();

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void UndoingAChangeByHand_LeavesTheSessionCleanAgain_WithoutWriting()
    {
        var store = new FakeStore();
        var session = Session(store);
        var main = new MenuId("main");

        session.Editor.PlaceDefinition(main, RadialDirection.South, "edit.cut");
        Assert.True(session.IsDirty);
        session.Editor.RemovePlacement(new ItemId("main.edit.cut"));

        Assert.False(session.IsDirty);
        session.Save();
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void AFailedSave_KeepsTheChanges_ReportsTheReason_AndCanBeRetried()
    {
        var store = new FakeStore { Fail = true };
        var session = Session(store);
        session.Editor.PlaceDefinition(new MenuId("main"), RadialDirection.South, "edit.cut");
        var raised = 0;
        session.Saved += _ => raised++;

        var failed = session.Save();

        Assert.False(failed.Success);
        Assert.True(session.IsDirty);
        Assert.Equal("Disco lleno.", session.LastSaveError);
        Assert.Equal(0, raised);
        Assert.NotNull(session.Current.Menus[0].GetItem(RadialDirection.South)); // los cambios siguen ahí

        store.Fail = false;
        var retried = session.Save();

        Assert.True(retried.Success);
        Assert.False(session.IsDirty);
        Assert.Null(session.LastSaveError);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Discarding_RestoresTheLastSavedConfiguration()
    {
        var store = new FakeStore();
        var session = Session(store);
        var main = new MenuId("main");
        session.Editor.PlaceDefinition(main, RadialDirection.South, "edit.cut");
        session.Save();
        session.Editor.RemovePlacement(new ItemId("main.edit.copy"));
        Assert.True(session.IsDirty);

        session.Discard();

        Assert.False(session.IsDirty);
        Assert.NotNull(session.Current.Menus[0].GetItem(RadialDirection.North)); // lo borrado sin guardar vuelve
        Assert.NotNull(session.Current.Menus[0].GetItem(RadialDirection.South)); // lo guardado se conserva
    }

    [Fact]
    public void AfterDiscarding_TheSessionKeepsWorkingOnTheNewEditor()
    {
        var session = Session(new FakeStore());
        var main = new MenuId("main");
        session.Editor.PlaceDefinition(main, RadialDirection.South, "edit.cut");

        session.Discard();
        Assert.True(session.Editor.PlaceDefinition(main, RadialDirection.East, "edit.cut").Success);

        Assert.True(session.IsDirty);
        Assert.Null(session.Current.Menus[0].GetItem(RadialDirection.South));
    }

    [Fact]
    public void Changed_IsRaisedByEdits_Saves_AndDiscards()
    {
        var session = Session(new FakeStore());
        var raised = 0;
        session.Changed += () => raised++;
        var main = new MenuId("main");

        session.Editor.PlaceDefinition(main, RadialDirection.South, "edit.cut");
        var afterEdit = raised;
        session.Save();
        var afterSave = raised;
        session.Discard();

        Assert.True(afterEdit >= 1);
        Assert.True(afterSave > afterEdit);
        Assert.True(raised > afterSave);
    }

    [Fact]
    public void CanSave_ReflectsWhetherTheLoadedConfigurationMayBeOverwritten()
    {
        Assert.True(Session(new FakeStore(), canSave: true).CanSave);
        Assert.False(Session(new FakeStore(), canSave: false).CanSave);
    }
}
