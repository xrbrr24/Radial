using Radial.Core.Geometry;

namespace Radial.Core.Model;

/// <summary>
/// Un elemento de un menú: UNA colocación de una acción (o de un grupo) en una posición. <see cref="Id"/> identifica la
/// colocación y es único en toda la configuración; <see cref="DefinitionId"/> dice, si el elemento salió de una definición del
/// repertorio (<c>ActionDefinition</c>), cuál: la misma definición puede estar colocada en varios menús, cada colocación con su
/// propio <see cref="Id"/>. Un grupo (<see cref="OpenMenuAction"/>) es estructura del árbol de menús y no tiene definición.
/// </summary>
/// <param name="DefinitionId">Identificador estable de la definición de origen; <c>null</c> si el elemento no viene del repertorio.</param>
public sealed record RadialItem(ItemId Id, string Name, RadialDirection Position, RadialAction Action, IconReference? Icon = null, string? DefinitionId = null)
{
    /// <summary>Un grupo abre otro menú en lugar de producir un resultado ejecutable.</summary>
    public bool IsGroup => Action is OpenMenuAction;

    /// <summary>La acción es de un tipo desconocido para esta versión: se conserva en la configuración, pero nunca se ejecuta.</summary>
    public bool IsUnsupported => Action is UnknownAction;
}
