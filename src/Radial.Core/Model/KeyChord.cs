namespace Radial.Core.Model;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8,
}

/// <summary>
/// Combinación de teclado independiente de cualquier framework de interfaz. <see cref="Key"/> es un nombre
/// legible ("T", "F5", "Enter"); la capa de Windows define cómo se traduce a teclas reales.
/// </summary>
public sealed record KeyChord(KeyModifiers Modifiers, string Key);
