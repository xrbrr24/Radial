namespace Radial.App.Runtime;

/// <summary>
/// El contenido de la sección de diagnóstico para el centro de control: la vista se elige por este tipo. Lleva el anfitrión de la
/// captura y, en el modo de autoprueba desatendida, la ruta donde escribir el resultado.
/// </summary>
public sealed class DiagnosticsPage
{
    internal DiagnosticsPage(CaptureHost host, string? selfTestOutput)
    {
        Host = host;
        SelfTestOutput = selfTestOutput;
    }

    internal CaptureHost Host { get; }

    /// <summary>Ruta del resultado de la autoprueba desatendida (<c>--selftest</c>), o <c>null</c> en el uso normal.</summary>
    public string? SelfTestOutput { get; }
}
