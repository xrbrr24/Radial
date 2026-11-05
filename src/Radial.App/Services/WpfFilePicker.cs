using System.Windows;
using Microsoft.Win32;
using Radial.Windows.Presentation;

namespace Radial.App.Services;

/// <summary>
/// Selectores de archivos y carpetas del sistema (cuadros estándar de Windows, sin Windows Forms). Solo eligen una ruta: no abren ni
/// ejecutan nada. Devuelven <c>null</c> si el usuario cancela.
/// </summary>
internal sealed class WpfFilePicker : IFilePicker
{
    public string? PickApplication() => PickFileWith("Elige el programa", "Programas (*.exe)|*.exe|Todos los archivos (*.*)|*.*");

    public string? PickFile() => PickFileWith("Elige el archivo", "Todos los archivos (*.*)|*.*");

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Elige la carpeta", Multiselect = false };
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FolderName : null;
    }

    private static string? PickFileWith(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }
}
