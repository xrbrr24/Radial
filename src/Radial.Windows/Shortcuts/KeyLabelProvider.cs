using System.Globalization;
using Radial.Core.Model;
using Radial.Windows.Native;

namespace Radial.Windows.Shortcuts;

/// <summary>
/// Cómo se llama una tecla en la distribución de teclado ACTIVA. La configuración guarda el nombre estable de la tecla
/// (<c>Oem3</c>); lo que ve el usuario (<c>Ñ</c> en un teclado español, <c>`</c> en uno estadounidense) lo da esta interfaz.
/// </summary>
public interface IKeyLabelProvider
{
    /// <summary>El texto de la tecla en la distribución activa, o <c>null</c> si no hay nada mejor que el texto de respaldo del vocabulario.</summary>
    string? GetLabel(string keyName);

    /// <summary>Nombre de la distribución activa (p. ej. «español (España)»), solo informativo.</summary>
    string LayoutName { get; }
}

/// <summary>
/// Implementación real sobre las APIs de Windows (<c>GetKeyboardLayout</c>, <c>MapVirtualKeyEx</c> y <c>GetKeyNameText</c>): el
/// nombre que Windows da a la tecla con la distribución del hilo actual. Solo lectura; no captura ni envía nada. Se consulta cada
/// vez (el usuario puede cambiar de distribución con la aplicación abierta). Los fallos de la API devuelven <c>null</c>.
/// </summary>
public sealed class Win32KeyLabelProvider : IKeyLabelProvider
{
    public string LayoutName
    {
        get
        {
            try
            {
                var layout = KeyboardNativeMethods.GetKeyboardLayout(0);
                var languageId = (int)((long)layout & 0xFFFF);
                return new CultureInfo(languageId).DisplayName;
            }
            catch (Exception exception) when (exception is CultureNotFoundException or ArgumentException)
            {
                return "distribución desconocida";
            }
        }
    }

    public string? GetLabel(string keyName)
    {
        if (!KeyboardKeys.TryGetVirtualKey(keyName, out var virtualKey, out var extended))
        {
            return null;
        }

        // Las teclas multimedia y de navegador no tienen nombre propio en GetKeyNameText: se usa el texto de respaldo.
        if (KeyVocabulary.TryGetInfo(keyName, out var info) && info.Group is KeyGroup.Media or KeyGroup.Browser)
        {
            return null;
        }

        try
        {
            var layout = KeyboardNativeMethods.GetKeyboardLayout(0);
            var scan = KeyboardNativeMethods.MapVirtualKeyEx((uint)virtualKey, KeyboardNativeMethods.MAPVK_VK_TO_VSC_EX, layout);
            if (scan == 0)
            {
                return null;
            }

            var lParam = (int)((scan & 0xFF) << 16);
            if (extended || (scan & 0xFF00) == 0xE000)
            {
                lParam |= 1 << 24;
            }

            var buffer = new char[64];
            var length = KeyboardNativeMethods.GetKeyNameText(lParam, buffer, buffer.Length);
            return length > 0 ? new string(buffer, 0, length) : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
