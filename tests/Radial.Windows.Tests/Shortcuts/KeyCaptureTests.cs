using Radial.Core.Model;
using Radial.Windows.Shortcuts;

namespace Radial.Windows.Tests.Shortcuts;

/// <summary>
/// La tabla de teclas ampliada (código de tecla virtual de cada nombre y el camino de vuelta), la lógica de grabación de un atajo y
/// los nombres de las teclas según la distribución del teclado (con una distribución española simulada: las pruebas no dependen del
/// teclado del equipo).
/// </summary>
public class KeyCaptureTests
{
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12;

    // Tabla de códigos

    [Theory]
    [InlineData("Space", 0x20, false)]
    [InlineData("Escape", 0x1B, false)]
    [InlineData("Backspace", 0x08, false)]
    [InlineData("Delete", 0x2E, true)]
    [InlineData("Insert", 0x2D, true)]
    [InlineData("Home", 0x24, true)]
    [InlineData("End", 0x23, true)]
    [InlineData("PageUp", 0x21, true)]
    [InlineData("PageDown", 0x22, true)]
    [InlineData("Numpad0", 0x60, false)]
    [InlineData("Numpad9", 0x69, false)]
    [InlineData("NumpadMultiply", 0x6A, false)]
    [InlineData("NumpadAdd", 0x6B, false)]
    [InlineData("NumpadSubtract", 0x6D, false)]
    [InlineData("NumpadDecimal", 0x6E, false)]
    [InlineData("NumpadDivide", 0x6F, true)]
    [InlineData("NumpadEnter", 0x0D, true)]
    [InlineData("PrintScreen", 0x2C, true)]
    [InlineData("Pause", 0x13, false)]
    [InlineData("ContextMenu", 0x5D, true)]
    [InlineData("Oem1", 0xBA, false)]
    [InlineData("OemPlus", 0xBB, false)]
    [InlineData("OemComma", 0xBC, false)]
    [InlineData("OemMinus", 0xBD, false)]
    [InlineData("OemPeriod", 0xBE, false)]
    [InlineData("Oem2", 0xBF, false)]
    [InlineData("Oem3", 0xC0, false)]
    [InlineData("Oem4", 0xDB, false)]
    [InlineData("Oem5", 0xDC, false)]
    [InlineData("Oem6", 0xDD, false)]
    [InlineData("Oem7", 0xDE, false)]
    [InlineData("Oem8", 0xDF, false)]
    [InlineData("Oem102", 0xE2, false)]
    [InlineData("MediaPlayPause", 0xB3, false)]
    [InlineData("MediaNextTrack", 0xB0, false)]
    [InlineData("MediaPreviousTrack", 0xB1, false)]
    [InlineData("MediaStop", 0xB2, false)]
    [InlineData("VolumeUp", 0xAF, false)]
    [InlineData("VolumeDown", 0xAE, false)]
    [InlineData("VolumeMute", 0xAD, false)]
    [InlineData("BrowserBack", 0xA6, false)]
    [InlineData("BrowserForward", 0xA7, false)]
    [InlineData("BrowserRefresh", 0xA8, false)]
    [InlineData("BrowserHome", 0xAC, false)]
    public void TheNewKeys_HaveTheirWindowsVirtualKeyCode(string name, int code, bool extended)
    {
        Assert.True(KeyboardKeys.TryGetVirtualKey(name, out var actualCode, out var actualExtended));

        Assert.Equal(code, actualCode);
        Assert.Equal(extended, actualExtended);
    }

    [Fact]
    public void EveryKey_IsTranslatedToACodeAndBackToItsName()
    {
        foreach (var name in KeyVocabulary.Names)
        {
            Assert.True(KeyboardKeys.TryGetVirtualKey(name, out var code, out var extended), name);
            Assert.True(KeyboardKeys.TryGetName(code, extended, out var back), name);
            Assert.Equal(name, back);
        }
    }

    [Fact]
    public void EnterAndTheNumericEnter_ShareACode_AndTheExtendedFlagTellsThemApart()
    {
        Assert.True(KeyboardKeys.TryGetName(0x0D, null, out var unknown));
        Assert.True(KeyboardKeys.TryGetName(0x0D, false, out var normal));
        Assert.True(KeyboardKeys.TryGetName(0x0D, true, out var numeric));

        Assert.Equal("Enter", unknown);
        Assert.Equal("Enter", normal);
        Assert.Equal("NumpadEnter", numeric);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0xFF)]
    [InlineData(0x100)]
    [InlineData(0x14)] // Bloq Mayús
    [InlineData(0x90)] // Bloq Num
    [InlineData(VkControl)]
    [InlineData(VkShift)]
    [InlineData(0xA2)]
    public void ACodeThatIsNotAKeyOfTheVocabulary_HasNoName(int code)
    {
        Assert.False(KeyboardKeys.TryGetName(code, null, out var name));
        Assert.Equal(string.Empty, name);
    }

    [Fact]
    public void AnUnknownName_HasNoCode()
    {
        Assert.False(KeyboardKeys.TryGetVirtualKey("Ñ", out var code, out var extended));
        Assert.Equal(0, code);
        Assert.False(extended);
        Assert.False(KeyboardKeys.TryGetVirtualKey(null, out _, out _));
    }

    // Grabación de un atajo

    private static KeyChord Captured(KeyCaptureOutcome outcome) => Assert.IsType<KeyCaptured>(outcome).Chord;

    [Theory]
    [InlineData(VkControl)]
    [InlineData(VkShift)]
    [InlineData(VkMenu)]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    [InlineData(0xA0)]
    [InlineData(0xA1)]
    [InlineData(0xA2)]
    [InlineData(0xA3)]
    [InlineData(0xA4)]
    [InlineData(0xA5)]
    public void PressingOnlyAModifier_KeepsWaiting(int code)
    {
        Assert.IsType<KeyCaptureWaiting>(KeyCapture.Evaluate(code, KeyModifiers.Control));
        Assert.True(KeyCapture.IsModifier(code));
    }

    [Fact]
    public void ALetterWithModifiers_IsCaptured_WithTheModifiersThatWerePressed()
    {
        var chord = Captured(KeyCapture.Evaluate('S', KeyModifiers.Control | KeyModifiers.Shift));

        Assert.Equal(new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, "S"), chord);
    }

    [Theory]
    [InlineData(0x70, "F1")]
    [InlineData(0x87, "F24")]
    [InlineData(0x1B, "Escape")]
    [InlineData(0x20, "Space")]
    [InlineData(0x2E, "Delete")]
    [InlineData(0x60, "Numpad0")]
    [InlineData(0xB3, "MediaPlayPause")]
    [InlineData(0x30, "0")]
    public void OtherKeysOfTheVocabulary_AreCaptured_AlsoWithoutModifiers(int code, string expected)
    {
        Assert.Equal(new KeyChord(KeyModifiers.None, expected), Captured(KeyCapture.Evaluate(code, KeyModifiers.None)));
    }

    [Fact]
    public void TheSpanishNKey_IsCapturedByItsVirtualKey_NotByTheCharacter()
    {
        // En un teclado español la Ñ es VK_OEM_3 (0xC0): se guarda «Oem3», que es la misma tecla física con cualquier distribución.
        var chord = Captured(KeyCapture.Evaluate(0xC0, KeyModifiers.Control));

        Assert.Equal("Oem3", chord.Key);
        Assert.Empty(new SendKeysAction(chord).GetProblems());
    }

    [Fact]
    public void TheNumericEnter_IsCaptured_WhenTheKeyIsKnownToBeExtended()
    {
        Assert.Equal("NumpadEnter", Captured(KeyCapture.Evaluate(0x0D, KeyModifiers.None, extended: true)).Key);
        Assert.Equal("Enter", Captured(KeyCapture.Evaluate(0x0D, KeyModifiers.None, extended: false)).Key);
        Assert.Equal("Enter", Captured(KeyCapture.Evaluate(0x0D, KeyModifiers.None)).Key);
    }

    [Fact]
    public void CtrlAltDelete_IsRejected_AndExplained()
    {
        var outcome = KeyCapture.Evaluate(0x2E, KeyModifiers.Control | KeyModifiers.Alt);

        var rejected = Assert.IsType<KeyCaptureRejected>(outcome);
        Assert.Contains("Ctrl+Alt+Supr", rejected.Reason);
    }

    [Theory]
    [InlineData(0x14, "bloqueo")]
    [InlineData(0x90, "bloqueo")]
    [InlineData(0x91, "bloqueo")]
    [InlineData(0xE5, "método de entrada")]
    [InlineData(0xFC, "no está admitida")]
    [InlineData(0x07, "no está admitida")]
    public void KeysThatCannotBeUsed_AreRejected_WithAReason(int code, string reasonFragment)
    {
        var rejected = Assert.IsType<KeyCaptureRejected>(KeyCapture.Evaluate(code, KeyModifiers.Control));

        Assert.Contains(reasonFragment, rejected.Reason);
    }

    [Fact]
    public void EveryCapturedCombination_IsValidAndTranslatable()
    {
        for (var code = 1; code < 0xFF; code++)
        {
            if (KeyCapture.Evaluate(code, KeyModifiers.Control) is KeyCaptured captured)
            {
                var action = new SendKeysAction(captured.Chord);
                Assert.Empty(action.GetProblems());
                Assert.True(ShortcutParser.TryParse(captured.Chord, out _), captured.Chord.Key);
            }
        }
    }

    // Nombres según la distribución

    private sealed class SpanishLayout : IKeyLabelProvider
    {
        public string LayoutName => "español (España)";

        public string? GetLabel(string keyName) => keyName switch
        {
            "Oem3" => "Ñ",
            "Oem1" => "`",
            "Oem2" => "Ç",
            "Oem4" => "'",
            "Oem5" => "º",
            "Oem6" => "¡",
            "Oem7" => "´",
            "Oem102" => "<",
            "Delete" => "Supr",
            "Space" => "Espacio",
            _ => null,
        };
    }

    private sealed class UnitedStatesLayout : IKeyLabelProvider
    {
        public string LayoutName => "inglés (Estados Unidos)";

        public string? GetLabel(string keyName) => keyName switch
        {
            "Oem3" => "`",
            "Oem1" => ";",
            "Oem2" => "/",
            "Oem4" => "[",
            "Oem6" => "]",
            "Oem7" => "'",
            "Delete" => "Del",
            _ => null,
        };
    }

    [Fact]
    public void ACapturedKey_IsShownWithTheNameOfTheActiveLayout()
    {
        var chord = Captured(KeyCapture.Evaluate(0xC0, KeyModifiers.Control | KeyModifiers.Shift));

        Assert.Equal("Ctrl + Mayús + Ñ", KeyChordText.Format(chord, new SpanishLayout().GetLabel));
        Assert.Equal("Ctrl + Mayús + `", KeyChordText.Format(chord, new UnitedStatesLayout().GetLabel));
    }

    [Fact]
    public void TheSpanishPunctuationKeys_AreAllInTheVocabulary_ByTheirVirtualKeys()
    {
        // Las teclas de signos de un teclado español, por su tecla virtual (no por el carácter, que depende de la distribución).
        var layout = new SpanishLayout();
        foreach (var name in new[] { "Oem1", "Oem2", "Oem3", "Oem4", "Oem5", "Oem6", "Oem7", "Oem102" })
        {
            Assert.True(KeyVocabulary.TryGetCanonicalName(name, out _), name);
            Assert.False(string.IsNullOrEmpty(layout.GetLabel(name)), name);
        }
    }

    [Fact]
    public void TheRealLayoutProvider_NeverThrows_AndKnowsNothingAboutUnknownKeys()
    {
        var provider = new Win32KeyLabelProvider();

        Assert.Null(provider.GetLabel("NoExiste"));
        Assert.Null(provider.GetLabel(string.Empty));
        Assert.Null(provider.GetLabel("MediaPlayPause")); // sin nombre propio en Windows: se usa el de respaldo
        Assert.False(string.IsNullOrWhiteSpace(provider.LayoutName));
        foreach (var name in KeyVocabulary.Names)
        {
            var label = provider.GetLabel(name);
            Assert.True(label is null || label.Length > 0, name);
        }
    }
}
