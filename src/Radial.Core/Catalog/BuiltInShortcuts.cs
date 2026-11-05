using Radial.Core.Model;

namespace Radial.Core.Catalog;

/// <summary>
/// Las cinco acciones de teclado básicas que ya existían antes de la biblioteca ampliada (Copiar, Pegar, Deshacer, Seleccionar todo
/// y Guardar). No tienen datos propios: son las mismas definiciones de <see cref="FactoryActions"/>, que es la única lista.
/// </summary>
public static class BuiltInShortcuts
{
    public static ActionDefinition Copy { get; } = Factory("edit.copy");

    public static ActionDefinition Paste { get; } = Factory("edit.paste");

    public static ActionDefinition Undo { get; } = Factory("edit.undo");

    public static ActionDefinition SelectAll { get; } = Factory("edit.select-all");

    public static ActionDefinition Save { get; } = Factory("edit.save");

    public static IReadOnlyList<ActionDefinition> All { get; } = [Copy, Paste, Undo, SelectAll, Save];

    private static ActionDefinition Factory(string id) => FactoryActions.Shortcuts.Single(d => d.Id == id);
}
