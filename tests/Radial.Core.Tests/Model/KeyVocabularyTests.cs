using System.Globalization;
using Radial.Core.Model;

namespace Radial.Core.Tests.Model;

/// <summary>El vocabulario cerrado de teclas de un atajo: la única fuente de verdad de qué nombres existen.</summary>
public class KeyVocabularyTests
{
    // Escrito aquí a mano, no tomado de KeyVocabulary: es la definición aprobada contra la que se comprueba.
    public static readonly string[] Approved =
    [
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
        "Enter", "Tab", "ArrowLeft", "ArrowUp", "ArrowRight", "ArrowDown",
    ];

    public static TheoryData<string> ApprovedNames => new(Approved);

    public static TheoryData<string> RejectedNames =>
    [
        "Ñ", "ñ", "Á", "é", "Ａ", "K", "ſ", "ı", // no ASCII, o que lo parece
        "Space", "Delete", "Del", "Escape", "Esc", "Backspace", "Insert", "Home", "End", "PageUp", "PageDown", // fuera del vocabulario
        "Numpad0", "NumPad5", "NumpadEnter", "NumericEnter", "Return", "Left", "Right", "Up", "Down", // numpad, alias
        "Arrow Left", "ArrowLeft2", "Ctrl", "Control", "Shift", "Alt", "Win", "AA", "A+B", "Unknown", "OEM_PLUS",
        "F0", "F25", "F01", "F100", "F-1", "10", "-1",
        "!", "-", "+", "=", ",", ".", ";", "'", "`", "[", "\\", "/", "<", "º", // símbolos
        "", " ", " A", "A ", "\tTab", "Enter ", "A\n", "A\0", "\0", // vacío o con espacios: no se recorta nada
    ];

    [Fact]
    public void Names_AreExactlyTheApprovedVocabulary()
    {
        Assert.Equal(66, KeyVocabulary.Names.Count);
        Assert.Equal(Approved.Order(StringComparer.Ordinal), KeyVocabulary.Names.Order(StringComparer.Ordinal));
        Assert.Equal(KeyVocabulary.Names.Count, KeyVocabulary.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Names_CannotBeModifiedByTheCaller()
    {
        var list = (IList<string>)KeyVocabulary.Names;

        Assert.Throws<NotSupportedException>(() => list.Add("Space"));
        Assert.Throws<NotSupportedException>(() => list[0] = "Z");
        Assert.Equal(66, KeyVocabulary.Names.Count);
    }

    [Theory]
    [MemberData(nameof(ApprovedNames))]
    public void ApprovedNames_AreAccepted_InAnyCase_AndReturnTheirCanonicalName(string name)
    {
        foreach (var spelling in new[] { name, name.ToLowerInvariant(), name.ToUpperInvariant() })
        {
            Assert.True(KeyVocabulary.TryGetCanonicalName(spelling, out var canonical), $"'{spelling}' debería aceptarse.");
            Assert.Equal(name, canonical);
        }
    }

    [Theory]
    [InlineData("enter", "Enter")]
    [InlineData("eNtEr", "Enter")]
    [InlineData("ARROWLEFT", "ArrowLeft")]
    [InlineData("aRrOwRiGhT", "ArrowRight")]
    [InlineData("arrowup", "ArrowUp")]
    [InlineData("TAB", "Tab")]
    [InlineData("f12", "F12")]
    [InlineData("t", "T")]
    public void MixedCase_IsAccepted_AndNormalizedToTheCanonicalName(string spelling, string expected)
    {
        Assert.True(KeyVocabulary.TryGetCanonicalName(spelling, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [MemberData(nameof(RejectedNames))]
    public void NamesOutsideTheVocabulary_AreRejected(string name)
    {
        Assert.False(KeyVocabulary.TryGetCanonicalName(name, out var canonical));
        Assert.Equal(string.Empty, canonical);
    }

    [Fact]
    public void ANullName_IsRejectedWithoutThrowing()
    {
        Assert.False(KeyVocabulary.TryGetCanonicalName(null, out _));
    }

    [Fact]
    public void TheComparison_DoesNotDependOnTheCulture()
    {
        // Con la cultura turca, "i" en mayúscula es "İ": una comparación sensible a la cultura fallaría con "I" e "i".
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            Assert.True(KeyVocabulary.TryGetCanonicalName("i", out var lower));
            Assert.True(KeyVocabulary.TryGetCanonicalName("I", out var upper));
            Assert.True(KeyVocabulary.TryGetCanonicalName("ARROWLEFT", out var arrow));
            Assert.Equal(("I", "I", "ArrowLeft"), (lower, upper, arrow));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TheDescription_IsBuiltFromTheSameDefinition()
    {
        Assert.Equal("A–Z, 0–9, F1–F24, Enter, Tab, ArrowLeft, ArrowUp, ArrowRight, ArrowDown", KeyVocabulary.Description);
    }
}
