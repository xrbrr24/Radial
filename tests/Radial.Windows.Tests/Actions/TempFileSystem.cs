namespace Radial.Windows.Tests.Actions;

/// <summary>
/// Una carpeta temporal propia con un archivo, un "script" y una subcarpeta, para las comprobaciones de existencia de los
/// ejecutores de archivo y carpeta. Solo se crean datos inertes en el directorio temporal; nada se abre ni se ejecuta, y se borra
/// todo al terminar.
/// </summary>
public sealed class TempFileSystem : IDisposable
{
    public TempFileSystem()
    {
        Root = Path.Combine(Path.GetTempPath(), "radial-tests-" + Guid.NewGuid().ToString("N"));
        ExistingFolder = Path.Combine(Root, "carpeta");
        ExistingFile = Path.Combine(Root, "documento.txt");
        ExistingScript = Path.Combine(Root, "guion.bat");
        MissingPath = Path.Combine(Root, "no-existe");

        Directory.CreateDirectory(ExistingFolder);
        File.WriteAllText(ExistingFile, "x");
        File.WriteAllText(ExistingScript, "rem inerte: nunca se ejecuta");
    }

    public string Root { get; }

    public string ExistingFolder { get; }

    public string ExistingFile { get; }

    /// <summary>Un archivo .bat inerte, para comprobar que la extensión no se interpreta como un comando.</summary>
    public string ExistingScript { get; }

    /// <summary>Una ruta bien formada que no existe ni como archivo ni como carpeta.</summary>
    public string MissingPath { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Limpieza de mejor esfuerzo: es una carpeta temporal.
        }
    }
}
