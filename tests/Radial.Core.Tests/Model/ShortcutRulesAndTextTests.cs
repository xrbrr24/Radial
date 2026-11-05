using Radial.Core.Model;

namespace Radial.Core.Tests.Model;

/// <summary>Combinaciones que no se pueden enviar y texto de una combinación (con la distribución activa simulada).</summary>
public class ShortcutRulesAndTextTests
{
    private static SendKeysAction Action(string key, KeyModifiers modifiers) => new(new KeyChord(modifiers, key));

    // Reglas de combinaciones

    [Theory]
    [InlineData("Delete", KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData("delete", KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData("DELETE", KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)]
    [InlineData("Delete", KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Windows)]
    public void CtrlAltDelete_IsRejected_WithAReason(string key, KeyModifiers modifiers)
    {
        var problem = Assert.Single(Action(key, modifiers).GetProblems());

        Assert.Contains("Ctrl+Alt+Supr", problem);
        Assert.NotNull(ShortcutRules.GetProblem(new KeyChord(modifiers, key)));
    }

    [Theory]
    [InlineData("Delete", KeyModifiers.None)]
    [InlineData("Delete", KeyModifiers.Control)]
    [InlineData("Delete", KeyModifiers.Alt)]
    [InlineData("Delete", KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Escape", KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Tab", KeyModifiers.Alt)]
    [InlineData("D", KeyModifiers.Windows)]
    public void OtherCombinations_AreAccepted(string key, KeyModifiers modifiers)
    {
        Assert.Empty(Action(key, modifiers).GetProblems());
        Assert.Null(ShortcutRules.GetProblem(new KeyChord(modifiers, key)));
    }

    [Fact]
    public void TheRules_DoNotJudgeWhatTheVocabularyAlreadyRejects()
    {
        Assert.Null(ShortcutRules.GetProblem(null));
        Assert.Null(ShortcutRules.GetProblem(new KeyChord(KeyModifiers.Control | KeyModifiers.Alt, "NoExiste")));
    }

    // Texto de una combinación

    /// <summary>Una distribución española simulada: lo que se ve en las teclas de signos (Oem3 es la Ñ).</summary>
    private static string? Spanish(string key) => key switch
    {
        "Oem3" => "Ñ",
        "Oem1" => "`",
        "Oem2" => "Ç",
        "OemPlus" => "+",
        "Delete" => "Supr",
        _ => null,
    };

    /// <summary>Una distribución estadounidense simulada.</summary>
    private static string? UnitedStates(string key) => key switch
    {
        "Oem3" => "`",
        "Oem1" => ";",
        "Oem2" => "/",
        _ => null,
    };

    [Fact]
    public void TheSameStoredKey_IsShownDifferentlyOnEachLayout()
    {
        var chord = new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, "Oem3");

        Assert.Equal("Ctrl + Mayús + Ñ", KeyChordText.Format(chord, Spanish));
        Assert.Equal("Ctrl + Mayús + `", KeyChordText.Format(chord, UnitedStates));
        Assert.Equal("Oem3", chord.Key); // lo guardado no cambia con la distribución
    }

    [Fact]
    public void WithoutALayout_TheFallbackTextOfTheVocabularyIsUsed()
    {
        Assert.Equal("Ctrl + Supr", KeyChordText.Format(new KeyChord(KeyModifiers.Control, "Delete")));
        Assert.Equal("Win + E", KeyChordText.Format(new KeyChord(KeyModifiers.Windows, "E")));
        Assert.Equal("Reproducir/Pausa", KeyChordText.Format(new KeyChord(KeyModifiers.None, "MediaPlayPause")));
    }

    [Fact]
    public void AnEmptyLabelFromTheLayout_FallsBackToTheVocabulary()
    {
        Assert.Equal("Supr", KeyChordText.KeyText("Delete", _ => " "));
        Assert.Equal("Supr", KeyChordText.KeyText("Delete", _ => null));
    }

    [Fact]
    public void ModifierNames_AreInAFixedOrder()
    {
        Assert.Equal(new[] { "Ctrl", "Mayús", "Alt", "Win" }, KeyChordText.ModifierNames(KeyModifiers.Windows | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Control).ToArray());
        Assert.Empty(KeyChordText.ModifierNames(KeyModifiers.None));
    }

    [Fact]
    public void ANullChord_HasNoText()
    {
        Assert.Equal(string.Empty, KeyChordText.Format(null));
        Assert.Empty(KeyChordText.SearchForms(null));
    }

    [Fact]
    public void TheSearchForms_IncludeTheStoredNameAndTheLayoutText()
    {
        var forms = KeyChordText.SearchForms(new KeyChord(KeyModifiers.Control, "Oem3"), Spanish);

        Assert.Contains("CtrlOem3", forms);
        Assert.Contains("CtrlÑ", forms);
    }
}
