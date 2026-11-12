using System.Text;
using System.Text.Json.Nodes;
using Radial.Core.Model;
using Radial.Core.Storage;
using Radial.Windows.Storage;

namespace Radial.Windows.Tests.Storage;

/// <summary>
/// El almacén en disco con una carpeta temporal real: primer inicio, carga, recuperación de archivos corruptos (cuarentena y copia
/// válida), versión futura intacta, guardado atómico con copias de seguridad rotadas y conservación de acciones desconocidas.
/// </summary>
public class FileSettingsStoreTests : IDisposable
{
    private readonly StorageTempDirectory _directory = new();
    private readonly TickingClock _clock = new();

    public void Dispose() => _directory.Dispose();

    private FileSettingsStore Store() => StorageSamples.Store(_directory, _clock);

    private string ConfigPath => Path.Combine(_directory.Root, "config.json");

    private string BackupsPath => Path.Combine(_directory.Root, "backups");

    private void WriteConfig(string content)
    {
        Directory.CreateDirectory(_directory.Root);
        File.WriteAllText(ConfigPath, content, new UTF8Encoding(false));
    }

    private string[] Backups() => Directory.Exists(BackupsPath) ? Directory.GetFiles(BackupsPath, "config-*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray() : [];

    private string[] Quarantined() => Directory.GetFiles(_directory.Root, "config.corrupt-*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray();

    private string[] Temporaries() => Directory.GetFiles(_directory.Root, "*.tmp");

    // Primer inicio y cargas posteriores

    [Fact]
    public void TheFirstRun_CreatesTheFolders_AndSavesTheDefaultConfiguration()
    {
        Assert.False(Directory.Exists(_directory.Root));

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.CreatedDefaults, result.Source);
        Assert.True(result.CanSave);
        Assert.True(File.Exists(ConfigPath));
        Assert.True(Directory.Exists(BackupsPath));
        Assert.True(Directory.Exists(Path.Combine(_directory.Root, "icons")));
        Assert.True(Directory.Exists(Path.Combine(_directory.Root, "logs")));
        Assert.Equal(StorageSamples.Text(StorageSamples.Defaults()), File.ReadAllText(ConfigPath));
        Assert.Equal(StorageSamples.Text(StorageSamples.Defaults()), StorageSamples.Text(result.Configuration));
        Assert.Empty(Temporaries());
        Assert.Empty(Backups());
    }

    [Fact]
    public void TheConfigurationFile_IsWrittenAsUtf8WithoutAByteOrderMark()
    {
        Store().LoadOrCreate();

        var bytes = File.ReadAllBytes(ConfigPath);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public void ASecondRun_LoadsTheSameConfiguration_WithoutRewritingTheFile()
    {
        var first = Store().LoadOrCreate();
        var bytesBefore = File.ReadAllBytes(ConfigPath);

        var second = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.Loaded, second.Source);
        Assert.Equal(StorageSamples.Text(first.Configuration), StorageSamples.Text(second.Configuration));
        Assert.Equal(bytesBefore, File.ReadAllBytes(ConfigPath));
        Assert.Empty(Backups());
        Assert.Empty(Quarantined());
    }

    [Fact]
    public void AHandEditedConfiguration_IsLoadedAndUsed()
    {
        WriteConfig(StorageSamples.CustomJson("Mi menú"));

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.Loaded, result.Source);
        Assert.Equal("Mi menú", result.Configuration.Menus[0].Name);
        Assert.DoesNotContain(result.Notices, n => n.Severity != SettingsNoticeSeverity.Info);
    }

    [Fact]
    public void StaleTemporaryFilesFromACrash_AreCleanedUp()
    {
        Directory.CreateDirectory(_directory.Root);
        var stale = Path.Combine(_directory.Root, "config.json.abcdef.tmp");
        File.WriteAllText(stale, "{");

        Store().LoadOrCreate();

        Assert.False(File.Exists(stale));
    }

    // Archivos corruptos

    [Theory]
    [InlineData("{ roto")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"schemaVersion\":1,\"menus\":5}")]
    [InlineData("{\"schemaVersion\":\"uno\"}")]
    public void ACorruptFile_IsPutInQuarantine_AndTheDefaultsAreUsed(string content)
    {
        WriteConfig(content);

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.DefaultsAfterProblem, result.Source);
        Assert.True(result.CanSave);
        var quarantined = Assert.Single(Quarantined());
        Assert.Equal(content, File.ReadAllText(quarantined)); // el original se conserva íntegro
        Assert.Equal(StorageSamples.Text(StorageSamples.Defaults()), File.ReadAllText(ConfigPath));
        Assert.Contains(result.Notices, n => n.Severity == SettingsNoticeSeverity.Warning);
        Assert.Contains(result.Notices, n => n.Message.Contains("cuarentena"));
    }

    [Fact]
    public void AFileThatParsesButIsNotValid_IsTreatedAsCorrupt()
    {
        // Dos elementos con el mismo identificador y la misma posición.
        var invalid = "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":["
            + "{\"id\":\"x\",\"name\":\"A\",\"position\":\"North\",\"action\":{\"type\":\"openFolder\",\"path\":\"C:\\\\A\"}},"
            + "{\"id\":\"x\",\"name\":\"B\",\"position\":\"North\",\"action\":{\"type\":\"openFolder\",\"path\":\"C:\\\\B\"}}]}]}";
        WriteConfig(invalid);

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.DefaultsAfterProblem, result.Source);
        Assert.Equal(invalid, File.ReadAllText(Assert.Single(Quarantined())));
    }

    [Fact]
    public void AFileWithInvalidActions_LoadsTheRest_NeutralizesOnlyThoseActions_AndIsNotQuarantined()
    {
        // Una dirección con un esquema no admitido (acción inválida), una carpeta con ruta relativa (destino imposible: solo aviso) y una buena.
        var json = "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":["
            + "{\"id\":\"a\",\"name\":\"Correo\",\"position\":\"North\",\"action\":{\"type\":\"openUrl\",\"url\":\"mailto:alguien@example.com\"}},"
            + "{\"id\":\"b\",\"name\":\"Relativa\",\"position\":\"East\",\"action\":{\"type\":\"openFolder\",\"path\":\"relativa\"}},"
            + "{\"id\":\"c\",\"name\":\"Buena\",\"position\":\"South\",\"action\":{\"type\":\"openFolder\",\"path\":\"C:\\\\Docs\"}}]}]}";
        WriteConfig(json);

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.Loaded, result.Source);
        Assert.True(result.CanSave);
        Assert.Empty(Quarantined());
        var items = result.Configuration.Menus[0].Items;
        Assert.Equal(3, items.Count);
        Assert.IsType<UnknownAction>(items.Single(i => i.Name == "Correo").Action);
        Assert.Equal(new OpenFolderAction("relativa"), items.Single(i => i.Name == "Relativa").Action);
        Assert.Equal(new OpenFolderAction(@"C:\Docs"), items.Single(i => i.Name == "Buena").Action);
        Assert.Contains(result.Notices, n => n.Severity == SettingsNoticeSeverity.Warning && n.Message.Contains("no es válida y no se ejecutará"));
        Assert.Contains(result.Notices, n => n.Severity == SettingsNoticeSeverity.Warning && n.Message.Contains("ruta relativa"));
    }

    [Fact]
    public void AnInvalidActionInTheFile_IsKeptVerbatimWhenTheConfigurationIsSavedAgain()
    {
        var json = "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":["
            + "{\"id\":\"a\",\"name\":\"Correo\",\"position\":\"North\",\"action\":{\"type\":\"openUrl\",\"url\":\"mailto:alguien@example.com\"}}]}]}";
        WriteConfig(json);
        var store = Store();
        var loaded = store.LoadOrCreate();

        var saved = store.Save(loaded.Configuration with { Geometry = loaded.Configuration.Geometry with { DeadZoneRadius = 55 } });

        Assert.True(saved.Success, saved.Message);
        Assert.Contains("mailto:alguien@example.com", File.ReadAllText(ConfigPath));
        Assert.Empty(Quarantined());
    }

    // Una acción inerte (inválida al cargar) y sus colocaciones: dos enlazadas a ella (una con nombre e icono propios) y una a una buena.
    private const string InertConfig = """
        {
          "schemaVersion": 2,
          "rootMenuId": "main",
          "actions": [
            { "id": "x", "name": "Correo", "category": "custom", "icon": { "source": "glyph", "value": "Link" },
              "action": { "type": "openUrl", "url": "mailto:alguien@example.com", "note": "campo extra" } },
            { "id": "ok", "name": "Docs", "category": "custom", "icon": { "source": "glyph", "value": "Folder" },
              "action": { "type": "openFolder", "path": "C:\\Docs" } }
          ],
          "menus": [
            { "id": "main", "name": "M", "items": [
              { "id": "a", "position": "North", "actionId": "x", "label": "Mi correo", "icon": { "source": "glyph", "value": "Star" } },
              { "id": "b", "position": "East", "actionId": "x" },
              { "id": "c", "position": "South", "actionId": "ok" }
            ] }
          ]
        }
        """;

    private const string NeutralizedText = "no es válida y no se ejecutará";

    [Fact]
    public void AnInertAction_KeepsItsIdentityLinkAndPlacements_AfterSavingAndRestarting()
    {
        WriteConfig(InertConfig);
        var store = Store();
        var loaded = store.LoadOrCreate();
        Assert.Equal(SettingsSource.Loaded, loaded.Source);
        Assert.Single(loaded.Notices, n => n.Message.Contains(NeutralizedText));

        var saved = store.Save(loaded.Configuration with { Geometry = loaded.Configuration.Geometry with { DeadZoneRadius = 55 } });
        Assert.True(saved.Success, saved.Message);

        // «Reiniciar»: un almacén nuevo sobre la misma carpeta.
        var restarted = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.Loaded, restarted.Source);
        Assert.Empty(Quarantined());
        Assert.Single(restarted.Notices, n => n.Message.Contains(NeutralizedText)); // un aviso por acción, no uno por colocación ni uno extra por guardar
        var definition = Assert.Single(restarted.Configuration.Actions, d => d.Id == "x");
        var inert = Assert.IsType<UnknownAction>(definition.Action);
        Assert.Contains("campo extra", inert.RawJson); // el JSON original, completo

        var items = restarted.Configuration.Menus[0].Items;
        string[] expectedIds = ["a", "b", "c"];
        Assert.Equal(expectedIds, items.Select(i => i.Id.Value).Order().ToArray());
        var a = items.Single(i => i.Id.Value == "a");
        var b = items.Single(i => i.Id.Value == "b");
        Assert.Equal("x", a.DefinitionId);
        Assert.Equal("x", b.DefinitionId);
        Assert.Equal("Mi correo", a.LabelOverride);
        Assert.Equal(inert, a.Action);
        Assert.Equal(inert, b.Action);
        Assert.Equal(new OpenFolderAction(@"C:\Docs"), items.Single(i => i.Id.Value == "c").Action);

        // En el archivo, las colocaciones siguen siendo referencias, y el contenido de la acción sigue ahí.
        var file = File.ReadAllText(ConfigPath);
        Assert.Contains("mailto:alguien@example.com", file);
        Assert.Contains("\"actionId\"", file);
    }

    [Fact]
    public void ABackupWithAnInvalidAction_IsRecoverable_RestoredVerbatim_AndItsWarningsAreShown()
    {
        // Mismo criterio que el archivo principal: una acción inválida no invalida la copia, solo se neutraliza y se avisa.
        Directory.CreateDirectory(BackupsPath);
        File.WriteAllText(Path.Combine(BackupsPath, "config-20200101-000000-000.json"), InertConfig);
        WriteConfig("{ original roto");

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.RecoveredFromBackup, result.Source);
        Assert.Equal(InertConfig, File.ReadAllText(ConfigPath)); // byte a byte: el JSON de la acción no se toca
        Assert.IsType<UnknownAction>(result.Configuration.Actions.Single(d => d.Id == "x").Action);
        Assert.Contains(result.Notices, n => n.Message.Contains("restaurada"));
        Assert.Contains(result.Notices, n => n.Severity == SettingsNoticeSeverity.Warning && n.Message.Contains(NeutralizedText));
        Assert.Equal("{ original roto", File.ReadAllText(Assert.Single(Quarantined())));
    }

    [Fact]
    public void ABackupWithStructuralErrors_IsStillSkipped_EvenWithTheTolerantLoad()
    {
        // La carga tolerante solo neutraliza acciones: identificadores y posiciones duplicados siguen invalidando la copia.
        Directory.CreateDirectory(BackupsPath);
        var structural = "{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":["
            + "{\"id\":\"x\",\"name\":\"A\",\"position\":\"North\",\"action\":{\"type\":\"openFolder\",\"path\":\"C:\\\\A\"}},"
            + "{\"id\":\"x\",\"name\":\"B\",\"position\":\"North\",\"action\":{\"type\":\"openFolder\",\"path\":\"C:\\\\B\"}}]}]}";
        File.WriteAllText(Path.Combine(BackupsPath, "config-20200101-000000-000.json"), structural);
        WriteConfig("{ original roto");

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.DefaultsAfterProblem, result.Source);
        Assert.Contains(result.Notices, n => n.Message.Contains("no es utilizable"));
    }

    [Fact]
    public void AnOversizedFile_IsPutInQuarantine_WithoutBeingRead()
    {
        Directory.CreateDirectory(_directory.Root);
        File.WriteAllBytes(ConfigPath, new byte[ConfigurationFile.MaxBytes + 1000]);

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.DefaultsAfterProblem, result.Source);
        Assert.Equal(ConfigurationFile.MaxBytes + 1000, new FileInfo(Assert.Single(Quarantined())).Length);
    }

    [Fact]
    public void ACorruptFile_IsRecoveredFromTheNewestValidBackup_SkippingInvalidOnes()
    {
        Directory.CreateDirectory(BackupsPath);
        var valid = StorageSamples.CustomJson("Copia válida");
        File.WriteAllText(Path.Combine(BackupsPath, "config-20200101-000000-000.json"), valid);
        File.WriteAllText(Path.Combine(BackupsPath, "config-20210101-000000-000.json"), "{ copia rota");
        WriteConfig("{ original roto");

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.RecoveredFromBackup, result.Source);
        Assert.Equal("Copia válida", result.Configuration.Menus[0].Name);
        Assert.Equal(valid, File.ReadAllText(ConfigPath));
        Assert.Equal("{ original roto", File.ReadAllText(Assert.Single(Quarantined())));
        Assert.Equal(2, Backups().Length); // las copias no se tocan
        Assert.Contains(result.Notices, n => n.Message.Contains("restaurada"));
    }

    [Fact]
    public void WithOnlyInvalidBackups_TheDefaultsAreUsed_NeverABrokenCopy()
    {
        Directory.CreateDirectory(BackupsPath);
        File.WriteAllText(Path.Combine(BackupsPath, "config-20210101-000000-000.json"), "basura");
        WriteConfig("{ roto");

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.DefaultsAfterProblem, result.Source);
        Assert.Equal(StorageSamples.Text(StorageSamples.Defaults()), File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void AFutureBackup_IsNotUsedToRecover()
    {
        Directory.CreateDirectory(BackupsPath);
        File.WriteAllText(Path.Combine(BackupsPath, "config-20210101-000000-000.json"), "{\"schemaVersion\":99}");
        WriteConfig("{ roto");

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.DefaultsAfterProblem, result.Source);
    }

    [Fact]
    public void QuarantinedFiles_AreLimited_ToTheNewestTen()
    {
        var store = Store();
        for (var cycle = 0; cycle < FileSettingsStore.MaxQuarantined + 3; cycle++)
        {
            WriteConfig("{ roto " + cycle);
            store.LoadOrCreate();
        }

        var quarantined = Quarantined();
        Assert.Equal(FileSettingsStore.MaxQuarantined, quarantined.Length);
        Assert.Contains("{ roto " + (FileSettingsStore.MaxQuarantined + 2), File.ReadAllText(quarantined[^1])); // se conservan las más recientes
    }

    // Versión futura

    [Fact]
    public void AFutureVersion_IsNeverOverwritten_NorQuarantined_AndSavingIsRefused()
    {
        var future = "{\"schemaVersion\":99,\"menus\":[{\"nuevo\":{\"anidado\":[1,2,3]}}]}";
        WriteConfig(future);
        var store = Store();

        var result = store.LoadOrCreate();

        Assert.Equal(SettingsSource.DefaultsNotPersisted, result.Source);
        Assert.False(result.CanSave);
        Assert.Contains(result.Notices, n => n.Severity == SettingsNoticeSeverity.Error);
        Assert.Equal(future, File.ReadAllText(ConfigPath));
        Assert.Empty(Quarantined());
        Assert.Empty(Backups());

        var save = store.Save(StorageSamples.Custom("Intento"));

        Assert.False(save.Success);
        Assert.Equal(future, File.ReadAllText(ConfigPath));
        Assert.Empty(Backups());
        Assert.Empty(Temporaries());
    }

    [Fact]
    public void TheDefaultsUsedForAFutureVersion_AreAValidConfiguration()
    {
        WriteConfig("{\"schemaVersion\":99}");

        var result = Store().LoadOrCreate();

        Assert.NotEmpty(result.Configuration.Menus);
        Assert.Equal(StorageSamples.Text(StorageSamples.Defaults()), StorageSamples.Text(result.Configuration));
    }

    // Problemas del disco que no son corrupción

    [Fact]
    public void AFileThatCannotBeRead_IsNotQuarantined_AndTheDefaultsAreUsedInMemory()
    {
        var original = StorageSamples.CustomJson("Bloqueada");
        WriteConfig(original);
        SettingsLoadResult result;

        using (new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = Store().LoadOrCreate();
        }

        Assert.Equal(SettingsSource.DefaultsNotPersisted, result.Source);
        Assert.False(result.CanSave);
        Assert.Empty(Quarantined());
        Assert.Equal(original, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void WhenTheDataFolderCannotBeCreated_TheAppKeepsWorkingWithTheDefaultsInMemory()
    {
        var blocker = Path.Combine(_directory.Path, "es-un-archivo");
        File.WriteAllText(blocker, "x");
        var store = new FileSettingsStore(Path.Combine(blocker, "Radial"), _clock.Next, StorageSamples.Defaults);

        var result = store.LoadOrCreate();

        Assert.Equal(SettingsSource.DefaultsNotPersisted, result.Source);
        Assert.False(result.CanSave);
        Assert.Contains(result.Notices, n => n.Severity == SettingsNoticeSeverity.Error);
        Assert.NotEmpty(result.Configuration.Menus);
        Assert.False(store.Save(StorageSamples.Custom("x")).Success);
    }

    // Guardado

    [Fact]
    public void Saving_ReplacesTheFileAtomically_AndKeepsTheOldVersionAsABackup()
    {
        var store = Store();
        store.LoadOrCreate();
        var before = File.ReadAllText(ConfigPath);

        var result = store.Save(StorageSamples.Custom("Nueva"));

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Equal(StorageSamples.CustomJson("Nueva"), File.ReadAllText(ConfigPath));
        var backup = Assert.Single(Backups());
        Assert.Equal(before, File.ReadAllText(backup));
        Assert.Empty(Temporaries());
    }

    [Fact]
    public void TheFirstWrite_CreatesTheFile_WithoutABackup()
    {
        var store = Store();

        var result = store.Save(StorageSamples.Custom("Primera"));

        Assert.True(result.Success);
        Assert.Equal(StorageSamples.CustomJson("Primera"), File.ReadAllText(ConfigPath));
        Assert.Empty(Backups());
        Assert.Empty(Temporaries());
    }

    [Fact]
    public void SavingTheSameConfiguration_DoesNothing_AndCreatesNoBackup()
    {
        var store = Store();
        store.Save(StorageSamples.Custom("Igual"));

        var again = store.Save(StorageSamples.Custom("Igual"));

        Assert.True(again.Success);
        Assert.False(again.Changed);
        Assert.Empty(Backups());
        Assert.Empty(Temporaries());
    }

    [Fact]
    public void Backups_AreRotated_KeepingOnlyTheNewestTen()
    {
        var store = Store();
        const int saves = 15;

        for (var n = 0; n < saves; n++)
        {
            Assert.True(store.Save(StorageSamples.Custom("versión " + n)).Success);
        }

        var backups = Backups();
        Assert.Equal(FileSettingsStore.MaxBackups, backups.Length);

        // La más reciente guarda la versión anterior a la última (saves - 2); la más antigua que se conserva, la (saves - 11).
        Assert.Equal(StorageSamples.CustomJson("versión " + (saves - 2)), File.ReadAllText(backups[^1]));
        Assert.Equal(StorageSamples.CustomJson("versión " + (saves - 1 - FileSettingsStore.MaxBackups)), File.ReadAllText(backups[0]));
        Assert.Equal(StorageSamples.CustomJson("versión " + (saves - 1)), File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void AnInvalidConfiguration_IsNotSaved_AndTheFileIsLeftUntouched()
    {
        var store = Store();
        store.Save(StorageSamples.Custom("Buena"));
        var before = File.ReadAllText(ConfigPath);
        var invalid = new RadialConfiguration { RootMenuId = new MenuId("no-existe"), Menus = [] };

        var result = store.Save(invalid);

        Assert.False(result.Success);
        Assert.Equal(before, File.ReadAllText(ConfigPath));
        Assert.Empty(Backups());
        Assert.Empty(Temporaries());
    }

    [Fact]
    public void ASavedConfiguration_IsRecoveredByANewStoreInstance()
    {
        Store().Save(StorageSamples.Custom("Persistida"));

        var result = Store().LoadOrCreate();

        Assert.Equal(SettingsSource.Loaded, result.Source);
        Assert.Equal("Persistida", result.Configuration.Menus[0].Name);
    }

    // Acciones desconocidas

    [Fact]
    public void AnUnknownAction_IsLoadedWithAWarning_AndKeptIntactWhenTheConfigurationIsSavedAgain()
    {
        var action = "{\"type\":\"futureAction\",\"nested\":{\"list\":[1,2,{\"deep\":null}],\"n\":1.50},\"flag\":true}";
        WriteConfig("{\"schemaVersion\":1,\"rootMenuId\":\"main\",\"menus\":[{\"id\":\"main\",\"name\":\"M\",\"items\":["
            + "{\"id\":\"future\",\"name\":\"Futura\",\"position\":\"North\",\"action\":" + action + "}]}]}");
        var store = Store();

        var loaded = store.LoadOrCreate();

        Assert.Equal(SettingsSource.Loaded, loaded.Source);
        var item = Assert.Single(loaded.Configuration.Menus[0].Items);
        Assert.True(item.IsUnsupported);
        Assert.Contains(loaded.Notices, n => n.Severity == SettingsNoticeSeverity.Warning && n.Message.Contains("future"));

        // Se guarda otra configuración derivada de la cargada: la acción desconocida sigue siendo idéntica.
        var save = store.Save(loaded.Configuration with { Navigation = new NavigationSettings { MaxDepth = 2 } });

        Assert.True(save.Success);
        var written = JsonNode.Parse(File.ReadAllText(ConfigPath))!;
        var writtenAction = written["menus"]![0]!["items"]![0]!["action"]!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(action), writtenAction));

        var reloaded = Store().LoadOrCreate();
        Assert.IsType<UnknownAction>(Assert.Single(reloaded.Configuration.Menus[0].Items).Action);
        Assert.Equal(2, reloaded.Configuration.Navigation.MaxDepth);
    }

    [Fact]
    public void TheDefaultRootDirectory_IsRadialUnderLocalAppData()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Radial");

        Assert.Equal(expected, FileSettingsStore.DefaultRootDirectory);
    }

    [Fact]
    public void TheStoreExposesTheDocumentedLayout()
    {
        var store = Store();

        Assert.Equal(Path.Combine(_directory.Root, "config.json"), store.ConfigPath);
        Assert.Equal(Path.Combine(_directory.Root, "backups"), store.BackupsDirectory);
        Assert.Equal(Path.Combine(_directory.Root, "icons"), store.IconsDirectory);
        Assert.Equal(Path.Combine(_directory.Root, "logs"), store.LogsDirectory);
    }
}
