using Radial.Core.Model;
using Radial.Windows.Presentation;

namespace Radial.Windows.Tests.Presentation;

/// <summary>
/// El selector de atajos: grabar solo mientras se activa el modo, rechazar con explicación lo no utilizable, elegir a mano y mostrar
/// los nombres de las teclas según el teclado activo. Sin teclado real: se le pasan códigos de tecla virtual.
/// </summary>
public class KeyCaptureViewModelTests
{
    private const int VkOem3 = 0xC0; // la tecla de la Ñ en un teclado español
    private const int VkCapsLock = 0x14;
    private const int VkS = 0x53;
    private const int VkDelete = 0x2E;
    private const int VkControl = 0x11;

    private static KeyCaptureViewModel Create() => new(new SpanishLabels());

    [Fact]
    public void WhileNotRecording_KeysAreIgnored_AndNothingIsCaptured()
    {
        var capture = Create();

        var consumed = capture.HandleKey(VkS, KeyModifiers.Control);

        Assert.False(consumed);
        Assert.Null(capture.Chord);
        Assert.False(capture.IsRecording);
    }

    [Fact]
    public void RecordingACombination_StoresTheStableName_NotTheCharacter()
    {
        var capture = Create();
        capture.StartRecording();

        var consumed = capture.HandleKey(VkOem3, KeyModifiers.Control);

        Assert.True(consumed);
        Assert.False(capture.IsRecording); // la grabación termina al completarse
        Assert.Equal(new KeyChord(KeyModifiers.Control, "Oem3"), capture.Chord);
        Assert.Equal("Ctrl + Ñ", capture.Display); // se muestra con el nombre del teclado activo
    }

    [Fact]
    public void ModifiersAlone_KeepWaiting_AndShowWhatIsHeld()
    {
        var capture = Create();
        capture.StartRecording();

        capture.HandleKey(VkControl, KeyModifiers.Control);

        Assert.True(capture.IsRecording);
        Assert.Null(capture.Chord);
        Assert.Equal("Ctrl + …", capture.Display);
    }

    [Fact]
    public void AnUnusableKey_IsRejectedWithAnExplanation_AndNothingIsSaved()
    {
        var capture = Create();
        capture.StartRecording();

        capture.HandleKey(VkCapsLock, KeyModifiers.None);

        Assert.True(capture.IsRecording);
        Assert.Null(capture.Chord);
        Assert.NotEmpty(capture.Message);
    }

    [Fact]
    public void ARecordedCombination_RaisesTheChangeEvent()
    {
        var capture = Create();
        var raised = 0;
        capture.ChordChanged += () => raised++;
        capture.StartRecording();

        capture.HandleKey(VkS, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void StoppingTheRecording_LeavesTheCombinationUntouched()
    {
        var capture = Create();
        capture.Load(new KeyChord(KeyModifiers.Alt, "F4"));
        capture.StartRecording();

        capture.StopRecording();

        Assert.False(capture.IsRecording);
        Assert.Equal(new KeyChord(KeyModifiers.Alt, "F4"), capture.Chord);
    }

    [Fact]
    public void AReservedCombination_IsRejected_WhenRecorded_AndWhenChosenByHand()
    {
        var recorded = Create();
        recorded.StartRecording();
        recorded.HandleKey(VkDelete, KeyModifiers.Control | KeyModifiers.Alt);

        Assert.Null(recorded.Chord);
        Assert.NotEmpty(recorded.Message);

        var manual = Create();
        manual.Control = true;
        manual.Alt = true;
        manual.KeyName = "Delete";

        Assert.Null(manual.Chord);
        Assert.NotEmpty(manual.Message);
    }

    [Fact]
    public void ChoosingByHand_BuildsTheCombination_AndNotifies()
    {
        var capture = Create();
        var raised = 0;
        capture.ChordChanged += () => raised++;

        capture.Windows = true;
        capture.KeyName = "E";

        Assert.Equal(new KeyChord(KeyModifiers.Windows, "E"), capture.Chord);
        Assert.Equal("Win + E", capture.Display);
        Assert.Equal(string.Empty, capture.Message);
        Assert.True(raised >= 2);
    }

    [Fact]
    public void Clear_RemovesTheCombination()
    {
        var capture = Create();
        capture.Load(new KeyChord(KeyModifiers.Control, "S"));

        capture.Clear();

        Assert.Null(capture.Chord);
        Assert.Equal("Sin combinación", capture.Display);
    }

    [Fact]
    public void Load_DoesNotRaiseChanges_AndShowsTheLoadedCombination()
    {
        var capture = Create();
        var raised = 0;
        capture.ChordChanged += () => raised++;

        capture.Load(new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, "Oem3"));

        Assert.Equal(0, raised);
        Assert.Equal("Ctrl + Mayús + Ñ", capture.Display);
    }

    [Fact]
    public void TheKeyList_UsesTheNamesOfTheActiveLayout_AndCoversTheWholeVocabulary()
    {
        var capture = Create();

        Assert.Equal(KeyVocabulary.Names.Count, capture.Options.Count);
        Assert.Equal("Ñ", capture.Options.Single(o => o.Name == "Oem3").Label);
        Assert.Equal("español (prueba)", capture.LayoutName);
    }
}
