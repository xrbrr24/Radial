using Radial.Core.Catalog;
using Radial.Core.Editing;
using Radial.Core.Model;

namespace Radial.Core.Tests.Model;

/// <summary>
/// Los tipos de acción añadidos tras los atajos y la apertura de destinos: insertar texto, controlar el volumen, controlar la ventana
/// activa y la lista de argumentos de «iniciar aplicación». Aquí, las reglas del dominio; la serialización está en
/// <c>NewActionTypesJsonTests</c> y la ejecución, en Radial.Windows.Tests.
/// </summary>
public class NewActionTypesTests
{
    // Insertar texto

    [Theory]
    [InlineData("Hola")]
    [InlineData("   ")]
    [InlineData("Dos\r\nlíneas\ty tabulador")]
    [InlineData("ñandú, €, 日本語 y 😀")]
    public void TypeText_AcceptsAnyPrintableText_IncludingWhitespaceLineBreaksAndSurrogatePairs(string text) =>
        Assert.Empty(new TypeTextAction(text).GetProblems());

    [Fact]
    public void TypeText_AcceptsExactlyTheMaximumLength_AndRejectsOneMore()
    {
        Assert.Empty(new TypeTextAction(new string('a', TypeTextAction.MaxLength)).GetProblems());
        Assert.NotEmpty(new TypeTextAction(new string('a', TypeTextAction.MaxLength + 1)).GetProblems());
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\0b")]
    [InlineData("pitido\u0007")]
    [InlineData("escape\u001b")]
    public void TypeText_RejectsEmptyAndControlCharacters(string text) =>
        Assert.NotEmpty(new TypeTextAction(text).GetProblems());

    [Fact]
    public void TypeText_RejectsBrokenSurrogates()
    {
        var high = ((char)0xD83D).ToString();
        var low = ((char)0xDE00).ToString();

        Assert.NotEmpty(new TypeTextAction(high).GetProblems());
        Assert.NotEmpty(new TypeTextAction(low + " suelto").GetProblems());
        Assert.NotEmpty(new TypeTextAction("alto sin bajo " + high + " x").GetProblems());
        Assert.Empty(new TypeTextAction(high + low).GetProblems()); // el par completo sí
    }

    [Fact]
    public void TypeText_ProblemMessagesNeverRepeatTheText()
    {
        const string secret = "contraseña-secreta\u0007";

        Assert.DoesNotContain(secret, string.Concat(new TypeTextAction(secret).GetProblems()), StringComparison.Ordinal);
    }

    // Volumen

    [Theory]
    [InlineData(VolumeOperation.Increase, 1)]
    [InlineData(VolumeOperation.Increase, 100)]
    [InlineData(VolumeOperation.Decrease, 1)]
    [InlineData(VolumeOperation.Decrease, 50)]
    [InlineData(VolumeOperation.SetLevel, 0)]
    [InlineData(VolumeOperation.SetLevel, 37)]
    [InlineData(VolumeOperation.SetLevel, 100)]
    [InlineData(VolumeOperation.ToggleMute, 0)]
    [InlineData(VolumeOperation.ToggleMute, 999)]
    public void Volume_AcceptsLevelsAndDifferencesInRange(VolumeOperation operation, int percent) =>
        Assert.Empty(new VolumeAction(operation, percent).GetProblems());

    [Theory]
    [InlineData(VolumeOperation.Increase, 0)]
    [InlineData(VolumeOperation.Increase, -5)]
    [InlineData(VolumeOperation.Increase, 101)]
    [InlineData(VolumeOperation.Decrease, 0)]
    [InlineData(VolumeOperation.Decrease, 101)]
    [InlineData(VolumeOperation.SetLevel, -1)]
    [InlineData(VolumeOperation.SetLevel, 101)]
    [InlineData((VolumeOperation)42, 5)]
    [InlineData((VolumeOperation)(-1), 5)]
    public void Volume_RejectsLevelsDifferencesAndOperationsOutOfRange(VolumeOperation operation, int percent) =>
        Assert.NotEmpty(new VolumeAction(operation, percent).GetProblems());

    // Ventana

    [Theory]
    [InlineData(WindowOperation.Minimize)]
    [InlineData(WindowOperation.Maximize)]
    [InlineData(WindowOperation.Restore)]
    [InlineData(WindowOperation.Close)]
    public void WindowControl_AcceptsEveryKnownOperation(WindowOperation operation) =>
        Assert.Empty(new WindowControlAction(operation).GetProblems());

    [Theory]
    [InlineData((WindowOperation)42)]
    [InlineData((WindowOperation)(-1))]
    public void WindowControl_RejectsAnUnknownOperation(WindowOperation operation) =>
        Assert.NotEmpty(new WindowControlAction(operation).GetProblems());

    // Argumentos de «iniciar aplicación»

    [Fact]
    public void LaunchArguments_ArePassedAsAListWithoutAnyQuotingOrParsing()
    {
        var action = new LaunchApplicationAction(@"C:\Apps\a.exe", ["--abrir", "mi documento.txt", "a & b | c", "\"entre comillas\""], @"C:\Trabajo");

        Assert.Empty(action.GetProblems());
        Assert.Equal(4, action.Arguments!.Count);
        Assert.Equal("mi documento.txt", action.Arguments[1]);
    }

    [Fact]
    public void LaunchArguments_AreComparedByContent_NotByReference()
    {
        var one = new LaunchApplicationAction(@"C:\a.exe", ["x", "y"], @"C:\");
        var same = new LaunchApplicationAction(@"C:\a.exe", new List<string> { "x", "y" }, @"C:\");
        var otherOrder = new LaunchApplicationAction(@"C:\a.exe", ["y", "x"], @"C:\");
        var fewer = new LaunchApplicationAction(@"C:\a.exe", ["x"], @"C:\");

        Assert.Equal(one, same);
        Assert.Equal(one.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(one, otherOrder);
        Assert.NotEqual(one, fewer);
    }

    [Fact]
    public void LaunchArguments_NullAndEmptyMeanNoArguments()
    {
        var none = new LaunchApplicationAction(@"C:\a.exe");
        var empty = new LaunchApplicationAction(@"C:\a.exe", []);

        Assert.Equal(none, empty);
        Assert.Equal(none.GetHashCode(), empty.GetHashCode());
        Assert.Empty(empty.GetProblems());
    }

    [Fact]
    public void LaunchArguments_RejectNullElementsNullCharactersAndExcessiveSizes()
    {
        Assert.NotEmpty(new LaunchApplicationAction(@"C:\a.exe", [null!]).GetProblems());
        Assert.NotEmpty(new LaunchApplicationAction(@"C:\a.exe", ["a\0b"]).GetProblems());
        Assert.NotEmpty(new LaunchApplicationAction(@"C:\a.exe", [new string('x', LaunchApplicationAction.MaxArgumentLength + 1)]).GetProblems());
        Assert.NotEmpty(new LaunchApplicationAction(@"C:\a.exe", [.. Enumerable.Repeat("x", LaunchApplicationAction.MaxArguments + 1)]).GetProblems());
        Assert.NotEmpty(new LaunchApplicationAction(
            @"C:\a.exe",
            [.. Enumerable.Repeat(new string('x', LaunchApplicationAction.MaxArgumentLength), 4)]).GetProblems()); // 32 768 > 30 000
        Assert.Empty(new LaunchApplicationAction(@"C:\a.exe", [.. Enumerable.Repeat("x", LaunchApplicationAction.MaxArguments)]).GetProblems());
        Assert.Empty(new LaunchApplicationAction(@"C:\a.exe", [""]).GetProblems()); // un argumento vacío es legítimo en una línea de comandos
    }

    // División de la línea de argumentos antigua

    public static TheoryData<string?, string[]> Lines => new()
    {
        { null, [] },
        { "", [] },
        { "   \t ", [] },
        { "--x", ["--x"] },
        { "a b  c", ["a", "b", "c"] },
        { "a\tb", ["a", "b"] },
        { "--abrir \"mi doc.txt\" --modo rapido", ["--abrir", "mi doc.txt", "--modo", "rapido"] },
        { "\"\"", [""] },
        { "a\"b c\"d", ["ab cd"] },
        { "\"a\\\"b\"", ["a\"b"] },
        { "a\\\"b", ["a\"b"] },
        { "\"a\\\\\" b", ["a\\", "b"] },
        { @"C:\dir\sub\archivo.txt", [@"C:\dir\sub\archivo.txt"] },
        { "\"C:\\Program Files\\App\\a.exe\" -v", [@"C:\Program Files\App\a.exe", "-v"] },
        { "a & b | c", ["a", "&", "b", "|", "c"] },
    };

    [Theory]
    [MemberData(nameof(Lines))]
    public void ALegacyArgumentLine_IsSplitWithTheWindowsRules(string? line, string[] expected) =>
        Assert.Equal<string>(expected, CommandLineArguments.Split(line));

    // Tipos de acción en la interfaz y el repertorio

    [Fact]
    public void TheNewActions_HaveTheirOwnKindCategoryAndIcon()
    {
        Assert.Equal(ActionKind.Text, ActionKinds.Of(new TypeTextAction("x")));
        Assert.Equal(ActionKind.Volume, ActionKinds.Of(new VolumeAction(VolumeOperation.ToggleMute)));
        Assert.Equal(ActionKind.Window, ActionKinds.Of(new WindowControlAction(WindowOperation.Close)));

        Assert.Equal(ActionCategory.Multimedia, LibraryResolver.InferCategory(new VolumeAction(VolumeOperation.ToggleMute)));
        Assert.Equal(ActionCategory.System, LibraryResolver.InferCategory(new WindowControlAction(WindowOperation.Close)));
        Assert.Equal("VolumeUp", LibraryResolver.DefaultIcon(new VolumeAction(VolumeOperation.ToggleMute)).Value);
        Assert.Equal("TaskView", LibraryResolver.DefaultIcon(new WindowControlAction(WindowOperation.Close)).Value);
        Assert.Equal("Keyboard", LibraryResolver.DefaultIcon(new TypeTextAction("x")).Value);
    }

    [Fact]
    public void TheLibrarySearch_FindsTheTextOfATypeTextActionAndTheArgumentsOfAnApplication()
    {
        var text = new ActionDefinition("t", "Firma", ActionCategory.Editing, string.Empty, new IconReference(IconSource.Glyph, "Keyboard"), new TypeTextAction("Saludos cordiales"));
        var app = new ActionDefinition("a", "Editor", ActionCategory.Applications, string.Empty, new IconReference(IconSource.Glyph, "Apps"), new LaunchApplicationAction(@"C:\Apps\editor.exe", ["--perfil=trabajo"]));
        var library = new[] { text, app };

        Assert.Equal("t", Assert.Single(ActionSearch.Query(library, "cordiales")).Id);
        Assert.Equal("a", Assert.Single(ActionSearch.Query(library, "perfil trabajo")).Id);
    }
}
