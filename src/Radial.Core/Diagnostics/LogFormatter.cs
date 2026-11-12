using System.Globalization;
using System.Text;

namespace Radial.Core.Diagnostics;

/// <summary>
/// El formato en texto de una entrada del registro. Una primera línea con fecha, nivel, origen, hilo, operación y mensaje, y, si
/// procede, líneas con la excepción (tipo y mensaje de cada una, y la traza de pila) y con el contexto, sangradas. Siempre con la
/// cultura invariante. Todo texto pasa por el <see cref="LogSanitizer"/>. Nunca lanza: lo que no se pueda formatear se indica.
///
/// <code>
/// 2026-11-10T14:03:22.123+01:00 ERR [FileSettingsStore] t7 op=guardar configuración | No se pudo guardar la configuración.
///     ! System.IO.IOException: El proceso no puede obtener acceso al archivo…
///       at Radial.Windows.Storage.FileSettingsStore.Save(…)
///     ctx: 14:03:20.001 procesar gesto
/// </code>
/// </summary>
public static class LogFormatter
{
    public const int MaxExceptionDepth = 6;
    public const int MaxExceptionsPerEntry = 8;
    public const int MaxStackLines = 25;

    public static string Format(LogEntry entry, LogSanitizer? sanitizer = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        sanitizer ??= LogSanitizer.Default;

        try
        {
            var builder = new StringBuilder(256);
            builder.Append(entry.Timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture))
                .Append(' ').Append(Label(entry.Level))
                .Append(" [").Append(Clean(sanitizer, entry.Source, "?")).Append("] t")
                .Append(entry.ThreadId.ToString(CultureInfo.InvariantCulture))
                .Append(" op=").Append(Clean(sanitizer, entry.Operation, "-"))
                .Append(" | ").Append(sanitizer.SanitizeOneLine(entry.Message));

            if (entry.Exception is not null)
            {
                AppendException(builder, entry.Exception, sanitizer);
            }

            if (!string.IsNullOrWhiteSpace(entry.Context))
            {
                foreach (var line in sanitizer.Sanitize(entry.Context).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    builder.AppendLine().Append("    ctx: ").Append(line);
                }
            }

            return builder.ToString();
        }
        catch (Exception exception)
        {
            // El formato no puede hacer fallar a quien registra: se deja una línea mínima y reconocible.
            return $"{entry.Timestamp.ToString("o", CultureInfo.InvariantCulture)} {Label(entry.Level)} [?] t{entry.ThreadId} | (entrada no formateable: {exception.GetType().Name})";
        }
    }

    public static string Label(LogLevel level) => level switch
    {
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private static string Clean(LogSanitizer sanitizer, string? text, string whenEmpty)
    {
        var clean = sanitizer.SanitizeOneLine(text);
        return string.IsNullOrWhiteSpace(clean) ? whenEmpty : clean;
    }

    private static void AppendException(StringBuilder builder, Exception root, LogSanitizer sanitizer)
    {
        var pending = new Stack<(Exception Exception, int Depth)>();
        pending.Push((root, 0));
        var written = 0;
        while (pending.Count > 0)
        {
            var (exception, depth) = pending.Pop();
            if (written >= MaxExceptionsPerEntry)
            {
                builder.AppendLine().Append("    ! (hay más excepciones; se omiten)");
                break;
            }

            written++;
            var indent = new string(' ', 4 + (depth * 2));
            builder.AppendLine().Append(indent).Append("! ")
                .Append(exception.GetType().FullName).Append(": ").Append(sanitizer.SanitizeOneLine(exception.Message));

            if (exception.StackTrace is { Length: > 0 } stack)
            {
                var lines = sanitizer.Sanitize(stack).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var line in lines.Take(MaxStackLines))
                {
                    builder.AppendLine().Append(indent).Append("  ").Append(line);
                }

                if (lines.Length > MaxStackLines)
                {
                    builder.AppendLine().Append(indent).Append("  … ").Append(lines.Length - MaxStackLines).Append(" líneas más");
                }
            }

            if (depth + 1 >= MaxExceptionDepth)
            {
                continue;
            }

            // Se apilan en orden inverso para escribirlas en el orden natural.
            var children = new List<Exception>();
            if (exception is AggregateException aggregate)
            {
                children.AddRange(aggregate.InnerExceptions);
            }
            else if (exception.InnerException is { } inner)
            {
                children.Add(inner);
            }

            for (var index = children.Count - 1; index >= 0; index--)
            {
                pending.Push((children[index], depth + 1));
            }
        }
    }
}
