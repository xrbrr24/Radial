using System.Globalization;
using System.Text.RegularExpressions;

namespace Radial.Core.Diagnostics;

/// <summary>Un archivo de registro existente: su nombre (sin carpeta) y su tamaño.</summary>
public sealed record LogFileInfo(string Name, long Length);

/// <summary>
/// Nombres y retención de los archivos de registro, sin tocar el disco: <c>radial-aaaaMMdd-NNN.log</c> (un archivo por día y, si
/// pasa de cierto tamaño, el siguiente número). Ese nombre ordena cronológicamente por orden alfabético. La retención conserva los
/// <c>maxFiles</c> más recientes y, además, no deja que el total supere <c>maxTotalBytes</c>; el más reciente nunca se borra.
/// Solo se consideran archivos con ese nombre: cualquier otro archivo de la carpeta no se toca.
/// </summary>
public static partial class LogRetention
{
    public const int DefaultMaxFiles = 14;
    public const long DefaultMaxTotalBytes = 8L * 1024 * 1024;
    public const long DefaultMaxFileBytes = 1024 * 1024;

    public static string FileName(DateOnly date, int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        return string.Create(CultureInfo.InvariantCulture, $"radial-{date:yyyyMMdd}-{sequence:D3}.log");
    }

    public static bool IsLogFile(string name) => !string.IsNullOrEmpty(name) && LogFileName().IsMatch(name);

    /// <summary>Los nombres que hay que borrar para cumplir la retención, del más antiguo al más reciente.</summary>
    public static IReadOnlyList<string> SelectToDelete(IEnumerable<LogFileInfo> files, int maxFiles = DefaultMaxFiles, long maxTotalBytes = DefaultMaxTotalBytes)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTotalBytes, 1L);

        var ordered = files.Where(file => IsLogFile(file.Name)).OrderBy(file => file.Name, StringComparer.Ordinal).ToList();
        var delete = new List<string>();
        var kept = ordered.Count;
        var total = ordered.Sum(file => file.Length);

        foreach (var file in ordered)
        {
            if (kept <= 1)
            {
                break; // el más reciente se conserva siempre
            }

            if (kept > maxFiles || total > maxTotalBytes)
            {
                delete.Add(file.Name);
                kept--;
                total -= file.Length;
            }
        }

        return delete;
    }

    [GeneratedRegex(@"^radial-\d{8}-\d{3}\.log$", RegexOptions.CultureInvariant)]
    private static partial Regex LogFileName();
}
