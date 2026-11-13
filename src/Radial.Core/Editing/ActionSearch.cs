using System.Globalization;
using System.Text;
using Radial.Core.Catalog;
using Radial.Core.Model;

namespace Radial.Core.Editing;

/// <summary>Tipo de acción tal como lo ve el usuario (no el nombre de la clase).</summary>
public enum ActionKind
{
    Shortcut,
    Application,
    File,
    Folder,
    Url,
    Text,
    Volume,
    Window,

    /// <summary>De un tipo que esta versión no conoce: se conserva pero no se ejecuta.</summary>
    Unsupported,
}

public static class ActionKinds
{
    public static ActionKind Of(RadialAction action) => action switch
    {
        SendKeysAction => ActionKind.Shortcut,
        LaunchApplicationAction => ActionKind.Application,
        OpenFileAction => ActionKind.File,
        OpenFolderAction => ActionKind.Folder,
        OpenUrlAction => ActionKind.Url,
        TypeTextAction => ActionKind.Text,
        VolumeAction => ActionKind.Volume,
        WindowControlAction => ActionKind.Window,
        _ => ActionKind.Unsupported,
    };
}

/// <param name="Category">Solo esta categoría.</param>
/// <param name="Kind">Solo este tipo de acción.</param>
/// <param name="BuiltIn"><c>true</c> = solo las incluidas de fábrica; <c>false</c> = solo las personalizadas.</param>
/// <param name="Placed"><c>true</c> = solo las colocadas en algún menú; <c>false</c> = solo las que no.</param>
public sealed record ActionFilter(ActionCategory? Category = null, ActionKind? Kind = null, bool? BuiltIn = null, bool? Placed = null);

/// <summary>
/// Búsqueda y filtrado de la biblioteca. Pura, síncrona y rápida (miles de acciones en milisegundos), así que se puede ejecutar en
/// cada pulsación sin bloquear la interfaz. Busca por nombre, descripción, categoría, etiquetas, destino (ruta, programa, dirección)
/// y combinación de teclas; sin distinguir mayúsculas ni tildes. Cada palabra de la consulta debe aparecer en algún sitio. La
/// combinación se encuentra escrita como «ctrl+s», «ctrl s», «ctrl mayús s» o «ctrls». El orden pone primero las coincidencias de
/// nombre y de combinación exacta.
/// </summary>
public static class ActionSearch
{
    private static readonly char[] TokenSeparators = [' ', '+', '\t', '-'];

    /// <summary>Minúsculas y sin tildes ni signos diacríticos (Ñ → n), para comparar. Cultura invariante.</summary>
    public static string Normalize(string? text)
    {
        var decomposed = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    /// <param name="keyLabel">Cómo se llama una tecla en la distribución activa (para encontrar «Ñ» por lo que se ve en el teclado).</param>
    /// <param name="categoryName">Nombre que se muestra de una categoría (para buscar por «Edición»).</param>
    /// <param name="placedIds">Identificadores de las definiciones colocadas en algún menú (para el filtro).</param>
    public static IReadOnlyList<ActionDefinition> Query(
        IEnumerable<ActionDefinition> library,
        string? text,
        ActionFilter? filter = null,
        Func<string, string?>? keyLabel = null,
        Func<ActionCategory, string>? categoryName = null,
        ISet<string>? placedIds = null)
    {
        ArgumentNullException.ThrowIfNull(library);

        var candidates = library.Where(d => d is not null && Passes(d, filter, placedIds)).ToList();
        var query = Normalize(text).Trim();
        if (query.Length == 0)
        {
            var order = ActionCategory.Standard.Select((c, i) => (c.Id, i)).ToDictionary(p => p.Id, p => p.i);
            return candidates
                .OrderBy(d => order.TryGetValue(d.Category.Id ?? string.Empty, out var i) ? i : int.MaxValue)
                .ThenBy(d => Normalize(d.Name), StringComparer.Ordinal)
                .ToList();
        }

        var tokens = query.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
        var compact = new string(query.Where(char.IsLetterOrDigit).ToArray());
        var scored = new List<(ActionDefinition Definition, int Score)>();

        foreach (var definition in candidates)
        {
            var name = Normalize(definition.Name);
            var rest = Normalize(string.Join(' ', definition.Description, definition.Category.Id, categoryName?.Invoke(definition.Category), string.Join(' ', definition.TagList), Target(definition.Action)));
            var chords = definition.Action is SendKeysAction keys ? ChordForms(keys.Chord, keyLabel) : [];

            var matchesAll = tokens.All(t => name.Contains(t, StringComparison.Ordinal) || rest.Contains(t, StringComparison.Ordinal) || chords.Any(c => c.Contains(t, StringComparison.Ordinal)));
            if (!matchesAll && !chords.Any(c => compact.Length > 0 && c.Contains(compact, StringComparison.Ordinal)))
            {
                continue;
            }

            var score = 10;
            if (name == query)
            {
                score = 100;
            }
            else if (chords.Any(c => c == compact))
            {
                score = 90;
            }
            else if (name.StartsWith(query, StringComparison.Ordinal))
            {
                score = 80;
            }
            else if (name.Contains(query, StringComparison.Ordinal) || tokens.All(t => name.Contains(t, StringComparison.Ordinal)))
            {
                score = 60;
            }
            else if (definition.TagList.Any(tag => Normalize(tag).Contains(query, StringComparison.Ordinal)))
            {
                score = 40;
            }
            else if (rest.Contains(query, StringComparison.Ordinal))
            {
                score = 20;
            }

            scored.Add((definition, score));
        }

        return scored.OrderByDescending(s => s.Score).ThenBy(s => Normalize(s.Definition.Name), StringComparer.Ordinal).Select(s => s.Definition).ToList();
    }

    private static bool Passes(ActionDefinition definition, ActionFilter? filter, ISet<string>? placedIds)
    {
        if (filter is null)
        {
            return true;
        }

        return (filter.Category is null || definition.Category == filter.Category)
            && (filter.Kind is null || ActionKinds.Of(definition.Action) == filter.Kind)
            && (filter.BuiltIn is null || definition.IsBuiltIn == filter.BuiltIn)
            && (filter.Placed is null || (placedIds is not null && placedIds.Contains(definition.Id)) == filter.Placed);
    }

    // Las formas de la combinación para buscarla: normalizadas y sin separadores («ctrlshifts», «ctrlmayuss»...).
    private static List<string> ChordForms(KeyChord chord, Func<string, string?>? keyLabel) =>
        KeyChordText.SearchForms(chord, keyLabel).Select(Normalize).Select(s => new string(s.Where(char.IsLetterOrDigit).ToArray())).Distinct().ToList();

    private static string Target(RadialAction action) => action switch
    {
        LaunchApplicationAction launch => string.Join(' ', new[] { launch.Path }.Concat(launch.Arguments ?? [])),
        OpenFileAction file => file.Path,
        OpenFolderAction folder => folder.Path,
        OpenUrlAction url => url.Url,
        TypeTextAction text => text.Text,
        _ => string.Empty,
    };
}
