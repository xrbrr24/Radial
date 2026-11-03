namespace Radial.Core.Model;

public sealed record LaunchApplicationAction(string Path, string? Arguments = null, string? WorkingDirectory = null) : RadialAction
{
    public override IEnumerable<string> GetProblems() =>
        RequiredText(Path, nameof(Path))
            .Concat(OptionalText(Arguments, nameof(Arguments)))
            .Concat(OptionalText(WorkingDirectory, nameof(WorkingDirectory)));
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

        return problems;
    }
}

/// <summary>Convierte el elemento en un grupo que abre otro menú.</summary>
public sealed record OpenMenuAction(MenuId TargetMenuId) : RadialAction
{
    public override IEnumerable<string> GetProblems() =>
        string.IsNullOrWhiteSpace(TargetMenuId.Value) ? [$"{nameof(TargetMenuId)} es obligatorio."] : [];
}
