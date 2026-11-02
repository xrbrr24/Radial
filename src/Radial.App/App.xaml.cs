using System.Windows;

namespace Radial.App;

public partial class App : Application
{
    // Uso:
    //   Radial.App.exe                        ventana de diagnóstico
    //   Radial.App.exe --selftest <archivo>   inicia la captura, ejecuta la autoprueba con entrada sintética,
    //                                         escribe el resultado en <archivo> y cierra (sin pulsar ningún botón)
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var index = Array.IndexOf(e.Args, "--selftest");
        var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null;
        new MainWindow(output).Show();
    }
}
