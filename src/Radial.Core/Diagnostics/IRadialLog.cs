namespace Radial.Core.Diagnostics;

/// <summary>
/// Por dónde cualquier proyecto de Radial deja constancia de un suceso, sin saber dónde ni cómo se guarda (disco, memoria, nada) y sin
/// depender de WPF. <see cref="Write"/> NO lanza nunca y NO bloquea de forma apreciable: puede llamarse desde cualquier hilo, también
/// desde un callback que no debe esperar. Si el registro falla, el fallo se queda dentro del registro.
/// </summary>
public interface IRadialLog
{
    void Write(LogLevel level, string source, string message, Exception? exception = null, string? operation = null, string? context = null);
}

/// <summary>Un registro que no hace nada: para quien no tiene registro (pruebas, herramientas de desarrollo).</summary>
public sealed class NullRadialLog : IRadialLog
{
    public static NullRadialLog Instance { get; } = new();

    public void Write(LogLevel level, string source, string message, Exception? exception = null, string? operation = null, string? context = null)
    {
    }
}

/// <summary>Atajos para escribir con la gravedad ya elegida.</summary>
public static class RadialLogExtensions
{
    public static void Info(this IRadialLog log, string source, string message, string? operation = null) =>
        log.Write(LogLevel.Information, source, message, null, operation);

    public static void Warn(this IRadialLog log, string source, string message, Exception? exception = null, string? operation = null) =>
        log.Write(LogLevel.Warning, source, message, exception, operation);

    public static void Error(this IRadialLog log, string source, string message, Exception? exception = null, string? operation = null) =>
        log.Write(LogLevel.Error, source, message, exception, operation);
}
