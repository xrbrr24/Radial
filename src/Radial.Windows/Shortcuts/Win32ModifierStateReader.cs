using Radial.Core.Model;
using Radial.Windows.Native;

namespace Radial.Windows.Shortcuts;

/// <summary>
/// <see cref="IModifierStateReader"/> real: <c>GetAsyncKeyState</c> de Ctrl, Mayús, Alt y las dos teclas Windows. Solo lee, nunca
/// se llama desde un callback de los hooks y no modifica nada del teclado. (El estado asíncrono incluye lo que otra aplicación
/// inyecte, pero este ejecutor lo consulta antes de enviar y Radial no mantiene ninguna tecla pulsada.)
/// </summary>
public sealed class Win32ModifierStateReader : IModifierStateReader
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12; // Alt
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;

    public KeyModifiers ReadPressed()
    {
        var pressed = KeyModifiers.None;
        pressed |= IsDown(VkControl) ? KeyModifiers.Control : KeyModifiers.None;
        pressed |= IsDown(VkShift) ? KeyModifiers.Shift : KeyModifiers.None;
        pressed |= IsDown(VkMenu) ? KeyModifiers.Alt : KeyModifiers.None;
        pressed |= IsDown(VkLeftWindows) || IsDown(VkRightWindows) ? KeyModifiers.Windows : KeyModifiers.None;
        return pressed;
    }

    // El bit más alto del resultado es «pulsada ahora».
    private static bool IsDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
