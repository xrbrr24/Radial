namespace Radial.Core.Model;

/// <summary>Grupo de teclas del vocabulario (para mostrarlas ordenadas y para describirlo).</summary>
public enum KeyGroup
{
    Letters,
    Digits,
    Function,
    Editing,
    Navigation,
    Numpad,

    /// <summary>Teclas de signos. Se nombran por su tecla virtual (<c>Oem1</c>, <c>OemComma</c>...), no por el carácter que dan.</summary>
    Punctuation,
    System,
    Media,
    Browser,
}

/// <param name="Name">Nombre canónico y estable (el que se guarda en la configuración).</param>
/// <param name="Group">Grupo al que pertenece.</param>
/// <param name="Fallback">Texto que se muestra si Windows no da un nombre para la tecla en la distribución activa.</param>
public readonly record struct KeyInfo(string Name, KeyGroup Group, string Fallback);

/// <summary>
/// ÚNICA definición de qué nombres de tecla admite un atajo (<see cref="KeyChord.Key"/>). Es una regla del dominio: la usa
/// la validación de <see cref="SendKeysAction"/> y la capa de Windows la recorre para asignar a cada nombre su código de
/// tecla virtual, sin llevar una lista propia. Los códigos de Windows NO viven aquí.
///
/// Qué es un nombre: la identidad ESTABLE de una tecla, independiente del texto que muestra la interfaz y de la distribución del
/// teclado. Las letras, los dígitos y las teclas con nombre propio (<c>Enter</c>, <c>Space</c>, <c>Delete</c>...) se llaman como
/// la tecla; las teclas de signos se llaman como su tecla virtual de Windows (<c>Oem1</c>, <c>OemPlus</c>, <c>OemComma</c>,
/// <c>Oem3</c>...): en un teclado español <c>Oem3</c> es la Ñ y en uno estadounidense es la tilde, y una configuración guardada
/// sigue enviando la misma tecla física con cualquier distribución. Cómo se llama esa tecla en la distribución activa lo dice la
/// capa de Windows (no el nombre guardado).
///
/// Vocabulario CERRADO y sin alias: letras A–Z y dígitos 0–9 (la fila superior); F1–F24; <c>Enter</c>, <c>Tab</c>, <c>Space</c>,
/// <c>Escape</c>, <c>Backspace</c>, <c>Delete</c>, <c>Insert</c>, <c>Home</c>, <c>End</c>, <c>PageUp</c>, <c>PageDown</c> y las
/// cuatro flechas; el teclado numérico (<c>Numpad0</c>–<c>Numpad9</c> y sus operadores y <c>NumpadEnter</c>); <c>PrintScreen</c>,
/// <c>Pause</c> y <c>ContextMenu</c>; las teclas de signos <c>Oem*</c>; las multimedia (<c>MediaPlayPause</c>, volumen...) y las
/// de navegador (<c>BrowserBack</c>...). Quedan fuera los alias, las teclas de bloqueo y los modificadores (esos son
/// <see cref="KeyModifiers"/>). Ampliarlo exige tocar la lista de aquí y la tabla de códigos de Windows (una prueba lo comprueba).
///
/// Comparación: sin distinguir mayúsculas de minúsculas, solo ASCII, sin recortar espacios (<c>" A"</c> no es <c>A</c>) y sin
/// depender de la cultura. El nombre canónico (el de <see cref="Names"/>) es estable.
/// </summary>
public static class KeyVocabulary
{
    private const int LastFunctionKey = 24;

    // Orden de inicialización: lo que usan los demás miembros va antes.
    private static readonly KeyInfo[] AllKeys = BuildAll();

    private static readonly Dictionary<string, KeyInfo> ByName = AllKeys.ToDictionary(k => k.Name, k => k, StringComparer.OrdinalIgnoreCase);

    /// <summary>Todas las teclas, con su grupo y su texto de respaldo, en el orden en que se muestran.</summary>
    public static IReadOnlyList<KeyInfo> All { get; } = Array.AsReadOnly(AllKeys);

    /// <summary>Los nombres canónicos admitidos. Solo lectura.</summary>
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(AllKeys.Select(k => k.Name).ToArray());

    /// <summary>Resumen legible del vocabulario para los mensajes de validación.</summary>
    public static string Description { get; } =
        $"A–Z, 0–9, F1–F{LastFunctionKey}, Enter, Tab, Space, Escape, Backspace, Delete, Insert, Home, End, PageUp, PageDown, ArrowLeft, ArrowUp, ArrowRight, ArrowDown, " +
        "teclado numérico (Numpad0–Numpad9, NumpadMultiply, NumpadAdd, NumpadSubtract, NumpadDecimal, NumpadDivide, NumpadEnter), PrintScreen, Pause, ContextMenu, " +
        "teclas de signos (Oem1, OemPlus, OemComma, OemMinus, OemPeriod, Oem2–Oem8, Oem102), teclas multimedia (MediaPlayPause, MediaNextTrack, MediaPreviousTrack, MediaStop, VolumeUp, VolumeDown, VolumeMute) " +
        "y de navegador (BrowserBack, BrowserForward, BrowserRefresh, BrowserHome)";

    /// <summary>
    /// Devuelve el nombre canónico de <paramref name="name"/> si pertenece al vocabulario. Falso, sin lanzar, si es nulo, está
    /// vacío, lleva espacios, no es ASCII o no está en la lista.
    /// </summary>
    public static bool TryGetCanonicalName(string? name, out string canonicalName)
    {
        if (TryGetInfo(name, out var info))
        {
            canonicalName = info.Name;
            return true;
        }

        canonicalName = string.Empty;
        return false;
    }

    /// <summary>Como <see cref="TryGetCanonicalName"/>, pero devuelve toda la información de la tecla.</summary>
    public static bool TryGetInfo(string? name, out KeyInfo info)
    {
        // Solo ASCII, por construcción. Medido en .NET 10: sin este filtro, OrdinalIgnoreCase ya rechaza el signo Kelvin, la 's'
        // larga, la 'i' sin punto y los caracteres de ancho completo, así que hoy no cambia ningún resultado. Se mantiene para
        // que "solo ASCII, sin depender de las tablas de mayúsculas del runtime ni de la cultura" sea una garantía y no una
        // coincidencia; las pruebas fijan esos casos.
        if (!string.IsNullOrEmpty(name) && System.Text.Ascii.IsValid(name) && ByName.TryGetValue(name, out info))
        {
            return true;
        }

        info = default;
        return false;
    }

    private static KeyInfo[] BuildAll()
    {
        var keys = new List<KeyInfo>();

        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            keys.Add(new KeyInfo(letter.ToString(), KeyGroup.Letters, letter.ToString()));
        }

        for (var digit = '0'; digit <= '9'; digit++)
        {
            keys.Add(new KeyInfo(digit.ToString(), KeyGroup.Digits, digit.ToString()));
        }

        for (var number = 1; number <= LastFunctionKey; number++)
        {
            keys.Add(new KeyInfo($"F{number}", KeyGroup.Function, $"F{number}"));
        }

        keys.AddRange(
        [
            new("Enter", KeyGroup.Editing, "Enter"),
            new("Tab", KeyGroup.Editing, "Tab"),
            new("Space", KeyGroup.Editing, "Espacio"),
            new("Escape", KeyGroup.Editing, "Esc"),
            new("Backspace", KeyGroup.Editing, "Retroceso"),
            new("Delete", KeyGroup.Editing, "Supr"),
            new("Insert", KeyGroup.Editing, "Insert"),
            new("Home", KeyGroup.Navigation, "Inicio"),
            new("End", KeyGroup.Navigation, "Fin"),
            new("PageUp", KeyGroup.Navigation, "Re Pág"),
            new("PageDown", KeyGroup.Navigation, "Av Pág"),
            new("ArrowLeft", KeyGroup.Navigation, "←"),
            new("ArrowUp", KeyGroup.Navigation, "↑"),
            new("ArrowRight", KeyGroup.Navigation, "→"),
            new("ArrowDown", KeyGroup.Navigation, "↓"),
        ]);

        for (var digit = 0; digit <= 9; digit++)
        {
            keys.Add(new KeyInfo($"Numpad{digit}", KeyGroup.Numpad, $"Num {digit}"));
        }

        keys.AddRange(
        [
            new("NumpadMultiply", KeyGroup.Numpad, "Num *"),
            new("NumpadAdd", KeyGroup.Numpad, "Num +"),
            new("NumpadSubtract", KeyGroup.Numpad, "Num -"),
            new("NumpadDecimal", KeyGroup.Numpad, "Num ."),
            new("NumpadDivide", KeyGroup.Numpad, "Num /"),
            new("NumpadEnter", KeyGroup.Numpad, "Num Enter"),
            new("PrintScreen", KeyGroup.System, "Impr Pant"),
            new("Pause", KeyGroup.System, "Pausa"),
            new("ContextMenu", KeyGroup.System, "Menú contextual"),
            new("Oem1", KeyGroup.Punctuation, "OEM 1"),
            new("OemPlus", KeyGroup.Punctuation, "+"),
            new("OemComma", KeyGroup.Punctuation, ","),
            new("OemMinus", KeyGroup.Punctuation, "-"),
            new("OemPeriod", KeyGroup.Punctuation, "."),
            new("Oem2", KeyGroup.Punctuation, "OEM 2"),
            new("Oem3", KeyGroup.Punctuation, "OEM 3"),
            new("Oem4", KeyGroup.Punctuation, "OEM 4"),
            new("Oem5", KeyGroup.Punctuation, "OEM 5"),
            new("Oem6", KeyGroup.Punctuation, "OEM 6"),
            new("Oem7", KeyGroup.Punctuation, "OEM 7"),
            new("Oem8", KeyGroup.Punctuation, "OEM 8"),
            new("Oem102", KeyGroup.Punctuation, "OEM 102"),
            new("MediaPlayPause", KeyGroup.Media, "Reproducir/Pausa"),
            new("MediaNextTrack", KeyGroup.Media, "Pista siguiente"),
            new("MediaPreviousTrack", KeyGroup.Media, "Pista anterior"),
            new("MediaStop", KeyGroup.Media, "Detener"),
            new("VolumeUp", KeyGroup.Media, "Subir volumen"),
            new("VolumeDown", KeyGroup.Media, "Bajar volumen"),
            new("VolumeMute", KeyGroup.Media, "Silenciar"),
            new("BrowserBack", KeyGroup.Browser, "Navegador: atrás"),
            new("BrowserForward", KeyGroup.Browser, "Navegador: adelante"),
            new("BrowserRefresh", KeyGroup.Browser, "Navegador: recargar"),
            new("BrowserHome", KeyGroup.Browser, "Navegador: inicio"),
        ]);

        return keys.ToArray();
    }
}
