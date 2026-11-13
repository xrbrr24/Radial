namespace Radial.Core.Model;

/// <summary>
/// Inicia una aplicación. <see cref="Arguments"/> es la LISTA de argumentos que recibe el programa (uno por elemento, tal cual, sin
/// comillas que escribir ni intérprete de por medio); <see cref="WorkingDirectory"/> es la carpeta desde la que arranca. Dos acciones
/// son iguales si lo son sus campos y sus argumentos elemento a elemento (<c>null</c> y lista vacía cuentan como «sin argumentos»).
/// </summary>
public sealed record LaunchApplicationAction(string Path, IReadOnlyList<string>? Arguments = null, string? WorkingDirectory = null) : RadialAction
{
    /// <summary>Cuántos argumentos como máximo.</summary>
    public const int MaxArguments = 64;

    /// <summary>Longitud máxima de cada argumento.</summary>
    public const int MaxArgumentLength = 8_192;

    /// <summary>Longitud total máxima de los argumentos (la línea de comandos de Windows admite 32 767 caracteres).</summary>
    public const int MaxTotalArgumentLength = 30_000;

    public override IEnumerable<string> GetProblems()
    {
        var problems = RequiredText(Path, nameof(Path)).Concat(OptionalText(WorkingDirectory, nameof(WorkingDirectory))).ToList();
        if (Arguments is null)
        {
            return problems;
        }

        if (Arguments.Count > MaxArguments)
        {
            problems.Add($"{nameof(Arguments)} admite como máximo {MaxArguments} argumentos.");
        }

        long total = 0;
        foreach (var argument in Arguments)
        {
            if (argument is null)
            {
                problems.Add($"{nameof(Arguments)} no puede contener elementos nulos.");
                continue;
            }

            total += argument.Length;
            if (argument.Length > MaxArgumentLength)
            {
                problems.Add($"Un argumento supera los {MaxArgumentLength} caracteres.");
            }

            if (argument.Contains('\0'))
            {
                problems.Add("Un argumento contiene caracteres nulos.");
            }
        }

        if (total > MaxTotalArgumentLength)
        {
            problems.Add($"Los argumentos suman más de {MaxTotalArgumentLength} caracteres.");
        }

        return problems.Distinct();
    }

    public bool Equals(LaunchApplicationAction? other) =>
        other is not null
        && string.Equals(Path, other.Path, StringComparison.Ordinal)
        && string.Equals(WorkingDirectory, other.WorkingDirectory, StringComparison.Ordinal)
        && (Arguments ?? []).SequenceEqual(other.Arguments ?? [], StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Path, StringComparer.Ordinal);
        hash.Add(WorkingDirectory, StringComparer.Ordinal);
        foreach (var argument in Arguments ?? [])
        {
            hash.Add(argument, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

public sealed record OpenFileAction(string Path) : RadialAction
{
    public override IEnumerable<string> GetProblems() => RequiredText(Path, nameof(Path));
}

public sealed record OpenFolderAction(string Path) : RadialAction
{
    public override IEnumerable<string> GetProblems() => RequiredText(Path, nameof(Path));
}

public sealed record OpenUrlAction(string Url) : RadialAction
{
    /// <summary>
    /// Esquemas que se pueden abrir: <c>http</c> y <c>https</c>. ÚNICA definición: la usa la validación de aquí y la comprobación
    /// defensiva que repite el ejecutor de Radial.Windows justo antes de lanzar. <c>mailto:</c>, <c>javascript:</c>, <c>file:</c>,
    /// <c>data:</c> y cualquier otro esquema se rechazan. Ampliarla es una decisión de diseño.
    /// </summary>
    public static IReadOnlyList<string> AllowedSchemes { get; } = Array.AsReadOnly([Uri.UriSchemeHttp, Uri.UriSchemeHttps]);

    public override IEnumerable<string> GetProblems()
    {
        var problems = RequiredText(Url, nameof(Url)).ToList();
        if (problems.Count > 0)
        {
            return problems;
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri))
        {
            return [$"{nameof(Url)} no es una URL absoluta válida."];
        }

        return AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
            ? []
            : [$"{nameof(Url)} usa el esquema '{uri.Scheme}'; solo se permiten {string.Join(", ", AllowedSchemes)}."];
    }
}

public sealed record SendKeysAction(KeyChord Chord) : RadialAction
{
    public override IEnumerable<string> GetProblems()
    {
        if (Chord is null)
        {
            return [$"{nameof(Chord)} es obligatorio."];
        }

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Chord.Key))
        {
            problems.Add("La tecla de la combinación es obligatoria.");
        }
        else if (!KeyVocabulary.TryGetCanonicalName(Chord.Key, out _))
        {
            // No se repite el texto recibido: puede ser larguísimo y la ruta del problema ya localiza el elemento.
            problems.Add($"La tecla de la combinación no está admitida. Teclas admitidas: {KeyVocabulary.Description}.");
        }

        const KeyModifiers all = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Windows;
        if ((Chord.Modifiers & ~all) != 0)
        {
            problems.Add("La combinación contiene modificadores desconocidos.");
        }

        if (ShortcutRules.GetProblem(Chord) is { } reserved)
        {
            problems.Add(reserved);
        }

        return problems;
    }
}

/// <summary>Convierte el elemento en un grupo que abre otro menú.</summary>
public sealed record OpenMenuAction(MenuId TargetMenuId) : RadialAction
{
    public override IEnumerable<string> GetProblems() =>
        string.IsNullOrWhiteSpace(TargetMenuId.Value) ? [$"{nameof(TargetMenuId)} es obligatorio."] : [];
}

/// <summary>
/// Escribe un texto en la aplicación que tiene el foco, carácter a carácter como entrada Unicode (no usa el portapapeles). Los
/// saltos de línea se envían como Intro y los tabuladores como Tab; no se admiten otros caracteres de control.
/// </summary>
public sealed record TypeTextAction(string Text) : RadialAction
{
    /// <summary>Caracteres como máximo. Una sola llamada al sistema envía todo el texto: se mantiene acotado.</summary>
    public const int MaxLength = 1_000;

    public override IEnumerable<string> GetProblems()
    {
        if (string.IsNullOrEmpty(Text))
        {
            return [$"{nameof(Text)} es obligatorio."];
        }

        var problems = new List<string>();
        if (Text.Length > MaxLength)
        {
            problems.Add($"{nameof(Text)} supera los {MaxLength} caracteres.");
        }

        for (var index = 0; index < Text.Length; index++)
        {
            var c = Text[index];
            if (c is '\n' or '\r' or '\t')
            {
                continue;
            }

            if (char.IsControl(c))
            {
                problems.Add($"{nameof(Text)} contiene caracteres de control no admitidos (solo se permiten saltos de línea y tabuladores).");
                break;
            }

            if (char.IsHighSurrogate(c) && index + 1 < Text.Length && char.IsLowSurrogate(Text[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(c))
            {
                problems.Add($"{nameof(Text)} contiene un carácter Unicode incompleto.");
                break;
            }
        }

        return problems;
    }
}

public enum VolumeOperation
{
    /// <summary>Sube el volumen <see cref="VolumeAction.Percent"/> puntos (y quita el silencio si estaba activo).</summary>
    Increase,

    /// <summary>Baja el volumen <see cref="VolumeAction.Percent"/> puntos.</summary>
    Decrease,

    /// <summary>Fija el volumen en <see cref="VolumeAction.Percent"/> % (y quita el silencio si el nivel es mayor que 0).</summary>
    SetLevel,

    /// <summary>Activa o quita el silencio del dispositivo de salida.</summary>
    ToggleMute,
}

/// <summary>
/// Controla el volumen del dispositivo de salida predeterminado de Windows con Core Audio (no simula teclas multimedia).
/// <see cref="Percent"/> es la diferencia (1–100) para subir y bajar, y el nivel (0–100) para fijarlo; no se usa al alternar el silencio.
/// </summary>
public sealed record VolumeAction(VolumeOperation Operation, int Percent = 0) : RadialAction
{
    public override IEnumerable<string> GetProblems()
    {
        if (!Enum.IsDefined(Operation))
        {
            return [$"{nameof(Operation)} no es una operación de volumen conocida."];
        }

        return Operation switch
        {
            VolumeOperation.Increase or VolumeOperation.Decrease when Percent is < 1 or > 100 =>
                [$"La diferencia de volumen debe estar entre 1 y 100 (es {Percent})."],
            VolumeOperation.SetLevel when Percent is < 0 or > 100 =>
                [$"El nivel de volumen debe estar entre 0 y 100 (es {Percent})."],
            _ => [],
        };
    }
}

public enum WindowOperation
{
    Minimize,
    Maximize,
    Restore,
    Close,
}

/// <summary>
/// Actúa sobre la ventana EXTERNA que tiene el foco al confirmar: minimizarla, maximizarla, restaurarla o cerrarla. Nunca sobre las
/// ventanas de Radial (centro de control, overlay) ni sobre el escritorio y la barra de tareas.
/// </summary>
public sealed record WindowControlAction(WindowOperation Operation) : RadialAction
{
    public override IEnumerable<string> GetProblems() =>
        Enum.IsDefined(Operation) ? [] : [$"{nameof(Operation)} no es una operación de ventana conocida."];
}
