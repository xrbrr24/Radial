using Radial.Core.Geometry;

namespace Radial.Core.Model;

/// <summary>
/// Un elemento de un menú: UNA colocación de una acción (o de un grupo) en una posición. <see cref="Id"/> identifica la
/// colocación y es único en toda la configuración.
///
/// Relación con la biblioteca (<see cref="RadialConfiguration.Actions"/>): si <see cref="DefinitionId"/> no es nulo, el elemento es
/// una colocación ENLAZADA a esa definición y <see cref="Name"/>, <see cref="Action"/> e <see cref="Icon"/> son la vista
/// materializada de ella (nombre e icono de la definición, salvo <see cref="LabelOverride"/> e <see cref="IconOverride"/>, que
/// cambian solo cómo se ve esa colocación): es lo que usan el motor y el overlay. Lo que se guarda en disco de una colocación
/// enlazada es solo la referencia y los ajustes visuales; la acción vive una sola vez, en la biblioteca. La validación comprueba que
/// la vista materializada coincida con la definición y quien edita la regenera (<c>LibraryResolver</c>). Sin
/// <see cref="DefinitionId"/> es una colocación suelta (acción propia de ese elemento, que al guardar pasa a la biblioteca) o un
/// grupo (<see cref="OpenMenuAction"/>: estructura del árbol de menús, nunca una definición) o una acción de tipo desconocido,
/// que se conserva tal cual.
/// </summary>
/// <param name="DefinitionId">Identificador estable de la definición enlazada; <c>null</c> si el elemento no está enlazado.</param>
public sealed record RadialItem(ItemId Id, string Name, RadialDirection Position, RadialAction Action, IconReference? Icon = null, string? DefinitionId = null)
{
    /// <summary>Nombre propio de esta colocación; <c>null</c> = el de la definición.</summary>
    public string? LabelOverride { get; init; }

    /// <summary>Icono propio de esta colocación; <c>null</c> = el de la definición.</summary>
    public IconReference? IconOverride { get; init; }

    /// <summary>Un grupo abre otro menú en lugar de producir un resultado ejecutable.</summary>
    public bool IsGroup => Action is OpenMenuAction;

    /// <summary>La acción es de un tipo desconocido para esta versión: se conserva en la configuración, pero nunca se ejecuta.</summary>
    public bool IsUnsupported => Action is UnknownAction;

    /// <summary>La colocación referencia una definición de la biblioteca.</summary>
    public bool IsLinked => DefinitionId is not null;
}
