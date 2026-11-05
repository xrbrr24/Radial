namespace Radial.Core.Model;

/// <summary>
/// Texto de una combinación de teclas. El nombre guardado de una tecla (<c>Oem3</c>) es estable pero no es lo que ve el usuario:
/// la interfaz pasa la función que da el nombre de la tecla en la distribución activa (<c>Ñ</c>). Sin ella se usa el texto de
/// respaldo del vocabulario. Puro y sin cultura: el mismo resultado en cualquier equipo.
/// </summary>
public static class KeyChordText
{
    private static readonly (KeyModifiers Flag, string Name)[] Modifiers =
    [
        (KeyModifiers.Control, "Ctrl"),
        (KeyModifiers.Shift, "Mayús"),
        (KeyModifiers.Alt, "Alt"),
        (KeyModifiers.Windows, "Win"),
    ];

    /// <summary>Los nombres de los modificadores activos, en el orden Ctrl, Mayús, Alt, Win.</summary>
    public static IReadOnlyList<string> ModifierNames(KeyModifiers modifiers) =>
        Modifiers.Where(m => modifiers.HasFlag(m.Flag)).Select(m => m.Name).ToList();

    /// <summary>El texto de la tecla: el que da <paramref name="keyLabel"/> si devuelve algo, o el de respaldo del vocabulario, o el propio nombre.</summary>
    public static string KeyText(string keyName, Func<string, string?>? keyLabel = null)
    {
        var label = keyLabel?.Invoke(keyName);
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        return KeyVocabulary.TryGetInfo(keyName, out var info) ? info.Fallback : keyName ?? string.Empty;
    }

    /// <summary>La combinación completa, p. ej. <c>Ctrl + Mayús + S</c>.</summary>
    public static string Format(KeyChord? chord, Func<string, string?>? keyLabel = null, string separator = " + ")
    {
        if (chord is null)
        {
            return string.Empty;
        }

        var parts = ModifierNames(chord.Modifiers).Append(KeyText(chord.Key, keyLabel));
        return string.Join(separator, parts);
    }

    /// <summary>
    /// Formas de la combinación para BUSCARLA: en minúsculas y sin separadores, con el nombre guardado y con el texto de la tecla
    /// (<c>ctrlshifts</c>, <c>ctrlmayuss</c>), de modo que «ctrl+s», «ctrl s» o «ctrl mayús s» la encuentren.
    /// </summary>
    public static IReadOnlyList<string> SearchForms(KeyChord? chord, Func<string, string?>? keyLabel = null)
    {
        if (chord is null)
        {
            return [];
        }

        var english = new List<string>();
        var spanish = new List<string>();
        foreach (var (flag, name) in Modifiers)
        {
            if (chord.Modifiers.HasFlag(flag))
            {
                spanish.Add(name);
                english.Add(flag == KeyModifiers.Shift ? "Shift" : flag == KeyModifiers.Windows ? "Win" : name);
            }
        }

        var byName = string.Concat(english) + chord.Key;
        var byLabel = string.Concat(spanish) + KeyText(chord.Key, keyLabel);
        var winAlias = string.Concat(english).Replace("Win", "Windows", StringComparison.Ordinal) + chord.Key;
        return [byName, byLabel, winAlias, string.Concat(spanish) + chord.Key];
    }
}
