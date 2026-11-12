namespace Radial.Core.Model;

/// <summary>Cómo clasifica <see cref="ActionTargets"/> una ruta.</summary>
public enum PathVerdict
{
    /// <summary>Ruta absoluta de unidad (<c>C:\…</c>) o de red (<c>\\servidor\recurso\…</c>).</summary>
    Absolute,

    /// <summary>No es absoluta: dependería del directorio de trabajo del proceso.</summary>
    Relative,

    /// <summary>Ruta de dispositivo o de espacio de nombres del sistema, o con «:» fuera de la letra de unidad.</summary>
    Forbidden,
}

/// <summary>
/// ÚNICA definición de qué destinos de archivos y programas admite Radial, sea cual sea su origen (el editor, <c>config.json</c> al
/// cargarse o el ejecutor justo antes de lanzar). Es solo sintaxis de rutas de Windows: no toca el disco ni el sistema.
///
/// Se acepta: una ruta absoluta (<c>C:\Apps\a.exe</c>, <c>\\servidor\recurso\a.exe</c>) y, solo para programas, un nombre sin ruta
/// (<c>notepad.exe</c>) que resuelve el sistema. Se rechaza todo lo demás: rutas relativas o de unidad relativa (dependerían del
/// directorio de trabajo del proceso), rutas de dispositivo o de espacio de nombres del sistema (<c>\\?\</c>, <c>\\.\</c>,
/// <c>\??\</c>, con cualquier mezcla de barras), flujos de datos alternativos (<c>C:\a.txt:flujo</c>) e identificadores con esquema
/// o protocolo (<c>ms-settings:</c>, <c>shell:</c>, <c>file:</c>, <c>http:</c>), que <c>ShellExecute</c> trataría como algo que lanzar.
/// Las direcciones web las valida <see cref="OpenUrlAction.AllowedSchemes"/>. Qué tipos de archivo se pueden abrir y qué hacer con las
/// rutas de red sigue siendo una decisión pendiente (documento técnico, 17.12).
/// </summary>
public static class ActionTargets
{
    /// <summary>
    /// Ruta absoluta de unidad (<c>C:\…</c>) o de red (<c>\\servidor\recurso\…</c>): la única forma admitida para abrir un archivo
    /// o una carpeta. No admite rutas de dispositivo ni «:» fuera de la letra de unidad.
    /// </summary>
    public static bool IsAbsoluteFilePath(string path) => Classify(path) == PathVerdict.Absolute;

    /// <summary>Destino de un programa: una ruta absoluta o un nombre de archivo sin ruta ni esquema.</summary>
    public static bool IsProgramTarget(string target) => IsAbsoluteFilePath(target) || IsBareName(target);

    /// <summary>
    /// <see cref="PathVerdict.Forbidden"/>: ruta de dispositivo o con «:» fuera de la letra de unidad (flujo alternativo);
    /// <see cref="PathVerdict.Relative"/>: no es absoluta, así que dependería del directorio de trabajo; el resto, absoluta.
    /// </summary>
    public static PathVerdict Classify(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

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

    /// <summary>
    /// Ruta de dispositivo o de espacio de nombres del sistema: empieza por dos separadores (cualquiera de las dos barras) y
    /// «.» o «?» y otro separador, o por <c>\??\</c>.
    /// </summary>
    public static bool IsDevicePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length >= 4 && IsSeparator(path[0]) && IsSeparator(path[1]) && (path[2] is '.' or '?') && IsSeparator(path[3]))
        {
            return true;
        }

        return path.Length >= 4 && path[0] == '\\' && path[1] == '?' && path[2] == '?' && path[3] == '\\';
    }

    /// <summary>
    /// Por qué el destino de <paramref name="action"/> no se puede abrir nunca (en palabras del usuario), o vacío si está bien o si la
    /// acción no abre archivos ni programas. NO repite lo que ya dice <see cref="RadialAction.GetProblems"/>: un texto vacío es
    /// asunto suyo (aquí no se informa). Es lo que el ejecutor rechazaría siempre con <c>TargetNotAllowed</c> o <c>TargetNotFound</c>.
    /// </summary>
    public static IReadOnlyList<string> GetProblems(RadialAction? action)
    {
        var problems = new List<string>();
        switch (action)
        {
            case LaunchApplicationAction launch:
                if (!string.IsNullOrWhiteSpace(launch.Path) && !IsProgramTarget(launch.Path))
                {
                    problems.Add(
                        "El programa debe ser una ruta completa (por ejemplo C:\\Programas\\app.exe) o un nombre de programa como notepad.exe: " +
                        "no se admiten rutas relativas, de dispositivo ni protocolos como ms-settings:.");
                }

                if (!string.IsNullOrWhiteSpace(launch.WorkingDirectory) && !IsAbsoluteFilePath(launch.WorkingDirectory))
                {
                    problems.Add("La carpeta de trabajo debe ser una ruta completa (por ejemplo C:\\Datos).");
                }

                break;

            case OpenFileAction file:
                AddPathProblem(problems, file.Path, "del archivo");
                break;

            case OpenFolderAction folder:
                AddPathProblem(problems, folder.Path, "de la carpeta");
                break;
        }

        return problems;
    }

    private static void AddPathProblem(List<string> problems, string path, string ofNoun)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        switch (Classify(path))
        {
            case PathVerdict.Relative:
                problems.Add($"La ruta {ofNoun} debe ser completa (por ejemplo C:\\Datos\\…): una ruta relativa depende del programa que la abre.");
                break;
            case PathVerdict.Forbidden:
                problems.Add($"La ruta {ofNoun} no es válida: no se admiten rutas de dispositivo (\\\\?\\, \\\\.\\) ni «:» fuera de la letra de unidad.");
                break;
        }
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
