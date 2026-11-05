using System.Runtime.InteropServices;

namespace Radial.Windows.Native;

/// <summary>
/// P/Invoke para saber cómo se llama una tecla en la distribución de teclado activa. Solo lectura: no envía ni captura nada.
/// Aparte de <see cref="NativeMethods"/> (captura global) para no mezclar ni tocar lo ya validado.
/// </summary>
internal static partial class KeyboardNativeMethods
{
    /// <summary>De tecla virtual a código de barrido, con el prefijo 0xE0 en las teclas extendidas.</summary>
    internal const uint MAPVK_VK_TO_VSC_EX = 4;

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    internal static partial uint MapVirtualKeyEx(uint code, uint mapType, IntPtr keyboardLayout);

    [LibraryImport("user32.dll", EntryPoint = "GetKeyNameTextW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetKeyNameText(int lParam, [Out] char[] buffer, int size);
}
