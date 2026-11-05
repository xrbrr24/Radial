using System.Globalization;
using Radial.Core.Model;
using Radial.Windows.Shortcuts;

namespace Radial.Windows.Tests.Shortcuts;

/// <summary>Traducción de la tecla de un <see cref="KeyChord"/> a código virtual. Pura: no envía ni lee ninguna tecla.</summary>
public class ShortcutParserTests
{
    public static TheoryData<char> Letters => new(Enumerable.Range('A', 26).Select(c => (char)c));

    public static TheoryData<char> Digits => new(Enumerable.Range('0', 10).Select(c => (char)c));

    public static TheoryData<int> FunctionNumbers => new(Enumerable.Range(1, 24));

    private static VirtualKey Parse(string name)
    {
        Assert.True(ShortcutParser.TryParseKey(name, out var key), $"'{name}' debería pertenecer al vocabulario.");
        return key;
    }

    // Letras

    [Theory]
    [MemberData(nameof(Letters))]
    public void Letters_MapToTheirVirtualKey_InAnyCase(char letter)
    {
        // VK_A..VK_Z valen lo mismo que el carácter ASCII en mayúscula (VK_A = 0x41).
        foreach (var spelling in new[] { letter.ToString(), char.ToLowerInvariant(letter).ToString() })
        {
            var key = Parse(spelling);

            Assert.Equal((ushort)letter, key.Code);
            Assert.Equal(letter.ToString(), key.Name);
            Assert.False(key.Extended);
        }
    }

    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("m", 0x4D)]
    [InlineData("T", 0x54)]
    [InlineData("z", 0x5A)]
    public void Letters_MatchKnownWindowsCodes(string name, int code)
    {
        Assert.Equal((ushort)code, Parse(name).Code);
    }

    // Dígitos

    [Theory]
    [MemberData(nameof(Digits))]
    public void Digits_MapToTheTopRowKeys(char digit)
    {
        var key = Parse(digit.ToString());

        Assert.Equal((ushort)digit, key.Code); // VK_0..VK_9 = 0x30..0x39
        Assert.Equal(digit.ToString(), key.Name);
        Assert.False(key.Extended);
    }

    [Theory]
    [InlineData("0", 0x30)]
    [InlineData("5", 0x35)]
    [InlineData("9", 0x39)]
    public void Digits_MatchKnownWindowsCodes(string name, int code)
    {
        Assert.Equal((ushort)code, Parse(name).Code);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("٣")] // dígito arábigo-índico
    [InlineData("５")] // dígito de ancho completo
    [InlineData("²")]
    public void Digits_OutsideTheTopRowAreRejected(string name)
    {
        Assert.False(ShortcutParser.TryParseKey(name, out _));
    }

    // Teclas de función

    [Theory]
    [MemberData(nameof(FunctionNumbers))]
    public void FunctionKeys_F1ToF24_MapInSequence(int number)
    {
        var key = Parse($"F{number}");

        Assert.Equal((ushort)(0x70 + number - 1), key.Code); // VK_F1 = 0x70 ... VK_F24 = 0x87
        Assert.Equal($"F{number}", key.Name);
        Assert.False(key.Extended);
    }

    [Theory]
    [InlineData("F1", 0x70)]
    [InlineData("F5", 0x74)]
    [InlineData("F12", 0x7B)]
    [InlineData("F13", 0x7C)]
    [InlineData("f24", 0x87)]
    public void FunctionKeys_MatchKnownWindowsCodes(string name, int code)
    {
        Assert.Equal((ushort)code, Parse(name).Code);
    }

    [Theory]
    [InlineData("F0")]
    [InlineData("F25")]
    [InlineData("F100")]
    [InlineData("F01")]
    [InlineData("F-1")]
    [InlineData("F 1")]
    [InlineData("F1.0")]
    [InlineData("１")]
    [InlineData("Ｆ１")]
    public void FunctionKeys_OutsideF1ToF24AreRejected(string name)
    {
        Assert.False(ShortcutParser.TryParseKey(name, out _));
    }

    // Teclas especiales

    [Theory]
    [InlineData("Enter", 0x0D)] // VK_RETURN
    [InlineData("Tab", 0x09)]   // VK_TAB
    public void SpecialKeys_MapAndAreNotExtended(string name, int code)
    {
        var key = Parse(name);

        Assert.Equal((ushort)code, key.Code);
        Assert.Equal(name, key.Name);
        Assert.False(key.Extended);
    }

    // Flechas

    [Theory]
    [InlineData("ArrowLeft", 0x25)]  // VK_LEFT
    [InlineData("ArrowUp", 0x26)]    // VK_UP
    [InlineData("ArrowRight", 0x27)] // VK_RIGHT
    [InlineData("ArrowDown", 0x28)]  // VK_DOWN
    public void Arrows_MapAndAreAllExtended(string name, int code)
    {
        var key = Parse(name);

        Assert.Equal((ushort)code, key.Code);
        Assert.Equal(name, key.Name);
        Assert.True(key.Extended);
    }

    [Fact]
    public void TheExtendedKeys_AreExactlyTheNavigationBlock_TheNumpadSlashAndEnter_PrintScreenAndTheMenuKey()
    {
        var extended = ShortcutParser.SupportedKeys.Where(k => k.Extended).Select(k => k.Name).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(
            new[] { "ArrowDown", "ArrowLeft", "ArrowRight", "ArrowUp", "ContextMenu", "Delete", "End", "Home", "Insert", "NumpadDivide", "NumpadEnter", "PageDown", "PageUp", "PrintScreen" },
            extended);
    }

    // Mayúsculas y minúsculas, nombre canónico

    [Theory]
    [InlineData("enter", "Enter")]
    [InlineData("ENTER", "Enter")]
    [InlineData("eNtEr", "Enter")]
    [InlineData("tab", "Tab")]
    [InlineData("TAB", "Tab")]
    [InlineData("arrowleft", "ArrowLeft")]
    [InlineData("ARROWUP", "ArrowUp")]
    [InlineData("aRrOwRiGhT", "ArrowRight")]
    [InlineData("arrowdown", "ArrowDown")]
    [InlineData("f10", "F10")]
    [InlineData("t", "T")]
    public void Names_AreCaseInsensitive_AndAlwaysReturnTheCanonicalName(string spelling, string canonical)
    {
        var key = Parse(spelling);

        Assert.Equal(canonical, key.Name);
        Assert.Equal(Parse(canonical), key);
    }

    [Fact]
    public void EveryCanonicalName_ParsesToItself_InAnyCase()
    {
        foreach (var supported in ShortcutParser.SupportedKeys)
        {
            Assert.Equal(supported, Parse(supported.Name));
            Assert.Equal(supported, Parse(supported.Name.ToLowerInvariant()));
            Assert.Equal(supported, Parse(supported.Name.ToUpperInvariant()));
        }
    }

    [Fact]
    public void Parsing_DoesNotDependOnTheCulture()
    {
        // Con la cultura turca, "i" en mayúscula es "İ": una comparación sensible a la cultura fallaría con "I" e "i".
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            Assert.Equal((ushort)0x49, Parse("i").Code);
            Assert.Equal((ushort)0x49, Parse("I").Code);
            Assert.Equal("ArrowLeft", Parse("ARROWLEFT").Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // Vocabulario cerrado

    [Fact]
    public void TheVocabulary_IsExactlyTheApprovedOne()
    {
        var actual = ShortcutParser.SupportedKeys.Select(k => k.Name).ToList();

        Assert.Equal(118, actual.Count);
        Assert.Equal(KeyVocabulary.Names.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NoTwoNames_ShareTheSameKey()
    {
        var keys = ShortcutParser.SupportedKeys.Select(k => (k.Code, k.Extended)).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Theory]
    [InlineData("Ñ")]
    [InlineData("ñ")]
    [InlineData("Á")]
    [InlineData("é")]
    [InlineData("Ａ")]      // letra de ancho completo
    [InlineData("K")]  // signo Kelvin: se parece a la K
    [InlineData("ſ")]  // 's' larga: algunas reglas de mayúsculas la igualan a la S
    [InlineData("ı")]  // 'i' sin punto
    public void NonAsciiLetters_AreRejected(string name)
    {
        Assert.False(ShortcutParser.TryParseKey(name, out _));
    }

    [Theory]
    [InlineData("!")]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData("=")]
    [InlineData(",")]
    [InlineData(".")]
    [InlineData(";")]
    [InlineData("'")]
    [InlineData("`")]
    [InlineData("[")]
    [InlineData("\\")]
    [InlineData("/")]
    [InlineData("<")]
    [InlineData("º")]
    public void SymbolsAndPunctuation_AreRejected(string name)
    {
        Assert.False(ShortcutParser.TryParseKey(name, out _));
    }

    [Theory]
    [InlineData("Esc")]
    [InlineData("Del")]
    [InlineData("CapsLock")]
    [InlineData("NumLock")]
    [InlineData("NumericEnter")]
    [InlineData("Return")]
    [InlineData("Left")]
    [InlineData("Right")]
    [InlineData("Up")]
    [InlineData("Down")]
    [InlineData("Arrow Left")]
    [InlineData("Arrow_Left")]
    [InlineData("ArrowLeft2")]
    [InlineData("Flecha")]
    [InlineData("Intro")]
    [InlineData("Tabulador")]
    [InlineData("Ctrl")]
    [InlineData("Control")]
    [InlineData("Shift")]
    [InlineData("Alt")]
    [InlineData("Win")]
    [InlineData("AA")]
    [InlineData("A+B")]
    [InlineData("OEM_PLUS")]
    [InlineData("Unknown")]
    public void NamesOutsideTheVocabulary_AreRejected(string name)
    {
        Assert.False(ShortcutParser.TryParseKey(name, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" A")]
    [InlineData("A ")]
    [InlineData(" Enter")]
    [InlineData("Enter ")]
    [InlineData("\tTab")]
    [InlineData("A\n")]
    [InlineData("A\0")]
    [InlineData("\0")]
    public void EmptyOrPaddedNames_AreRejected_NothingIsTrimmed(string name)
    {
        Assert.False(ShortcutParser.TryParseKey(name, out _));
    }

    [Fact]
    public void ANullKeyName_IsRejected()
    {
        Assert.False(ShortcutParser.TryParseKey(null, out _));
    }

    [Fact]
    public void ARejectedKey_LeavesTheDefaultValue()
    {
        ShortcutParser.TryParseKey("Esc", out var key);

        Assert.Equal(default, key);
    }

    // Atajo completo (KeyChord): los modificadores del modelo se conservan

    [Fact]
    public void AChord_KeepsItsModifiersUnchanged_AndTranslatesTheKey()
    {
        var chord = new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, "t");

        Assert.True(ShortcutParser.TryParse(chord, out var shortcut));

        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, shortcut.Modifiers);
        Assert.Equal(new VirtualKey("T", 0x54, false), shortcut.Key);
    }

    [Fact]
    public void EveryCombinationOfModifiers_IsKeptExactly()
    {
        // 0..15 son todas las combinaciones de Control, Shift, Alt y Windows (incluida ninguna).
        for (var bits = 0; bits <= 15; bits++)
        {
            var modifiers = (KeyModifiers)bits;

            Assert.True(ShortcutParser.TryParse(new KeyChord(modifiers, "ArrowDown"), out var shortcut));

            Assert.Equal(modifiers, shortcut.Modifiers);
            Assert.Equal(new VirtualKey("ArrowDown", 0x28, true), shortcut.Key);
        }
    }

    [Fact]
    public void Modifiers_AreNotReinterpretedOrValidated_EvenWithUnknownBits()
    {
        // Validar los modificadores es cosa de SendKeysAction.GetProblems; el parser no toca lo que no es la tecla.
        var odd = KeyModifiers.Control | (KeyModifiers)64;

        Assert.True(ShortcutParser.TryParse(new KeyChord(odd, "F5"), out var shortcut));

        Assert.Equal(odd, shortcut.Modifiers);
    }

    [Theory]
    [InlineData("Ñ")]
    [InlineData("Esc")]
    [InlineData("")]
    [InlineData(null)]
    public void AChordWithAnInvalidKey_IsRejected_WhateverItsModifiers(string? key)
    {
        Assert.False(ShortcutParser.TryParse(new KeyChord(KeyModifiers.Control, key!), out var shortcut));

        Assert.Equal(default, shortcut);
    }

    [Fact]
    public void ANullChord_IsRejected()
    {
        Assert.False(ShortcutParser.TryParse(null, out var shortcut));

        Assert.Equal(default, shortcut);
    }

    [Theory]
    [InlineData("T", 0x54, false)]
    [InlineData("F5", 0x74, false)]
    [InlineData("Enter", 0x0D, false)]
    [InlineData("Tab", 0x09, false)]
    public void TheKeyNamesUsedAsExamplesInTheModel_Parse(string name, int code, bool extended)
    {
        Assert.True(ShortcutParser.TryParse(new KeyChord(KeyModifiers.None, name), out var shortcut));

        Assert.Equal(new VirtualKey(name, (ushort)code, extended), shortcut.Key);
    }

    [Fact]
    public void ASendKeysActionWithAValidChord_IsTranslatedFromItsChord()
    {
        var action = new SendKeysAction(new KeyChord(KeyModifiers.Alt, "ArrowLeft"));

        Assert.Empty(action.GetProblems());
        Assert.True(ShortcutParser.TryParse(action.Chord, out var shortcut));
        Assert.Equal(KeyModifiers.Alt, shortcut.Modifiers);
    }
}
