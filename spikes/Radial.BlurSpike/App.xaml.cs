using System.IO;
using System.Windows;
using Radial.BlurSpike.Harness;

namespace Radial.BlurSpike;

public partial class App : Application
{
    // Uso:
    //   Radial.BlurSpike.exe                       harness interactivo
    //   Radial.BlurSpike.exe --auto [--out DIR]    batería automática y salida
    //   Radial.BlurSpike.exe --receiver X Y W H    ventana receptora (la lanza el harness)
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var args = e.Args;
        if (args.Length >= 5 && args[0] == "--receiver")
        {
            new ReceiverWindow(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4])).Show();
            return;
        }

        var auto = args.Contains("--auto");
        var outIndex = Array.IndexOf(args, "--out");
        var outDir = outIndex >= 0 && outIndex + 1 < args.Length
            ? args[outIndex + 1]
            : Path.Combine(AppContext.BaseDirectory, "results");
        // NO VALIDADO: --only y el log a archivo (run.log) se añadieron tras run1; compilan, nunca se han ejecutado.
        var onlyIndex = Array.IndexOf(args, "--only");
        var only = onlyIndex >= 0 && onlyIndex + 1 < args.Length ? args[onlyIndex + 1].Split(',') : null;
        new ControlWindow(auto, outDir, only).Show();
    }
}
