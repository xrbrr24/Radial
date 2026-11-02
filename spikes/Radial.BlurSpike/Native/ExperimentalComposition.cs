using System.Runtime.InteropServices;

namespace Radial.BlurSpike.Native;

/// <summary>
/// VARIANTE D — AISLADO A PROPÓSITO.
/// SetWindowCompositionAttribute NO está documentada por Microsoft como API pública ni estable: no aparece en
/// los encabezados del SDK, su firma se conoce por ingeniería inversa y puede cambiar o desaparecer en cualquier
/// actualización de Windows. Solo la usa la variante D. Ninguna otra variante ni el producto dependen de este archivo.
/// </summary>
internal static class ExperimentalComposition
{
    public enum AccentState
    {
        Disabled = 0,
        EnableBlurBehind = 3,
        EnableAcrylicBlurBehind = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor; // 0xAABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private const int WCA_ACCENT_POLICY = 19;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hWnd, ref WindowCompositionAttributeData data);

    /// <summary>Devuelve el valor BOOL de la API (0 = fallo). No hay HRESULT ni documentación de los códigos.</summary>
    public static int SetAccent(IntPtr hWnd, AccentState state, uint tintAbgr)
    {
        var policy = new AccentPolicy { AccentState = (int)state, AccentFlags = 2, GradientColor = unchecked((int)tintAbgr) };
        var size = Marshal.SizeOf(policy);
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = pointer, SizeOfData = size };
            return SetWindowCompositionAttribute(hWnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
