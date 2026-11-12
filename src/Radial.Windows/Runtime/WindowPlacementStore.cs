using System.Text;
using System.Text.Json;
using Radial.Core.Diagnostics;

namespace Radial.Windows.Runtime;

/// <summary>
/// Estado visual local de la ventana principal. No forma parte de config.json deliberadamente: la posición, tamaño y maximizado son
/// preferencias de este equipo y no deben viajar al importar o exportar una biblioteca radial.
/// </summary>
public sealed record WindowPlacementState(double Left, double Top, double Width, double Height, bool IsMaximized)
{
    public const double MinDimension = 200;
    public const double MaxDimension = 20_000;

    public bool IsSane =>
        double.IsFinite(Left) && double.IsFinite(Top) &&
        double.IsFinite(Width) && double.IsFinite(Height) &&
        Width is >= MinDimension and <= MaxDimension &&
        Height is >= MinDimension and <= MaxDimension;
}

public interface IWindowPlacementStore
{
    WindowPlacementState? Load();

    void Save(WindowPlacementState placement);
}

/// <summary>Almacén vacío para vistas de desarrollo o pruebas que no deben tocar el disco.</summary>
public sealed class NullWindowPlacementStore : IWindowPlacementStore
{
    public static NullWindowPlacementStore Instance { get; } = new();

    public WindowPlacementState? Load() => null;

    public void Save(WindowPlacementState placement)
    {
    }
}

/// <summary>
/// Guarda el estado visual en <c>%LOCALAPPDATA%\Radial\window.json</c>. Es no crítico: lectura, JSON o escritura fallidos se
/// registran y se ignoran; una ventana funcional con su tamaño predeterminado es preferible a impedir iniciar Radial. La escritura
/// usa un temporal junto al destino y nunca toca config.json.
/// </summary>
public sealed class FileWindowPlacementStore : IWindowPlacementStore
{
    private const string FileName = "window.json";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _gate = new();
    private readonly IRadialLog _log;

    public FileWindowPlacementStore(string rootDirectory, IRadialLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = System.IO.Path.GetFullPath(rootDirectory);
        _log = log ?? NullRadialLog.Instance;
    }

    public string RootDirectory { get; }

    public string Path => System.IO.Path.Combine(RootDirectory, FileName);

    public WindowPlacementState? Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(Path))
                {
                    return null;
                }

                var placement = JsonSerializer.Deserialize<WindowPlacementState>(File.ReadAllText(Path, Utf8NoBom));
                if (placement is not { IsSane: true })
                {
                    _log.Warn(nameof(FileWindowPlacementStore), "El estado local de la ventana no es válido y se ignora.", operation: "cargar ventana");
                    return null;
                }

                return placement;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                _log.Warn(nameof(FileWindowPlacementStore), "No se pudo leer el estado local de la ventana; se usan los valores predeterminados.", exception, "cargar ventana");
                return null;
            }
        }
    }

    public void Save(WindowPlacementState placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!placement.IsSane)
        {
            _log.Warn(nameof(FileWindowPlacementStore), "Se ignoró un estado local de ventana no válido.", operation: "guardar ventana");
            return;
        }

        lock (_gate)
        {
            string? temporary = null;
            try
            {
                Directory.CreateDirectory(RootDirectory);
                temporary = Path + ".tmp";
                var json = JsonSerializer.Serialize(placement);
                File.WriteAllText(temporary, json + Environment.NewLine, Utf8NoBom);
                File.Move(temporary, Path, overwrite: true);
                _log.Info(nameof(FileWindowPlacementStore), "El estado local de la ventana se guardó.", "guardar ventana");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _log.Warn(nameof(FileWindowPlacementStore), "No se pudo guardar el estado local de la ventana.", exception, "guardar ventana");
            }
            finally
            {
                if (temporary is not null)
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        _log.Warn(nameof(FileWindowPlacementStore), "No se pudo limpiar un temporal del estado de ventana.", exception, "guardar ventana");
                    }
                }
            }
        }
    }
}
