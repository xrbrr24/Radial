using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Validation;

namespace Radial.Core.Catalog;

/// <summary>Agrupación de las acciones del repertorio. Se amplía con el repertorio; no se asume que sea cerrada.</summary>
public enum ActionCategory
{
    /// <summary>Edición y teclado: copiar, pegar, deshacer, guardar...</summary>
    Editing,

    /// <summary>Navegador y direcciones web.</summary>
    Web,

    /// <summary>Carpetas y archivos.</summary>
    Files,
}

/// <summary>
/// Una acción del repertorio: lo que la identifica y la describe (<see cref="Id"/> estable, nombre, categoría, descripción,
/// icono) más lo que hace (<see cref="Action"/>: su tipo y sus parámetros). El tipo decide el ejecutor: distintas definiciones
/// pueden compartir uno (Ctrl+C y Ctrl+V son dos <see cref="SendKeysAction"/> y las ejecuta el mismo ejecutor de atajos), así que
/// ampliar el repertorio es añadir datos, no clases. Es solo datos y no ejecuta nada; ni la configuración en disco ni el editor
/// existen todavía (fases 5 y 6), y entonces el repertorio se lee de ahí.
/// </summary>
/// <param name="Id">Identificador estable (minúsculas, dígitos, «.», «-»; p. ej. <c>edit.copy</c>). No depende del nombre visible.</param>
public sealed record ActionDefinition(
    string Id,
    string Name,
    ActionCategory Category,
    string Description,
    IconReference Icon,
    RadialAction Action)
{
    /// <summary>Problemas de la definición; vacío si es correcta. Incluye los de la propia acción.</summary>
    public IEnumerable<string> GetProblems()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Id))
        {
            problems.Add("El identificador es obligatorio.");
        }
        else if (Id.Length > ConfigurationLimits.MaxIdLength || !Id.All(IsIdCharacter))
        {
            problems.Add($"El identificador debe tener como máximo {ConfigurationLimits.MaxIdLength} caracteres: minúsculas, dígitos, «.» y «-».");
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Length > ConfigurationLimits.MaxNameLength)
        {
            problems.Add($"El nombre es obligatorio y tiene como máximo {ConfigurationLimits.MaxNameLength} caracteres.");
        }

        if (!Enum.IsDefined(Category))
        {
            problems.Add("La categoría no es válida.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            problems.Add("La descripción es obligatoria.");
        }

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
            problems.Add("Un grupo de menú no es una acción del repertorio: se define en el menú, no aquí.");
        }
        else if (Action is UnknownAction)
        {
            problems.Add("Una acción de tipo desconocido no se puede ejecutar y no puede ser una definición del repertorio.");
        }
        else
        {
            problems.AddRange(Action.GetProblems());
        }

        return problems;
    }

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

    private static bool IsIdCharacter(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-';
}
