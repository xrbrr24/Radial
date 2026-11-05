namespace Radial.Core.Catalog;

/// <summary>
/// Categoría de una acción de la biblioteca. Es ABIERTA: un identificador estable (minúsculas, dígitos y «-», 1–32 caracteres), no
/// un catálogo cerrado; las siete iniciales son valores conocidos, y la configuración puede traer otras. El nombre que se muestra
/// es cosa de la interfaz. Un valor sin identificador (<c>default</c>) no es válido.
/// </summary>
public readonly record struct ActionCategory(string Id)
{
    public const int MaxIdLength = 32;

    /// <summary>Edición y teclado: copiar, pegar, deshacer, guardar...</summary>
    public static ActionCategory Editing { get; } = new("editing");

    /// <summary>Navegador: pestañas, recargar, direcciones web.</summary>
    public static ActionCategory Browser { get; } = new("browser");

    /// <summary>Programas que se lanzan.</summary>
    public static ActionCategory Applications { get; } = new("applications");

    /// <summary>Carpetas y archivos.</summary>
    public static ActionCategory Files { get; } = new("files");

    /// <summary>Atajos del sistema: escritorio, administrador de tareas, capturas...</summary>
    public static ActionCategory System { get; } = new("system");

    /// <summary>Teclas multimedia: reproducir, volumen...</summary>
    public static ActionCategory Multimedia { get; } = new("multimedia");

    /// <summary>Acciones creadas por el usuario.</summary>
    public static ActionCategory Custom { get; } = new("custom");

    /// <summary>Las categorías iniciales, en el orden en que se muestran.</summary>
    public static IReadOnlyList<ActionCategory> Standard { get; } = [Editing, Browser, Applications, Files, System, Multimedia, Custom];

    /// <summary>Identificador bien formado: 1–32 caracteres entre minúsculas ASCII, dígitos y «-».</summary>
    public bool IsValid => !string.IsNullOrEmpty(Id) && Id.Length <= MaxIdLength && Id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public override string ToString() => Id ?? string.Empty;
}
