using Radial.Core.Model;

namespace Radial.Windows.Shortcuts;

/// <summary>
/// Tecla principal de un atajo traducida a Windows. <see cref="Code"/> es el código de tecla virtual (<c>VK_*</c>);
/// <see cref="Extended"/> indica que la tecla es "extendida" (<c>KEYEVENTF_EXTENDEDKEY</c>), lo que distingue p. ej. las
/// flechas de las del teclado numérico. <see cref="Name"/> es el nombre canónico del vocabulario, estable para registros y avisos.
/// Es solo información: no envía nada.
/// </summary>
internal readonly record struct VirtualKey(string Name, ushort Code, bool Extended);

/// <summary>
/// Un <see cref="KeyChord"/> ya traducido. <see cref="Modifiers"/> es el del modelo, sin alterar: el parser no los
/// reinterpreta (ni los valida; de eso se ocupa <see cref="SendKeysAction.GetProblems"/>).
/// </summary>
internal readonly record struct ParsedShortcut(KeyModifiers Modifiers, VirtualKey Key);
