using Radial.Core.Model;

namespace Radial.Core.Editing;

/// <summary>Códigos estables del resultado de una edición (los comprueban las pruebas; el mensaje es para el usuario).</summary>
public static class EditCodes
{
    public const string NotFound = "edit.not-found";
    public const string Occupied = "edit.occupied";
    public const string Invalid = "edit.invalid";
    public const string Duplicate = "edit.duplicate";
    public const string InUse = "edit.in-use";
    public const string GroupNotEmpty = "edit.group-not-empty";
    public const string Cycle = "edit.cycle";
    public const string TooDeep = "edit.too-deep";
    public const string NotAllowed = "edit.not-allowed";
    public const string LibraryFull = "edit.library-full";
}

/// <summary>
/// Resultado de una edición: si se aplicó y, si no, por qué (código estable y mensaje en español para mostrar). Una edición que no
/// se puede aplicar NO cambia nada.
/// </summary>
public readonly record struct EditResult(bool Success, string Code, string Message)
{
    public static EditResult Ok { get; } = new(true, "ok", string.Empty);

    public static EditResult Fail(string code, string message) => new(false, code, message);
}

/// <summary>Qué hacer con el contenido de un grupo al eliminarlo.</summary>
public enum GroupDeletion
{
    /// <summary>Solo se elimina si el submenú está vacío; si no, no se hace nada y se informa (para pedir confirmación).</summary>
    RefuseIfNotEmpty,

    /// <summary>Se elimina el grupo y todos los submenús que solo dependían de él. Las acciones de la biblioteca no se tocan.</summary>
    Cascade,
}

/// <summary>Dónde está colocado un elemento.</summary>
public sealed record PlacementRef(MenuId Menu, RadialItem Item);

/// <summary>Lo que se perdería al eliminar un grupo (para avisar antes).</summary>
public sealed record GroupContents(int Menus, int Placements, int Groups);
