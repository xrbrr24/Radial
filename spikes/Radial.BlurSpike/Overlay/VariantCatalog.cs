namespace Radial.BlurSpike.Overlay;

internal sealed record VariantInfo(
    string Id,
    string Name,
    string Technique,
    Func<OverlayWindowBase> Create,
    double DefaultOpacity,
    bool UsesBlurRadius,
    bool UsesBackdropChoice,
    bool UsesAccentChoice,
    bool UsesCircularToggle,
    bool IsCircular);

internal static class VariantCatalog
{
    public static IReadOnlyList<VariantInfo> All { get; } =
    [
        new("A", "WPF layered window", "AllowsTransparency=true + elipse translúcida. SIN blur.", () => new WindowA(), 0.80, false, false, false, false, true),
        new("B", "DWM system backdrop", "DwmSetWindowAttribute(SYSTEMBACKDROP_TYPE=Acrylic transitorio). Ventana rectangular, no layered.", () => new WindowB(), 0.20, false, true, false, false, false),
        // NO VALIDADO: B2 se añadió tras run1; compila, nunca se ha ejecutado. No forma parte de los resultados.
        new("B2", "B + WindowChrome (experimento)", "Como B pero con WindowChrome (marco de cristal), para descartar que el popup desnudo sea la causa.", () => new WindowB(useWindowChrome: true), 0.20, false, true, false, false, false),
        new("C", "DWM backdrop + SetWindowRgn", "Variante B + región elíptica (máscara de 1 bit).", () => new WindowC(), 0.20, false, true, false, false, true),
        new("D", "SetWindowCompositionAttribute", "API NO documentada. Acrylic/BlurBehind vía ACCENT_POLICY. EXPERIMENTAL.", () => new WindowD(), 0.20, false, false, true, true, true),
        new("E", "Captura + BlurEffect", "Foto estática de la pantalla + BlurEffect de WPF. NO es blur en vivo.", () => new WindowE(), 0.45, true, false, false, false, true),
    ];

    public static VariantInfo Get(string id) => All.First(v => v.Id == id);
}
