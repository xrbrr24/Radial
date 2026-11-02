using Radial.Windows.Input;

namespace Radial.Windows.Tests.Input;

/// <summary>Traducción de mensajes de hook a eventos. Pura: no instala ningún hook.</summary>
public class HookMessagesTests
{
    private const long OtherAppMarker = 0x1234;

    [Theory]
    [InlineData(HookMessages.WM_KEYDOWN, HookMessages.VK_LCONTROL, InputEventKind.KeyDown, InputKey.LeftControl)]
    [InlineData(HookMessages.WM_KEYUP, HookMessages.VK_LCONTROL, InputEventKind.KeyUp, InputKey.LeftControl)]
    [InlineData(HookMessages.WM_KEYDOWN, HookMessages.VK_RCONTROL, InputEventKind.KeyDown, InputKey.RightControl)]
    [InlineData(HookMessages.WM_KEYUP, HookMessages.VK_RCONTROL, InputEventKind.KeyUp, InputKey.RightControl)]
    [InlineData(HookMessages.WM_SYSKEYDOWN, HookMessages.VK_LCONTROL, InputEventKind.KeyDown, InputKey.LeftControl)]
    [InlineData(HookMessages.WM_SYSKEYUP, HookMessages.VK_RCONTROL, InputEventKind.KeyUp, InputKey.RightControl)]
    [InlineData(HookMessages.WM_KEYDOWN, HookMessages.VK_ESCAPE, InputEventKind.KeyDown, InputKey.Escape)]
    [InlineData(HookMessages.WM_SYSKEYUP, HookMessages.VK_ESCAPE, InputEventKind.KeyUp, InputKey.Escape)]
    public void Keyboard_MapsControlAndEscape_IncludingSystemKeys(int message, int vk, InputEventKind kind, InputKey key)
    {
        Assert.True(HookMessages.TryMapKeyboard(message, vk, 0, 0, 123, out var result));

        Assert.Equal(kind, result.Kind);
        Assert.Equal(key, result.Key);
        Assert.False(result.Injected);
        Assert.False(result.OwnSynthetic);
        Assert.Equal(123, result.Timestamp);
    }

    [Theory]
    [InlineData(0x41)] // 'A'
    [InlineData(0x11)] // VK_CONTROL genérico: ambiguo, se ignora
    [InlineData(0xA0)] // Shift izquierdo
    [InlineData(0x12)] // Alt
    public void Keyboard_IgnoresOtherKeys(int vk)
    {
        Assert.False(HookMessages.TryMapKeyboard(HookMessages.WM_KEYDOWN, vk, 0, 0, 0, out _));
    }

    [Fact]
    public void Keyboard_IgnoresUnknownMessages()
    {
        Assert.False(HookMessages.TryMapKeyboard(0x0102 /* WM_CHAR */, HookMessages.VK_LCONTROL, 0, 0, 0, out _));
    }

    [Theory]
    [InlineData(0x10, true)]  // LLKHF_INJECTED
    [InlineData(0x02, true)]  // LLKHF_LOWER_IL_INJECTED
    [InlineData(0x12, true)]
    [InlineData(0x20, false)] // LLKHF_ALTDOWN: no es inyectado
    [InlineData(0x80, false)] // LLKHF_UP
    [InlineData(0x00, false)]
    public void Keyboard_DetectsInjectedEventsFromTheFlags(int flags, bool injected)
    {
        HookMessages.TryMapKeyboard(HookMessages.WM_KEYDOWN, HookMessages.VK_LCONTROL, flags, 0, 0, out var result);

        Assert.Equal(injected, result.Injected);
    }

    [Fact]
    public void Mouse_MapsMoveAndMiddleButton_WithTheirPosition()
    {
        Assert.True(HookMessages.TryMapMouse(HookMessages.WM_MOUSEMOVE, 10, 20, 0, 0, 5, out var move));
        Assert.True(HookMessages.TryMapMouse(HookMessages.WM_MBUTTONDOWN, 30, 40, 0, 0, 6, out var down));
        Assert.True(HookMessages.TryMapMouse(HookMessages.WM_MBUTTONUP, 50, 60, 0, 0, 7, out var up));

        Assert.Equal((InputEventKind.Move, new ScreenPoint(10, 20)), (move.Kind, move.Point));
        Assert.Equal((InputEventKind.MiddleDown, new ScreenPoint(30, 40)), (down.Kind, down.Point));
        Assert.Equal((InputEventKind.MiddleUp, new ScreenPoint(50, 60)), (up.Kind, up.Point));
        Assert.Equal(7, up.Timestamp);
    }

    [Theory]
    [InlineData(HookMessages.WM_MOUSEWHEEL)]
    [InlineData(HookMessages.WM_MOUSEHWHEEL)]
    public void Mouse_MapsTheWheelAsAWheelEvent_NeverAsAMiddleClick(int message)
    {
        Assert.True(HookMessages.TryMapMouse(message, 1, 2, 0, 0, 0, out var result));

        Assert.Equal(InputEventKind.Wheel, result.Kind);
    }

    [Theory]
    [InlineData(0x0201)] // WM_LBUTTONDOWN
    [InlineData(0x0202)] // WM_LBUTTONUP
    [InlineData(0x0204)] // WM_RBUTTONDOWN
    [InlineData(0x020B)] // WM_XBUTTONDOWN
    public void Mouse_IgnoresOtherButtons(int message)
    {
        Assert.False(HookMessages.TryMapMouse(message, 0, 0, 0, 0, 0, out _));
    }

    [Theory]
    [InlineData(0x01, true)]  // LLMHF_INJECTED
    [InlineData(0x02, true)]  // LLMHF_LOWER_IL_INJECTED
    [InlineData(0x00, false)]
    public void Mouse_DetectsInjectedEventsFromTheFlags(int flags, bool injected)
    {
        HookMessages.TryMapMouse(HookMessages.WM_MBUTTONDOWN, 0, 0, flags, 0, 0, out var result);

        Assert.Equal(injected, result.Injected);
    }

    [Fact]
    public void TheWheelNeverActivatesTheGesture_EvenThroughTheRealMapping()
    {
        var tracker = new ChordTracker();
        tracker.Process(RawInputEvent.KeyDown(InputKey.LeftControl));
        HookMessages.TryMapMouse(HookMessages.WM_MOUSEWHEEL, 5, 5, 0, 0, 0, out var wheel);

        var result = tracker.Process(in wheel);

        Assert.Equal(GestureSignal.None, result.Signal);
        Assert.False(result.Consume);
    }

    // Marca propia (dwExtraInfo)

    [Fact]
    public void TheMarker_IsNeverZero_AndFitsIn32Bits()
    {
        Assert.NotEqual(0L, SyntheticMarker.Value);
        Assert.InRange(SyntheticMarker.Value, 1L, uint.MaxValue);
    }

    [Fact]
    public void TheMouseHookDeliversOnlyThe32LowBits_SoOnlyThoseAreCompared()
    {
        // Medido con hooks reales: el ratón entrega dwExtraInfo truncado a 32 bits; el teclado, completo.
        var truncatedByTheMouseHook = SyntheticMarker.Value;
        var fullByTheKeyboardHook = SyntheticMarker.Value | (0x1F2E3D4CL << 32);

        HookMessages.TryMapMouse(HookMessages.WM_MBUTTONDOWN, 0, 0, 0x01, truncatedByTheMouseHook, 0, out var mouse);
        HookMessages.TryMapKeyboard(HookMessages.WM_KEYDOWN, HookMessages.VK_LCONTROL, 0x10, fullByTheKeyboardHook, 0, out var keyboard);

        Assert.True(mouse.OwnSynthetic);
        Assert.True(keyboard.OwnSynthetic);
    }

    [Fact]
    public void ADifferentLow32Bits_IsNotOurs_EvenWithMatchingHighBits()
    {
        var other = ((SyntheticMarker.Value ^ 1) & 0xFFFFFFFFL) | (SyntheticMarker.Value << 32);

        HookMessages.TryMapKeyboard(HookMessages.WM_KEYDOWN, HookMessages.VK_LCONTROL, 0x10, other, 0, out var result);

        Assert.True(result.Injected);
        Assert.False(result.OwnSynthetic);
    }

    [Fact]
    public void Keyboard_InjectedWithOurMarker_IsOwnSynthetic()
    {
        HookMessages.TryMapKeyboard(HookMessages.WM_KEYDOWN, HookMessages.VK_LCONTROL, 0x10, SyntheticMarker.Value, 0, out var result);

        Assert.True(result.Injected);
        Assert.True(result.OwnSynthetic);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(OtherAppMarker)]
    public void Keyboard_InjectedWithoutOurMarker_IsInjectedButNotOwn(long extraInfo)
    {
        HookMessages.TryMapKeyboard(HookMessages.WM_KEYDOWN, HookMessages.VK_LCONTROL, 0x10, extraInfo, 0, out var result);

        Assert.True(result.Injected);
        Assert.False(result.OwnSynthetic);
    }

    [Fact]
    public void Keyboard_WithOurMarkerButNotInjected_IsNeverOwn()
    {
        // Un evento físico no puede pasar por propio aunque su dwExtraInfo coincidiera por casualidad.
        HookMessages.TryMapKeyboard(HookMessages.WM_KEYDOWN, HookMessages.VK_LCONTROL, 0, SyntheticMarker.Value, 0, out var result);

        Assert.False(result.Injected);
        Assert.False(result.OwnSynthetic);
    }

    [Fact]
    public void Mouse_InjectedWithOurMarker_IsOwnSynthetic()
    {
        HookMessages.TryMapMouse(HookMessages.WM_MBUTTONDOWN, 1, 2, 0x01, SyntheticMarker.Value, 0, out var result);

        Assert.True(result.Injected);
        Assert.True(result.OwnSynthetic);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(OtherAppMarker)]
    public void Mouse_InjectedWithoutOurMarker_IsInjectedButNotOwn(long extraInfo)
    {
        HookMessages.TryMapMouse(HookMessages.WM_MBUTTONDOWN, 1, 2, 0x01, extraInfo, 0, out var result);

        Assert.True(result.Injected);
        Assert.False(result.OwnSynthetic);
    }

    [Fact]
    public void Mouse_WithOurMarkerButNotInjected_IsNeverOwn()
    {
        HookMessages.TryMapMouse(HookMessages.WM_MBUTTONDOWN, 1, 2, 0, SyntheticMarker.Value, 0, out var result);

        Assert.False(result.Injected);
        Assert.False(result.OwnSynthetic);
    }
}
