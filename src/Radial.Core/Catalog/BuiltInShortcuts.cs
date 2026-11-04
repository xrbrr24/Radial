using Radial.Core.Model;

namespace Radial.Core.Catalog;

/// <summary>
/// Las primeras acciones de teclado del repertorio. Todas son <see cref="SendKeysAction"/>: la misma clase de acción y el mismo
/// ejecutor, solo cambian los datos (modificadores y tecla). Añadir un atajo es añadir una definición aquí o, más adelante, en la
/// configuración; no requiere código nuevo mientras la tecla esté en <see cref="KeyVocabulary"/>. Los iconos son nombres del
/// catálogo de glifos de la interfaz (<see cref="IconSource.Glyph"/>).
/// </summary>
public static class BuiltInShortcuts
{
    public static ActionDefinition Copy { get; } = Shortcut("edit.copy", "Copiar", "Copy", "Copia la selección al portapapeles (Ctrl+C).", KeyModifiers.Control, "C");

    public static ActionDefinition Paste { get; } = Shortcut("edit.paste", "Pegar", "Paste", "Pega el contenido del portapapeles (Ctrl+V).", KeyModifiers.Control, "V");

    public static ActionDefinition Undo { get; } = Shortcut("edit.undo", "Deshacer", "Undo", "Deshace la última acción (Ctrl+Z).", KeyModifiers.Control, "Z");

    public static ActionDefinition SelectAll { get; } = Shortcut("edit.select-all", "Seleccionar todo", "SelectAll", "Selecciona todo el contenido (Ctrl+A).", KeyModifiers.Control, "A");

    public static ActionDefinition Save { get; } = Shortcut("edit.save", "Guardar", "Save", "Guarda el documento actual (Ctrl+S).", KeyModifiers.Control, "S");

    public static IReadOnlyList<ActionDefinition> All { get; } = [Copy, Paste, Undo, SelectAll, Save];

    private static ActionDefinition Shortcut(string id, string name, string glyph, string description, KeyModifiers modifiers, string key) =>
        new(id, name, ActionCategory.Editing, description, new IconReference(IconSource.Glyph, glyph), new SendKeysAction(new KeyChord(modifiers, key)));
}
