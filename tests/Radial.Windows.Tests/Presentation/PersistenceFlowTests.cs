using Radial.Core.Editing;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Validation;
using Radial.Windows.Presentation;
using Radial.Windows.Storage;
using Radial.Windows.Tests.Storage;

namespace Radial.Windows.Tests.Presentation;

/// <summary>
/// El flujo completo que describe la aceptación de la fase, sobre el almacén REAL en una carpeta temporal: crear acciones, guardarlas
/// sin colocar, reutilizarlas en varios menús, crear un grupo, organizar, guardar, «reiniciar» (cargar de nuevo desde el disco) y
/// recuperar todo. Los modelos de vista son los mismos que usa la aplicación; solo cambian los selectores y los diálogos por dobles.
/// </summary>
public class PersistenceFlowTests
{
    private static (ShellViewModel Shell, ConfigurationSession Session) Open(FileSettingsStore store)
    {
        var session = new ConfigurationSession(store, store.LoadOrCreate());
        var dialogs = new FakeDialogs();
        var services = new UiServices(dialogs, new FakeFilePicker(), dialogs, new SpanishLabels());
        return (new ShellViewModel(session, services, new InAppDialogs(), new object()), session);
    }

    [Fact]
    public void TheWholeAcceptanceFlow_SurvivesARestart()
    {
        using var directory = new StorageTempDirectory();

        // Primer arranque: se crea la configuración con su biblioteca.
        var (shell, session) = Open(StorageSamples.Store(directory));
        Assert.False(session.IsDirty);

        // Crear un atajo grabando la combinación (Win + Mayús + S) y una acción web; se guardan sin colocar.
        shell.Library.New.Execute(ActionKind.Shortcut);
        shell.Library.Form!.Name = "Captura";
        shell.Library.Form.Key.StartRecording();
        shell.Library.Form.Key.HandleKey(0x53, KeyModifiers.Windows | KeyModifiers.Shift);
        shell.Library.SaveForm.Execute(null);
        var shortcutId = shell.Library.Selected!.Id;

        shell.Library.New.Execute(ActionKind.Url);
        shell.Library.Form!.Name = "Mi web";
        shell.Library.Form.Url = "https://example.com/inicio";
        shell.Library.SaveForm.Execute(null);
        var webId = shell.Library.Selected!.Id;
        Assert.Empty(session.Editor.PlacementsOf(webId));
        Assert.Empty(session.Editor.PlacementsOf(shortcutId));

        // Reutilizar y organizar: el atajo en el menú principal; un grupo nuevo con la web y el mismo atajo dentro; un intercambio de sectores.
        var root = session.Current.RootMenuId;
        Assert.True(shell.Editor.PlaceDefinition(shortcutId, RadialDirection.SouthWest).Success);
        Assert.True(shell.Editor.CreateGroupAt(RadialDirection.NorthWest, "Trabajo").Success);
        shell.Editor.EnterSelectedGroup();
        Assert.True(shell.Editor.PlaceDefinition(webId, RadialDirection.North).Success);
        Assert.True(shell.Editor.PlaceDefinition(shortcutId, RadialDirection.South).Success);
        shell.Editor.OpenMenu(root);
        Assert.True(shell.Editor.MoveSector(RadialDirection.North, RadialDirection.NorthEast).Success); // Copiar ↔ Pegar

        // Guardar.
        Assert.True(session.IsDirty);
        shell.Save.Execute(null);
        Assert.False(session.IsDirty);
        Assert.Equal(SaveState.Saved, shell.SaveState);

        // «Reiniciar»: otra sesión sobre el mismo disco.
        var (restarted, restartedSession) = Open(StorageSamples.Store(directory));
        var configuration = restartedSession.Current;

        Assert.True(ConfigurationValidator.Validate(configuration).IsValid);
        Assert.Contains(configuration.Actions, a => a.Id == shortcutId && a.Action is SendKeysAction { Chord: { Key: "S", Modifiers: KeyModifiers.Windows | KeyModifiers.Shift } });
        Assert.Contains(configuration.Actions, a => a.Id == webId && a.Action is OpenUrlAction { Url: "https://example.com/inicio" });
        Assert.Equal(2, restartedSession.Editor.PlacementsOf(shortcutId).Count); // la misma definición, en dos sitios
        Assert.Single(restartedSession.Editor.PlacementsOf(webId));

        Assert.Equal("Pegar", restarted.Editor.CurrentMenu.GetItem(RadialDirection.North)?.Name); // el intercambio persistió
        Assert.Equal("Copiar", restarted.Editor.CurrentMenu.GetItem(RadialDirection.NorthEast)?.Name);
        var group = restarted.Editor.Tree[0].Children.Single(c => c.Name == "Trabajo");
        restarted.Editor.OpenMenu(group.MenuId);
        Assert.Equal("Mi web", restarted.Editor.CurrentMenu.GetItem(RadialDirection.North)?.Name);
        Assert.Equal("Captura", restarted.Editor.CurrentMenu.GetItem(RadialDirection.South)?.Name);
        Assert.False(restartedSession.IsDirty); // lo recién cargado no tiene cambios pendientes
    }

    [Fact]
    public void ASavedConfiguration_ReloadsWithoutChanges_SoNothingIsWrittenOnTheNextSave()
    {
        using var directory = new StorageTempDirectory();
        var (shell, session) = Open(StorageSamples.Store(directory));
        shell.Editor.PlaceDefinition("edit.cut");
        shell.Save.Execute(null);

        var (_, reloaded) = Open(StorageSamples.Store(directory));
        var result = reloaded.Save();

        Assert.False(result.Changed); // nada que escribir
        Assert.Equal(session.Current.Menus.Count, reloaded.Current.Menus.Count);
        Assert.Equal("Cortar", reloaded.Current.Menus[0].GetItem(RadialDirection.SouthWest)?.Name);
    }
}
