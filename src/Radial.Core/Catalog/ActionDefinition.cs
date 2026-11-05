using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Validation;

namespace Radial.Core.Catalog;

/// <summary>
/// Una acción de la biblioteca: lo que la identifica y la describe (<see cref="Id"/> estable, nombre, descripción, categoría,
/// etiquetas e icono) más lo que hace (<see cref="Action"/>: su tipo y sus parámetros). El tipo decide el ejecutor: distintas
/// definiciones pueden compartir uno (Ctrl+C y Ctrl+V son dos <see cref="SendKeysAction"/> y las ejecuta el mismo ejecutor de
/// atajos), así que ampliar la biblioteca es añadir datos, no clases.
///
/// La biblioteca (<see cref="RadialConfiguration.Actions"/>) es la ÚNICA fuente de verdad de lo que hace una acción: se guarda
/// aunque ninguna colocación la use, y las colocaciones (<see cref="RadialItem"/>) solo la referencian por
/// <see cref="Id"/>. Es solo datos y no ejecuta nada.
/// </summary>
/// <param name="Id">Identificador estable (minúsculas, dígitos, «.», «-»; p. ej. <c>edit.copy</c>). No depende del nombre visible.</param>
/// <param name="Description">Texto libre; puede estar vacío.</param>
/// <param name="Tags">Etiquetas libres para buscar y filtrar (como mucho <see cref="ConfigurationLimits.MaxTags"/>); <c>null</c> = ninguna.</param>
/// <param name="IsBuiltIn">Viene de fábrica (la biblioteca inicial) en lugar de haberla creado el usuario. Solo informativo: se puede editar y borrar igual.</param>
public sealed record ActionDefinition(
    string Id,
    string Name,
    ActionCategory Category,
    string Description,
    IconReference Icon,
    RadialAction Action,
    IReadOnlyList<string>? Tags = null,
    bool IsBuiltIn = false)
{
    /// <summary>Las etiquetas, nunca nulas.</summary>
    public IReadOnlyList<string> TagList => Tags ?? [];

    /// <summary>Problemas de la definición; vacío si es correcta. Incluye los de la propia acción. Una acción desconocida es un problema.</summary>
    public IEnumerable<string> GetProblems() => GetProblems(allowUnknownAction: false);

    /// <summary>
    /// Igual que <see cref="GetProblems()"/>, pero con <paramref name="allowUnknownAction"/> una acción de tipo desconocido no es un
    /// problema (se conserva al leer una configuración; nunca se acepta al crear una definición nueva).
    /// </summary>
    public IEnumerable<string> GetProblems(bool allowUnknownAction)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Id))
        {
            problems.Add("El identificador es obligatorio.");
        }
        else if (!IsValidId(Id))
        {
            problems.Add($"El identificador debe tener como máximo {ConfigurationLimits.MaxIdLength} caracteres: minúsculas, dígitos, «.» y «-».");
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Length > ConfigurationLimits.MaxNameLength)
        {
            problems.Add($"El nombre es obligatorio y tiene como máximo {ConfigurationLimits.MaxNameLength} caracteres.");
        }

        if (!Category.IsValid)
        {
            problems.Add("La categoría no es válida: minúsculas, dígitos y «-», hasta " + ActionCategory.MaxIdLength + " caracteres.");
        }

        if (Description is null || Description.Length > ConfigurationLimits.MaxDescriptionLength)
        {
            problems.Add($"La descripción tiene como máximo {ConfigurationLimits.MaxDescriptionLength} caracteres.");
        }

        problems.AddRange(TagProblems());

        if (Icon is null || !Enum.IsDefined(Icon.Source) || string.IsNullOrWhiteSpace(Icon.Value) || Icon.Value.Length > ConfigurationLimits.MaxIconValueLength)
        {
            problems.Add("El icono necesita un origen válido y un valor.");
        }

        if (Action is null)
        {
            problems.Add("La acción es obligatoria.");
        }
        else if (Action is OpenMenuAction)
        {
            problems.Add("Un grupo de menú no es una acción de la biblioteca: se define en el menú, no aquí.");
        }
        else if (Action is UnknownAction)
        {
            if (!allowUnknownAction)
            {
                problems.Add("Una acción de tipo desconocido no se puede ejecutar y no puede ser una definición de la biblioteca.");
            }
        }
        else
        {
            problems.AddRange(Action.GetProblems());
        }

        return problems;
    }

    /// <summary>Una definición con este identificador sigue las reglas de los identificadores de acción.</summary>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= ConfigurationLimits.MaxIdLength && id.All(IsIdCharacter);

    /// <summary>
    /// Una COLOCACIÓN de esta acción en un menú: el elemento lleva su propio identificador <paramref name="itemId"/> (único en toda
    /// la configuración) y recuerda de qué definición salió (<see cref="RadialItem.DefinitionId"/>). La misma definición se puede colocar
    /// en tantos menús y posiciones como se quiera, cada vez con un identificador de elemento distinto.
    /// </summary>
    public RadialItem ToItem(ItemId itemId, RadialDirection position) => new(itemId, Name, position, Action, Icon, Id);

    /// <summary>
    /// Colocación con identificador derivado: <c>{menú}.{definición}</c>. Es único mientras la definición se coloque una sola vez en
    /// ese menú (lo habitual); si se coloca dos veces en el mismo menú hay que dar el identificador con la otra sobrecarga.
    /// </summary>
    public RadialItem ToItem(MenuId menuId, RadialDirection position) => ToItem(new ItemId($"{menuId.Value}.{Id}"), position);

    /// <summary>Igualdad por valor, comparando las etiquetas elemento a elemento (una lista no se compara por referencia).</summary>
    public bool Equals(ActionDefinition? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && Category == other.Category
        && Description == other.Description
        && Equals(Icon, other.Icon)
        && Equals(Action, other.Action)
        && IsBuiltIn == other.IsBuiltIn
        && TagList.SequenceEqual(other.TagList, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Category, Description, Icon, Action, IsBuiltIn, TagList.Count);

    private IEnumerable<string> TagProblems()
    {
        var tags = TagList;
        if (tags.Count > ConfigurationLimits.MaxTags)
        {
            yield return $"Como máximo {ConfigurationLimits.MaxTags} etiquetas.";
        }

        if (tags.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > ConfigurationLimits.MaxTagLength || tag != tag.Trim()))
        {
            yield return $"Cada etiqueta es un texto no vacío, sin espacios en los extremos y de hasta {ConfigurationLimits.MaxTagLength} caracteres.";
        }

        if (tags.Count != tags.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            yield return "Hay etiquetas repetidas.";
        }
    }

    private static bool IsIdCharacter(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-';
}
