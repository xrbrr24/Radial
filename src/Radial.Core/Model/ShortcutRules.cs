namespace Radial.Core.Model;

/// <summary>
/// Combinaciones que un atajo no puede enviar aunque cada tecla esté en el vocabulario. Se comprueban al validar
/// <see cref="SendKeysAction"/>, así que una definición con una de ellas no se guarda. La lista es corta a propósito: solo lo que
/// está documentado como imposible de enviar desde una aplicación.
/// </summary>
public static class ShortcutRules
{
    /// <summary>El motivo por el que la combinación no es utilizable, o <c>null</c> si lo es.</summary>
    public static string? GetProblem(KeyChord? chord)
    {
        if (chord is null || !KeyVocabulary.TryGetCanonicalName(chord.Key, out var key))
        {
            return null; // eso ya lo señala SendKeysAction
        }

        // Ctrl+Alt+Supr es la «secuencia de atención segura»: la trata Windows y no se puede enviar con SendInput.
        if (key == "Delete" && chord.Modifiers.HasFlag(KeyModifiers.Control) && chord.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            return "Ctrl+Alt+Supr es una secuencia reservada de Windows: ninguna aplicación puede enviarla.";
        }

        return null;
    }
}
