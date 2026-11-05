using Radial.Core.Model;

namespace Radial.Core.Catalog;

/// <summary>
/// Las acciones de TECLADO de la biblioteca inicial (las que no dependen del equipo): edición, navegador, sistema y multimedia.
/// Todas son <see cref="SendKeysAction"/>: la misma clase de acción y el mismo ejecutor, solo cambian los datos. Las acciones que
/// dependen del equipo (programas y carpetas conocidas) las añade <c>Radial.Windows</c>. Los iconos son nombres del catálogo de
/// glifos de la interfaz. Son definiciones de fábrica (<see cref="ActionDefinition.IsBuiltIn"/>): se pueden editar y borrar como
/// cualquier otra, y se pueden volver a añadir las que falten.
/// </summary>
public static class FactoryActions
{
    private const KeyModifiers Ctrl = KeyModifiers.Control;
    private const KeyModifiers Shift = KeyModifiers.Shift;
    private const KeyModifiers Alt = KeyModifiers.Alt;
    private const KeyModifiers Win = KeyModifiers.Windows;

    /// <summary>Los atajos de fábrica, en el orden en que se muestran.</summary>
    public static IReadOnlyList<ActionDefinition> Shortcuts { get; } =
    [
        // Edición
        Key("edit.copy", "Copiar", ActionCategory.Editing, "Copy", "Copia la selección al portapapeles.", Ctrl, "C", "portapapeles"),
        Key("edit.paste", "Pegar", ActionCategory.Editing, "Paste", "Pega el contenido del portapapeles.", Ctrl, "V", "portapapeles"),
        Key("edit.cut", "Cortar", ActionCategory.Editing, "Cut", "Corta la selección y la lleva al portapapeles.", Ctrl, "X", "portapapeles"),
        Key("edit.undo", "Deshacer", ActionCategory.Editing, "Undo", "Deshace la última acción.", Ctrl, "Z"),
        Key("edit.redo", "Rehacer", ActionCategory.Editing, "Redo", "Rehace la acción deshecha.", Ctrl, "Y"),
        Key("edit.select-all", "Seleccionar todo", ActionCategory.Editing, "SelectAll", "Selecciona todo el contenido.", Ctrl, "A"),
        Key("edit.save", "Guardar", ActionCategory.Editing, "Save", "Guarda el documento actual.", Ctrl, "S"),
        Key("edit.find", "Buscar", ActionCategory.Editing, "Find", "Abre la búsqueda en la aplicación activa.", Ctrl, "F"),
        Key("edit.new", "Nuevo", ActionCategory.Editing, "New", "Crea un documento o una ventana nueva.", Ctrl, "N"),
        Key("edit.open", "Abrir", ActionCategory.Editing, "Open", "Abre un archivo en la aplicación activa.", Ctrl, "O"),
        Key("edit.print", "Imprimir", ActionCategory.Editing, "Print", "Abre el cuadro de impresión.", Ctrl, "P"),
        Key("edit.delete", "Suprimir", ActionCategory.Editing, "Delete", "Borra la selección o el carácter siguiente.", KeyModifiers.None, "Delete"),

        // Navegador
        Key("browser.new-tab", "Pestaña nueva", ActionCategory.Browser, "New", "Abre una pestaña nueva.", Ctrl, "T", "pestaña"),
        Key("browser.close-tab", "Cerrar pestaña", ActionCategory.Browser, "Close", "Cierra la pestaña activa.", Ctrl, "W", "pestaña"),
        Key("browser.reopen-tab", "Reabrir pestaña", ActionCategory.Browser, "Undo", "Reabre la última pestaña cerrada.", Ctrl | Shift, "T", "pestaña"),
        Key("browser.next-tab", "Pestaña siguiente", ActionCategory.Browser, "Forward", "Pasa a la pestaña siguiente.", Ctrl, "Tab", "pestaña"),
        Key("browser.previous-tab", "Pestaña anterior", ActionCategory.Browser, "Back", "Pasa a la pestaña anterior.", Ctrl | Shift, "Tab", "pestaña"),
        Key("browser.refresh", "Recargar", ActionCategory.Browser, "Refresh", "Recarga la página.", KeyModifiers.None, "F5"),
        Key("browser.back", "Atrás", ActionCategory.Browser, "Back", "Vuelve a la página anterior.", Alt, "ArrowLeft"),
        Key("browser.forward", "Adelante", ActionCategory.Browser, "Forward", "Avanza a la página siguiente.", Alt, "ArrowRight"),
        Key("browser.address-bar", "Barra de direcciones", ActionCategory.Browser, "Globe", "Selecciona la barra de direcciones.", Ctrl, "L"),
        Key("browser.private", "Ventana privada", ActionCategory.Browser, "Lock", "Abre una ventana privada.", Ctrl | Shift, "N"),

        // Sistema
        Key("system.show-desktop", "Mostrar el escritorio", ActionCategory.System, "Desktop", "Minimiza todas las ventanas.", Win, "D"),
        Key("system.file-explorer", "Explorador de archivos", ActionCategory.System, "Folder", "Abre el Explorador de archivos.", Win, "E"),
        Key("system.task-view", "Vista de tareas", ActionCategory.System, "TaskView", "Muestra todas las ventanas y los escritorios.", Win, "Tab"),
        Key("system.switch-window", "Cambiar de ventana", ActionCategory.System, "TaskView", "Cambia a la ventana anterior.", Alt, "Tab"),
        Key("system.screenshot", "Captura de pantalla", ActionCategory.System, "Camera", "Abre la captura de recortes.", Win | Shift, "S", "recorte"),
        Key("system.task-manager", "Administrador de tareas", ActionCategory.System, "Settings", "Abre el Administrador de tareas.", Ctrl | Shift, "Escape"),
        Key("system.settings", "Configuración de Windows", ActionCategory.System, "Settings", "Abre la Configuración de Windows.", Win, "I"),
        Key("system.clipboard-history", "Historial del portapapeles", ActionCategory.System, "Paste", "Abre el historial del portapapeles.", Win, "V", "portapapeles"),
        Key("system.emoji", "Panel de emojis", ActionCategory.System, "Emoji", "Abre el panel de emojis y símbolos.", Win, "OemPeriod"),
        Key("system.run", "Ejecutar", ActionCategory.System, "Open", "Abre el cuadro Ejecutar.", Win, "R"),
        Key("system.print-screen", "Imprimir pantalla", ActionCategory.System, "Camera", "Copia la pantalla al portapapeles.", KeyModifiers.None, "PrintScreen"),

        // Multimedia
        Key("media.play-pause", "Reproducir / pausa", ActionCategory.Multimedia, "Play", "Reproduce o pausa el contenido multimedia.", KeyModifiers.None, "MediaPlayPause"),
        Key("media.next", "Pista siguiente", ActionCategory.Multimedia, "Next", "Pasa a la pista siguiente.", KeyModifiers.None, "MediaNextTrack"),
        Key("media.previous", "Pista anterior", ActionCategory.Multimedia, "Previous", "Vuelve a la pista anterior.", KeyModifiers.None, "MediaPreviousTrack"),
        Key("media.stop", "Detener", ActionCategory.Multimedia, "Stop", "Detiene la reproducción.", KeyModifiers.None, "MediaStop"),
        Key("media.volume-up", "Subir volumen", ActionCategory.Multimedia, "VolumeUp", "Sube el volumen.", KeyModifiers.None, "VolumeUp"),
        Key("media.volume-down", "Bajar volumen", ActionCategory.Multimedia, "VolumeDown", "Baja el volumen.", KeyModifiers.None, "VolumeDown"),
        Key("media.mute", "Silenciar", ActionCategory.Multimedia, "Mute", "Silencia o activa el sonido.", KeyModifiers.None, "VolumeMute"),
    ];

    private static ActionDefinition Key(
        string id,
        string name,
        ActionCategory category,
        string glyph,
        string description,
        KeyModifiers modifiers,
        string key,
        params string[] tags) =>
        new(id, name, category, description, new IconReference(IconSource.Glyph, glyph), new SendKeysAction(new KeyChord(modifiers, key)), tags.Length == 0 ? null : tags, IsBuiltIn: true);
}
