namespace Radial.Core.Diagnostics;

/// <summary>
/// Una entrada del registro, tal y como la recibe quien la escribe. Es solo datos: el formato en texto es de <see cref="LogFormatter"/>.
/// </summary>
/// <param name="Timestamp">Cuándo ocurrió (hora local con su desfase).</param>
/// <param name="Level">Gravedad.</param>
/// <param name="Source">Qué componente la produce («CaptureHost», «FileSettingsStore»...). Nunca datos del usuario.</param>
/// <param name="Message">Qué pasó, en una frase. No debe contener rutas, direcciones ni contenido del usuario.</param>
/// <param name="Operation">Qué se estaba haciendo («guardar configuración», «ejecutar acción»...), si se sabe.</param>
/// <param name="Exception">La excepción, si la hay.</param>
/// <param name="Context">Contexto adicional de varias líneas (p. ej. las últimas operaciones conocidas), si lo hay.</param>
/// <param name="ThreadId">Identificador del hilo gestionado que escribe la entrada.</param>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Source,
    string Message,
    string? Operation = null,
    Exception? Exception = null,
    string? Context = null,
    int ThreadId = 0);
