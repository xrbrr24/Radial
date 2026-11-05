using Radial.Core.Model;

namespace Radial.Windows.Shortcuts;

/// <summary>Resultado de evaluar una tecla pulsada durante la grabación de un atajo.</summary>
public abstract record KeyCaptureOutcome;

/// <summary>Solo se pulsaron modificadores: se sigue esperando la tecla principal.</summary>
public sealed record KeyCaptureWaiting : KeyCaptureOutcome;

/// <summary>Combinación completa y válida.</summary>
public sealed record KeyCaptured(KeyChord Chord) : KeyCaptureOutcome;

/// <summary>La tecla o la combinación no se pueden usar; <see cref="Reason"/> lo explica al usuario. No se guarda nada.</summary>
public sealed record KeyCaptureRejected(string Reason) : KeyCaptureOutcome;

/// <summary>
/// Lógica de la grabación de un atajo, sin interfaz ni teclado real: recibe el código de tecla virtual de lo que se pulsó y los
/// modificadores que había y decide si es una combinación utilizable. El nombre que se guarda es el estable del vocabulario
/// (<see cref="KeyVocabulary"/>), no el carácter que produce la tecla: es la misma combinación con cualquier distribución. La
/// interfaz solo recoge las pulsaciones mientras el usuario tiene abierto el modo de grabación; esto no captura nada por sí mismo.
/// </summary>
public static class KeyCapture
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftShift = 0xA0;
    private const int VkRightMenu = 0xA5;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private const int VkCapsLock = 0x14;
    private const int VkNumLock = 0x90;
    private const int VkScrollLock = 0x91;
    private const int VkProcessKey = 0xE5;

    /// <param name="virtualKey">Código de tecla virtual (<c>VK_*</c>) de la tecla pulsada.</param>
    /// <param name="modifiers">Modificadores que estaban pulsados.</param>
    /// <param name="extended">Si se sabe que la tecla es extendida (distingue Enter del Enter numérico); <c>null</c> si no se sabe.</param>
    public static KeyCaptureOutcome Evaluate(int virtualKey, KeyModifiers modifiers, bool? extended = null)
    {
        if (IsModifier(virtualKey))
        {
            return new KeyCaptureWaiting();
        }

        if (virtualKey is VkCapsLock or VkNumLock or VkScrollLock)
        {
            return new KeyCaptureRejected("Las teclas de bloqueo (Bloq Mayús, Bloq Num, Bloq Despl) no se pueden usar en un atajo.");
        }

        if (virtualKey == VkProcessKey)
        {
            return new KeyCaptureRejected("Esa pulsación la está procesando un método de entrada (IME) y no se puede grabar. Elige la tecla de la lista.");
        }

        if (!ShortcutParser.TryGetName(virtualKey, extended, out var name))
        {
            return new KeyCaptureRejected($"Esa tecla (código {virtualKey}) no está admitida en los atajos de Radial. Elige otra o selecciónala de la lista.");
        }

        var chord = new KeyChord(modifiers, name);
        if (ShortcutRules.GetProblem(chord) is { } problem)
        {
            return new KeyCaptureRejected(problem);
        }

        return new KeyCaptured(chord);
    }

    /// <summary>Un código que es un modificador (Ctrl, Mayús, Alt o Windows, izquierdo o derecho).</summary>
    public static bool IsModifier(int virtualKey) =>
        virtualKey is VkShift or VkControl or VkMenu or VkLeftWindows or VkRightWindows || virtualKey is >= VkLeftShift and <= VkRightMenu;
}

/// <summary>
/// Acceso público a la tabla de teclas de Windows para quien no puede ver el parser (el nombre estable de una tecla y su código).
/// </summary>
public static class KeyboardKeys
{
    /// <summary>El nombre estable de la tecla con ese código de tecla virtual, si pertenece al vocabulario.</summary>
    public static bool TryGetName(int virtualKey, bool? extended, out string name) => ShortcutParser.TryGetName(virtualKey, extended, out name);

    /// <summary>El código de tecla virtual y si es extendida, para un nombre del vocabulario.</summary>
    public static bool TryGetVirtualKey(string? name, out int virtualKey, out bool extended)
    {
        if (ShortcutParser.TryParseKey(name, out var key))
        {
            virtualKey = key.Code;
            extended = key.Extended;
            return true;
        }

        virtualKey = 0;
        extended = false;
        return false;
    }
}
