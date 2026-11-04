namespace Radial.Windows.Actions;

/// <summary>
/// Qué destinos de archivos y programas aceptan los ejecutores, independientemente de dónde salga la acción. Es una defensa
/// mínima en el último punto antes de llamar al sistema; NO sustituye a la validación de la configuración (fase 5), que tendrá que
/// decidir además qué tipos de archivo se pueden abrir y qué hacer con las rutas de red.
///
/// Se acepta: una ruta absoluta (<c>C:\Apps\a.exe</c>, <c>\\servidor\recurso\a.exe</c>) y, solo para programas, un nombre sin
/// ruta (<c>notepad.exe</c>) que resuelve el sistema. Se rechaza todo lo demás: rutas relativas o de unidad relativa (dependerían
/// del directorio de trabajo del proceso), rutas de dispositivo o de espacio de nombres del sistema (<c>\\?\</c>, <c>\\.\</c>,
/// <c>\??\</c>, con cualquier mezcla de barras), flujos de datos alternativos (<c>C:\a.txt:flujo</c>) e identificadores con
/// esquema o protocolo (<c>ms-settings:</c>, <c>shell:</c>, <c>file:</c>, <c>http:</c>), que <c>ShellExecute</c> trataría como algo
/// que lanzar.
/// </summary>
internal enum PathVerdict
{
    Absolute,
    Relative,
    Forbidden,
}

internal static class TargetPolicy
{
    /// <summary>
    /// Ruta absoluta de unidad (<c>C:\…</c>) o de red (<c>\\servidor\recurso\…</c>): la única forma admitida para abrir un archivo
    /// o una carpeta. No admite rutas de dispositivo ni «:» fuera de la letra de unidad.
    /// </summary>
    public static bool IsAbsoluteFilePath(string path) => Classify(path) == PathVerdict.Absolute;

    /// <summary>
    /// <see cref="PathVerdict.Forbidden"/>: ruta de dispositivo o con «:» fuera de la letra de unidad (flujo alternativo);
    /// <see cref="PathVerdict.Relative"/>: no es absoluta, así que dependería del directorio de trabajo; el resto, absoluta.
    /// </summary>
    public static PathVerdict Classify(string path)
    {
        if (IsDevicePath(path))
        {
            return PathVerdict.Forbidden;
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return PathVerdict.Relative;
        }

        return HasColonBeyondTheDriveLetter(path) ? PathVerdict.Forbidden : PathVerdict.Absolute;
    }

    /// <summary>Destino de un programa: una ruta absoluta o un nombre de archivo sin ruta ni esquema.</summary>
    public static bool IsProgramTarget(string target) => IsAbsoluteFilePath(target) || IsBareName(target);

    /// <summary>
    /// Ruta de dispositivo o de espacio de nombres del sistema: empieza por dos separadores (cualquiera de las dos barras) y
    /// «.» o «?» y otro separador, o por <c>\??\</c>.
    /// </summary>
    public static bool IsDevicePath(string path)
    {
        if (path.Length >= 4 && IsSeparator(path[0]) && IsSeparator(path[1]) && (path[2] is '.' or '?') && IsSeparator(path[3]))
        {
            return true;
        }

        return path.Length >= 4 && path[0] == '\\' && path[1] == '?' && path[2] == '?' && path[3] == '\\';
    }

    private static bool IsSeparator(char c) => c is '\\' or '/';

    // «C:» solo puede aparecer en la posición 1 (letra de unidad); en cualquier otro sitio sería un flujo alternativo o un protocolo.
    private static bool HasColonBeyondTheDriveLetter(string path)
    {
        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] == ':' && i != 1)
            {
                return true;
            }
        }

        return false;
    }

    // Sin separadores ni «:» (que lo convertirían en una ruta, una unidad o un protocolo) ni nombres que son solo puntos.
    private static bool IsBareName(string target) =>
        target.IndexOfAny(['\\', '/', ':']) < 0 && target.Trim('.').Length > 0;
}
