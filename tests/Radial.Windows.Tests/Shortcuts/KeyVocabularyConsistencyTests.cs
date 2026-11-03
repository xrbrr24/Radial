using Radial.Core.Model;
using Radial.Windows.Shortcuts;

namespace Radial.Windows.Tests.Shortcuts;

/// <summary>
/// Radial.Core decide QUÉ nombres de tecla existen (<see cref="KeyVocabulary"/>) y Radial.Windows solo les asigna código VK.
/// Estas pruebas demuestran que no hay dos listas que puedan separarse: lo que valida una acción es siempre ejecutable.
/// </summary>
public class KeyVocabularyConsistencyTests
{
    private static IEnumerable<string?> Candidates()
    {
        // Todo lo aprobado en varias grafías, más lo explícitamente excluido, más todos los caracteres sueltos de Latin-1 y
        // Latin Extended-A (letras con acento, Ñ, símbolos, controles) y variantes con espacios.
        foreach (var name in KeyVocabulary.Names)
        {
            yield return name;
            yield return name.ToLowerInvariant();
            yield return name.ToUpperInvariant();
            yield return " " + name;
            yield return name + " ";
            yield return name + name;
        }

        foreach (var name in new[]
                 {
                     "Space", "Delete", "Escape", "Backspace", "Ñ", "ñ", "Numpad0", "NumpadEnter", "Return", "Left", "F0", "F25",
                     "F01", "Unknown", "", " ", "K", "ſ", "ı", "Ｆ１", "Arrow Left", "OEM_PLUS",
                 })
        {
            yield return name;
        }

        for (var code = 0; code <= 0x17F; code++)
        {
            yield return ((char)code).ToString();
        }

        yield return null;
    }

    [Fact]
    public void TheParserSupportsExactlyTheNamesOfCore()
    {
        var parserNames = ShortcutParser.SupportedKeys.Select(k => k.Name).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(66, parserNames.Count);
        Assert.Equal(KeyVocabulary.Names.Order(StringComparer.Ordinal), parserNames);
    }

    [Fact]
    public void EveryNameOfCore_HasAVirtualKey_InAnyCase()
    {
        foreach (var name in KeyVocabulary.Names)
        {
            foreach (var spelling in new[] { name, name.ToLowerInvariant(), name.ToUpperInvariant() })
            {
                Assert.True(ShortcutParser.TryParseKey(spelling, out var key), $"'{spelling}' está en Core pero el parser no lo traduce.");
                Assert.Equal(name, key.Name);
            }
        }
    }

    [Fact]
    public void TheParserAcceptsAName_IfAndOnlyIfCoreDoes()
    {
        var checkedCandidates = 0;
        foreach (var candidate in Candidates())
        {
            var core = KeyVocabulary.TryGetCanonicalName(candidate, out var canonical);
            var parser = ShortcutParser.TryParseKey(candidate, out var key);

            Assert.True(core == parser, $"Core y el parser discrepan con '{candidate}' (Core: {core}, parser: {parser}).");
            if (core)
            {
                Assert.Equal(canonical, key.Name);
            }

            checkedCandidates++;
        }

        Assert.True(checkedCandidates > 500); // evita una prueba vacua si el generador cambia
    }

    [Fact]
    public void AnActionThatPassesValidation_CanAlwaysBeTranslated_AndViceVersa()
    {
        foreach (var candidate in Candidates())
        {
            var chord = new KeyChord(KeyModifiers.Control, candidate!);
            var validates = !new SendKeysAction(chord).GetProblems().Any();
            var translates = ShortcutParser.TryParse(chord, out _);

            Assert.True(validates == translates, $"'{candidate}': valida={validates}, se traduce={translates}.");
        }
    }

    [Theory]
    [InlineData("Space")]
    [InlineData("Delete")]
    [InlineData("Escape")]
    [InlineData("Ñ")]
    public void TheKeysThatUsedToValidateButCouldNotRun_AreNowRejectedByBoth(string name)
    {
        var chord = new KeyChord(KeyModifiers.None, name);

        Assert.NotEmpty(new SendKeysAction(chord).GetProblems());
        Assert.False(ShortcutParser.TryParse(chord, out _));
    }
}
