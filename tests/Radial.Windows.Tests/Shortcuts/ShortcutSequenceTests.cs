using Radial.Core.Model;
using Radial.Windows.Shortcuts;

namespace Radial.Windows.Tests.Shortcuts;

public class ShortcutSequenceTests
{
    private const ushort Ctrl = 0xA2;
    private const ushort Shift = 0xA0;
    private const ushort Alt = 0xA4;
    private const ushort Win = 0x5B;

    private static IReadOnlyList<KeyboardEvent> Build(KeyModifiers modifiers, string key)
    {
        Assert.True(ShortcutParser.TryParse(new KeyChord(modifiers, key), out var shortcut));
        return ShortcutSequence.Build(shortcut);
    }

    private static string Describe(IEnumerable<KeyboardEvent> events) =>
        string.Join(" ", events.Select(e => $"{(e.KeyUp ? '-' : '+')}{e.Code:X2}"));

    [Fact]
    public void CtrlC_IsCtrlDown_C_Down_C_Up_CtrlUp() =>
        Assert.Equal("+A2 +43 -43 -A2", Describe(Build(KeyModifiers.Control, "C")));

    [Fact]
    public void CtrlShiftS_PressesTheModifiersInFixedOrder_AndReleasesThemInReverse() =>
        Assert.Equal("+A2 +A0 +53 -53 -A0 -A2", Describe(Build(KeyModifiers.Control | KeyModifiers.Shift, "S")));

    [Fact]
    public void AltTab_UsesTheLeftAltAndTheTabKey() =>
        Assert.Equal("+A4 +09 -09 -A4", Describe(Build(KeyModifiers.Alt, "Tab")));

    [Fact]
    public void WinE_UsesTheLeftWindowsKeyMarkedAsExtended()
    {
        var events = Build(KeyModifiers.Windows, "E");

        Assert.Equal("+5B +45 -45 -5B", Describe(events));
        Assert.True(events[0].Extended);
        Assert.True(events[3].Extended);
        Assert.False(events[1].Extended);
    }

    [Fact]
    public void AllFourModifiers_AreHandledInTheSameWay() =>
        Assert.Equal(
            "+A2 +A0 +A4 +5B +74 -74 -5B -A4 -A0 -A2",
            Describe(Build(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Windows, "F5")));

    [Fact]
    public void WithoutModifiers_OnlyTheKeyIsSent() =>
        Assert.Equal("+0D -0D", Describe(Build(KeyModifiers.None, "Enter")));

    [Fact]
    public void ArrowKeys_AreSentAsExtended()
    {
        var events = Build(KeyModifiers.Control, "ArrowLeft");

        Assert.Equal("+A2 +25 -25 -A2", Describe(events));
        Assert.True(events[1].Extended);
        Assert.True(events[2].Extended);
    }

    [Fact]
    public void TheKeyCodes_AreTheOnesOfTheModifiers_NotOfTheirRightHandCounterparts()
    {
        var all = Build(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Windows, "A").Select(e => e.Code);

        Assert.Contains(Ctrl, all);
        Assert.Contains(Shift, all);
        Assert.Contains(Alt, all);
        Assert.Contains(Win, all);
    }

    // Todo atajo válido produce una secuencia equilibrada: nunca deja nada pulsado.

    public static TheoryData<KeyModifiers, string> EveryCombination()
    {
        var data = new TheoryData<KeyModifiers, string>();
        foreach (var modifiers in Enumerable.Range(0, 16).Select(i => (KeyModifiers)i))
        {
            foreach (var key in new[] { "A", "9", "F24", "Enter", "Tab", "ArrowDown" })
            {
                data.Add(modifiers, key);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void EverySequence_IsBalanced_AndLeavesNoKeyDown(KeyModifiers modifiers, string key)
    {
        var events = Build(modifiers, key);
        var down = new HashSet<ushort>();

        foreach (var e in events)
        {
            if (e.KeyUp)
            {
                Assert.True(down.Remove(e.Code), $"se soltó {e.Code:X2} sin haberse pulsado");
            }
            else
            {
                Assert.True(down.Add(e.Code), $"se pulsó {e.Code:X2} dos veces");
            }
        }

        Assert.Empty(down);
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void TheKeyIsAlwaysPressedWhileEveryModifierIsHeld(KeyModifiers modifiers, string key)
    {
        var events = Build(modifiers, key);
        var modifierCodes = new[] { Ctrl, Shift, Alt, Win };
        var keyDown = events.ToList().FindIndex(e => !e.KeyUp && !modifierCodes.Contains(e.Code));

        var heldBefore = events.Take(keyDown).Select(e => e.Code).ToList();
        var releasedAfter = events.Skip(keyDown + 2).Select(e => e.Code).ToList();
        Assert.Equal(heldBefore.OrderBy(c => c), releasedAfter.OrderBy(c => c));
        Assert.Equal(heldBefore.Count, ((int)modifiers & 1) + ((int)modifiers >> 1 & 1) + ((int)modifiers >> 2 & 1) + ((int)modifiers >> 3 & 1));
    }

    // Liberación de lo ya enviado

    [Fact]
    public void ReleasesFor_ASequenceCutAfterThePressedModifiers_ReleasesThemInReverse()
    {
        var sent = Build(KeyModifiers.Control | KeyModifiers.Shift, "S").Take(2).ToList(); // +Ctrl +Shift

        Assert.Equal("-A0 -A2", Describe(ShortcutSequence.ReleasesFor(sent)));
    }

    [Fact]
    public void ReleasesFor_ASequenceCutWhileTheKeyIsDown_ReleasesTheKeyAndTheModifiers()
    {
        var sent = Build(KeyModifiers.Control, "C").Take(2).ToList(); // +Ctrl +C

        Assert.Equal("-43 -A2", Describe(ShortcutSequence.ReleasesFor(sent)));
    }

    [Fact]
    public void ReleasesFor_ASequenceCutAfterTheKeyWasReleased_ReleasesOnlyTheModifiers()
    {
        var sent = Build(KeyModifiers.Control, "C").Take(3).ToList(); // +Ctrl +C -C

        Assert.Equal("-A2", Describe(ShortcutSequence.ReleasesFor(sent)));
    }

    [Fact]
    public void ReleasesFor_ACompleteOrEmptySequence_HasNothingToRelease()
    {
        Assert.Empty(ShortcutSequence.ReleasesFor(Build(KeyModifiers.Control, "C")));
        Assert.Empty(ShortcutSequence.ReleasesFor([]));
    }

    [Fact]
    public void ReleasesFor_KeepsTheExtendedFlag()
    {
        var sent = Build(KeyModifiers.Windows, "E").Take(1).ToList();

        var release = Assert.Single(ShortcutSequence.ReleasesFor(sent));
        Assert.True(release.Extended);
        Assert.True(release.KeyUp);
    }
}
