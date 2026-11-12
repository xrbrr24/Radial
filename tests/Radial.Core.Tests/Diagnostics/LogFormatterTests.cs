using Radial.Core.Diagnostics;

namespace Radial.Core.Tests.Diagnostics;

/// <summary>El formato de las entradas del registro y la limpieza de datos sensibles. Todo en memoria.</summary>
public class LogFormatterTests
{
    private static readonly DateTimeOffset Moment = new(2026, 11, 10, 14, 3, 22, 123, TimeSpan.FromHours(1));

    private static LogEntry Entry(
        string message = "mensaje",
        LogLevel level = LogLevel.Error,
        string source = "FileSettingsStore",
        string? operation = "guardar configuración",
        Exception? exception = null,
        string? context = null,
        int threadId = 7) =>
        new(Moment, level, source, message, operation, exception, context, threadId);

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    [Fact]
    public void TheFirstLine_HasTimestampLevelSourceThreadOperationAndMessage()
    {
        var text = LogFormatter.Format(Entry("No se pudo guardar."));

        Assert.Equal("2026-11-10T14:03:22.123+01:00 ERR [FileSettingsStore] t7 op=guardar configuración | No se pudo guardar.", text);
    }

    [Theory]
    [InlineData(LogLevel.Information, "INF")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Error, "ERR")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void EachLevelHasItsOwnLabel(LogLevel level, string label)
    {
        Assert.Contains($" {label} [", LogFormatter.Format(Entry(level: level)));
    }

    [Fact]
    public void AMissingOperationOrSource_IsMarkedInsteadOfLeftEmpty()
    {
        var text = LogFormatter.Format(Entry(source: " ", operation: null));

        Assert.Contains("[?]", text);
        Assert.Contains("op=-", text);
    }

    [Fact]
    public void TheMessageIsOneLine_EvenIfItHasLineBreaks()
    {
        var text = LogFormatter.Format(Entry("primera\r\nsegunda\ntercera"));

        Assert.DoesNotContain("\n", text);
        Assert.EndsWith("| primera segunda tercera", text);
    }

    [Fact]
    public void AnException_IsWrittenWithItsTypeMessageAndStackTrace()
    {
        var exception = Thrown(new InvalidOperationException("algo salió mal"));

        var text = LogFormatter.Format(Entry(exception: exception));

        Assert.Contains("! System.InvalidOperationException: algo salió mal", text);
        Assert.Contains("at ", text);
    }

    [Fact]
    public void AnInnerException_IsWrittenAfterItsOuterOne_AndMoreIndented()
    {
        var exception = Thrown(new InvalidOperationException("externa", new IOException("interna")));

        var text = LogFormatter.Format(Entry(exception: exception));

        var outer = text.IndexOf("System.InvalidOperationException: externa", StringComparison.Ordinal);
        var inner = text.IndexOf("System.IO.IOException: interna", StringComparison.Ordinal);
        Assert.True(outer >= 0 && inner > outer);
        Assert.Contains("      ! System.IO.IOException", text); // seis espacios: un nivel más
    }

    [Fact]
    public void AnAggregateException_ListsEachOfItsInnerExceptions()
    {
        var aggregate = new AggregateException("varias", new TimeoutException("a"), new FormatException("b"));

        var text = LogFormatter.Format(Entry(exception: aggregate));

        Assert.Contains("System.TimeoutException: a", text);
        Assert.Contains("System.FormatException: b", text);
    }

    [Fact]
    public void ALongChainOfExceptions_IsCutAtTheDepthLimit()
    {
        Exception chain = new InvalidOperationException("nivel-0");
        for (var level = 1; level < 20; level++)
        {
            chain = new InvalidOperationException($"nivel-{level}", chain);
        }

        var text = LogFormatter.Format(Entry(exception: chain));

        var written = text.Split('\n').Count(line => line.TrimStart().StartsWith("! ", StringComparison.Ordinal));
        Assert.Equal(LogFormatter.MaxExceptionDepth, written);
    }

    [Fact]
    public void TheContext_IsWrittenAsIndentedLines()
    {
        var text = LogFormatter.Format(Entry(context: "14:03:20.001 procesar gesto\n14:03:21.500 guardar"));

        Assert.Contains("    ctx: 14:03:20.001 procesar gesto", text);
        Assert.Contains("    ctx: 14:03:21.500 guardar", text);
    }

    [Fact]
    public void TheUserProfileFolder_IsNeverWritten_NeitherInTheMessageNorInTheException()
    {
        var sanitizer = new LogSanitizer(@"C:\Users\maria");
        var exception = Thrown(new IOException(@"No se puede abrir C:\Users\maria\Documentos\a.txt"));

        var text = LogFormatter.Format(Entry(@"Falló c:\users\MARIA\Descargas", exception: exception), sanitizer);

        Assert.DoesNotContain("maria", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%USERPROFILE%\Descargas", text);
        Assert.Contains(@"%USERPROFILE%\Documentos\a.txt", text);
    }

    [Fact]
    public void ThePartOfAnAddressAfterTheQuestionMark_IsNeverWritten()
    {
        var text = LogFormatter.Format(Entry("Abrir https://sitio.example/ruta/a?token=secreto&x=1 y http://otro.example/p#sesion=abc fin"));

        Assert.DoesNotContain("secreto", text);
        Assert.DoesNotContain("sesion=abc", text);
        Assert.Contains("https://sitio.example/ruta/a?…", text);
        Assert.Contains("http://otro.example/p?…", text);
        Assert.EndsWith("fin", text);
    }

    [Fact]
    public void ALongMessage_IsCutAndSaysHowMuchWasLeftOut()
    {
        var text = LogFormatter.Format(Entry(new string('x', 500)), new LogSanitizer(maxLength: 100));

        Assert.Contains("[recortado: 400 caracteres más]", text);
        Assert.DoesNotContain(new string('x', 101), text);
    }

    private sealed class BrokenException : Exception
    {
        public override string Message => throw new InvalidOperationException("el mensaje no se puede leer");
    }

    [Fact]
    public void AnEntryThatCannotBeFormatted_NeverThrows_AndLeavesAnIdentifiableLine()
    {
        var text = LogFormatter.Format(Entry(exception: new BrokenException()));

        Assert.Contains("entrada no formateable", text);
        Assert.Contains("ERR", text);
    }

    [Fact]
    public void FormattingNeedsAnEntry()
    {
        Assert.Throws<ArgumentNullException>(() => LogFormatter.Format(null!));
    }
}

public class LogSanitizerTests
{
    [Fact]
    public void WithoutAProfileFolder_OnlyAddressesAndLengthAreHandled()
    {
        Assert.Equal(@"C:\Users\maria\a.txt", LogSanitizer.Default.Sanitize(@"C:\Users\maria\a.txt"));
    }

    [Theory]
    [InlineData(@"C:\Users\maria\a.txt", @"%USERPROFILE%\a.txt")]
    [InlineData(@"c:\USERS\Maria\a.txt", @"%USERPROFILE%\a.txt")]
    [InlineData("C:/Users/maria/a.txt", "%USERPROFILE%/a.txt")]
    public void TheProfileFolder_IsReplacedWhateverTheCaseAndSlashes(string text, string expected)
    {
        Assert.Equal(expected, new LogSanitizer(@"C:\Users\maria\").Sanitize(text));
    }

    [Fact]
    public void NullOrEmptyText_BecomesEmpty()
    {
        Assert.Equal(string.Empty, LogSanitizer.Default.Sanitize(null));
        Assert.Equal(string.Empty, LogSanitizer.Default.SanitizeOneLine(string.Empty));
    }

    [Fact]
    public void SanitizeKeepsLineBreaks_AndOneLineRemovesThem()
    {
        Assert.Equal("a\nb", LogSanitizer.Default.Sanitize("a\nb"));
        Assert.Equal("a b c", LogSanitizer.Default.SanitizeOneLine("a\r\nb\nc"));
    }

    [Fact]
    public void AnAddressWithoutQueryOrFragment_IsLeftAsItIs()
    {
        Assert.Equal("https://www.wikipedia.org/wiki/Radial", LogSanitizer.Default.Sanitize("https://www.wikipedia.org/wiki/Radial"));
    }

    [Fact]
    public void TheMaximumLengthMustBeReasonable()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogSanitizer(maxLength: 4));
    }
}
