using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Radial.App.Views;
using Radial.Core.Editing;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Windows.Presentation;
using Radial.Windows.Shortcuts;
using Radial.Windows.Storage;

namespace Radial.App.Services;

/// <summary>
/// Herramienta de desarrollo: <c>Radial.App.exe --render-ui &lt;carpeta&gt;</c> dibuja el centro de control (editor, biblioteca, ajustes,
/// diálogo) en archivos PNG con la configuración predeterminada EN MEMORIA, sin ventana visible, sin hooks, sin entrada y sin tocar
/// el disco. Sirve para ver el diseño sin lanzar la aplicación. No prueba interacción (arrastrar, foco, teclado): eso es manual.
/// </summary>
internal static class UiPreviewRenderer
{
    public static void Render(string folder)
    {
        Directory.CreateDirectory(folder);
        var dialogs = new InAppDialogs();
        var services = new UiServices(dialogs, new NoFilePicker(), dialogs, new Win32KeyLabelProvider()) { Factory = () => FactoryLibrary.Create() };
        var configuration = DefaultConfiguration.Create();
        var store = new InMemorySettingsStore(configuration);
        var session = new ConfigurationSession(store, store.LoadOrCreate());
        var shell = new ShellViewModel(session, services, dialogs, new object());

        // 1. Editor sin selección, 2. un sector con acción, 3. un sector vacío, 4. dentro de un grupo.
        Save(shell, folder, "01-editor", 1320, 820);

        shell.Editor.SelectDirection(shell.Editor.Sectors.First(s => !s.IsEmpty && !s.IsGroup).Direction);
        Save(shell, folder, "02-editor-accion", 1320, 820);

        var free = shell.Editor.Sectors.FirstOrDefault(s => s.IsEmpty);
        if (free is not null)
        {
            shell.Editor.SelectDirection(free.Direction);
            Save(shell, folder, "03-editor-vacio", 1320, 820);
        }

        var group = shell.Editor.Sectors.FirstOrDefault(s => s.IsGroup);
        if (group is not null)
        {
            shell.Editor.SelectDirection(group.Direction);
            Save(shell, folder, "04-editor-grupo", 1320, 820);
            shell.Editor.EnterSelectedGroup();
            shell.Editor.SelectDirection(shell.Editor.Sectors.First(s => !s.IsEmpty).Direction);
            Save(shell, folder, "05-editor-dentro-del-grupo", 1320, 820);
            shell.Editor.OpenMenu(session.Current.RootMenuId);
        }

        Save(shell, folder, "06-editor-estrecho", 960, 700);

        // Biblioteca: lista, búsqueda y formulario de atajo.
        shell.Go(AppSection.Library);
        Save(shell, folder, "10-biblioteca", 1320, 820);
        shell.Library.SearchText = "ctrl s";
        Save(shell, folder, "11-biblioteca-busqueda", 1320, 820);
        shell.Library.SearchText = string.Empty;
        shell.Library.SelectById("edit.copy");
        Save(shell, folder, "12-biblioteca-detalle", 1320, 820);
        shell.Library.New.Execute(ActionKind.Shortcut);
        if (shell.Library.Form is { } form)
        {
            form.Name = "Captura de pantalla";
            form.Key.Windows = true;
            form.Key.Shift = true;
            form.Key.KeyName = "S";
        }

        Save(shell, folder, "13-biblioteca-formulario-atajo", 1320, 820);

        shell.Library.CancelForm.Execute(null);
        dialogs.CurrentDialog?.Complete(1); // confirma el descarte del formulario
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        shell.Library.New.Execute(ActionKind.Application);
        Save(shell, folder, "14-biblioteca-formulario-aplicacion", 1320, 820);

        // Ajustes y un diálogo de confirmación.
        shell.Go(AppSection.Settings);
        Save(shell, folder, "20-ajustes", 1320, 820);

        shell.Go(AppSection.Editor);
        session.Editor.SetNavigation(session.Current.Navigation with { MaxDepth = 4 });
        _ = dialogs.ConfirmAsync("Eliminar un grupo con contenido", "Contiene 3 elemento(s). Se eliminará el grupo «Abrir» y todo lo que contiene de los menús. Las acciones seguirán en la biblioteca.", "Eliminar el grupo", danger: true);
        dialogs.Show("Configuración guardada. El radial ya usa los cambios.");
        Save(shell, folder, "30-dialogo", 1320, 820);
    }

    private static void Save(ShellViewModel shell, string folder, string name, int width, int height)
    {
        var view = new ShellView { DataContext = shell, Width = width, Height = height };
        var size = new Size(width, height);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.UpdateLayout();

        // Deja que se ejecuten los enlaces y las plantillas pendientes antes de dibujar.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
    }

    private sealed class NoFilePicker : IFilePicker
    {
        public string? PickApplication() => null;

        public string? PickFile() => null;

        public string? PickFolder() => null;
    }
}
