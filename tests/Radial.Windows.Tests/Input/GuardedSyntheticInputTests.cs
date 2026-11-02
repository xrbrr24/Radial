using Radial.Windows.Diagnostics;
using Radial.Windows.Input;

namespace Radial.Windows.Tests.Input;

/// <summary>Registra lo que se "enviaría" a Windows. No inyecta nada.</summary>
internal sealed class FakeSyntheticBackend : ISyntheticBackend
{
    public int OwnProcessId => 100;

    public ScreenPoint Cursor { get; set; }

    public int ProcessUnderCursor { get; set; } = 100;

    public int ForegroundProcess { get; set; } = 100;

    public bool FailSends { get; set; }

    public List<string> Sent { get; } = [];

    public ScreenPoint GetCursor() => Cursor;

    public int GetProcessIdAt(ScreenPoint point) => ProcessUnderCursor;

    public int GetForegroundProcessId() => ForegroundProcess;

    public void MoveTo(ScreenPoint point)
    {
        Cursor = point;
        Sent.Add($"move {point.X},{point.Y}");
    }

    public void KeyDown(InputKey key) => Record($"down {key}");

    public void KeyUp(InputKey key) => Record($"up {key}");

    public void MiddleDown() => Record("down Middle");

    public void MiddleUp() => Record("up Middle");

    public void Wheel() => Record("wheel");

    private void Record(string what)
    {
        if (FailSends)
        {
            throw new InvalidOperationException("SendInput falló.");
        }

        Sent.Add(what);
    }
}

/// <summary>Las protecciones de la autoprueba (D1/D3) sin tocar Windows: comprobación previa, qué se pulsó y liberación.</summary>
public class GuardedSyntheticInputTests
{
    private readonly FakeSyntheticBackend _backend = new();
    private readonly GuardedSyntheticInput _input;

    public GuardedSyntheticInputTests()
    {
        _input = new GuardedSyntheticInput(_backend);
        _input.MoveCursor(new ScreenPoint(500, 400));
        _backend.Sent.Clear();
    }

    [Fact]
    public void WhenEverythingIsSafe_TheInputIsSent()
    {
        _input.KeyDown(InputKey.LeftControl);
        _input.MiddleDown();
        _input.Wheel();

        Assert.Equal(["down LeftControl", "down Middle", "wheel"], _backend.Sent);
    }

    [Fact]
    public void BeforeThePointerIsPlaced_NothingIsSent()
    {
        var input = new GuardedSyntheticInput(_backend);

        Assert.Throws<InvalidOperationException>(() => input.KeyDown(InputKey.LeftControl));
        Assert.Empty(_backend.Sent);
    }

    [Fact]
    public void IfTheCursorWasMoved_NothingIsSent()
    {
        _backend.Cursor = new ScreenPoint(900, 400);

        var error = Assert.Throws<SyntheticInputUnsafeException>(() => _input.MiddleDown());

        Assert.Contains("cursor", error.Message);
        Assert.Empty(_backend.Sent);
        Assert.False(_input.AnyHeld);
    }

    [Fact]
    public void ASmallCursorRoundingError_IsTolerated()
    {
        _backend.Cursor = new ScreenPoint(502, 399);

        _input.KeyDown(InputKey.Escape);

        Assert.Equal(["down Escape"], _backend.Sent);
    }

    [Fact]
    public void IfTheWindowUnderTheCursorIsAnotherProcess_NothingIsSent()
    {
        _backend.ProcessUnderCursor = 777;

        var error = Assert.Throws<SyntheticInputUnsafeException>(() => _input.MiddleDown());

        Assert.Contains("777", error.Message);
        Assert.Empty(_backend.Sent);
    }

    [Fact]
    public void IfNoWindowIsUnderTheCursor_NothingIsSent()
    {
        _backend.ProcessUnderCursor = 0;

        Assert.Throws<SyntheticInputUnsafeException>(() => _input.Wheel());
        Assert.Empty(_backend.Sent);
    }

    [Fact]
    public void IfTheForegroundWindowIsAnotherProcess_KeysAreNotSent()
    {
        _backend.ForegroundProcess = 888;

        var error = Assert.Throws<SyntheticInputUnsafeException>(() => _input.KeyDown(InputKey.LeftControl));

        Assert.Contains("primer plano", error.Message);
        Assert.Contains("888", error.Message);
        Assert.Empty(_backend.Sent);
    }

    [Fact]
    public void EveryInjectionIsCheckedAgain_NotJustTheFirstOne()
    {
        _input.KeyDown(InputKey.LeftControl);
        _backend.ForegroundProcess = 888; // otra ventana toma el primer plano a mitad de la prueba

        Assert.Throws<SyntheticInputUnsafeException>(() => _input.MiddleDown());

        Assert.Equal(["down LeftControl"], _backend.Sent);
    }

    [Fact]
    public void ReleaseAll_ReleasesOnlyWhatWasPressed_MiddleFirstThenEscapeThenCtrl()
    {
        _input.KeyDown(InputKey.LeftControl);
        _input.KeyDown(InputKey.RightControl);
        _input.MiddleDown();
        _input.KeyDown(InputKey.Escape);
        _backend.Sent.Clear();

        Assert.True(_input.ReleaseAll());

        Assert.Equal(["up Middle", "up Escape", "up RightControl", "up LeftControl"], _backend.Sent);
        Assert.False(_input.AnyHeld);
    }

    [Fact]
    public void ReleaseAll_DoesNotTouchInputsTheTestNeverPressed()
    {
        _input.KeyDown(InputKey.LeftControl);
        _backend.Sent.Clear();

        _input.ReleaseAll();

        Assert.Equal(["up LeftControl"], _backend.Sent); // ni botón central, ni Escape, ni Ctrl derecho
    }

    [Fact]
    public void ReleaseAll_WithNothingPressed_SendsNothing()
    {
        Assert.True(_input.ReleaseAll());

        Assert.Empty(_backend.Sent);
    }

    [Fact]
    public void ReleaseAll_IsIdempotent()
    {
        _input.MiddleDown();
        _input.ReleaseAll();
        _backend.Sent.Clear();

        _input.ReleaseAll();

        Assert.Empty(_backend.Sent);
    }

    [Fact]
    public void ReleasesOfInputsNotPressedByTheTest_AreNotSent()
    {
        _input.KeyUp(InputKey.LeftControl);
        _input.KeyUp(InputKey.Escape);
        _input.MiddleUp();

        Assert.Empty(_backend.Sent);
    }

    [Fact]
    public void AfterAnUnsafeAbort_ReleaseAllStillReleasesWhatWasPressed_WithoutTheSafetyCheck()
    {
        _input.KeyDown(InputKey.LeftControl);
        _input.MiddleDown();
        _backend.ForegroundProcess = 888;
        _backend.Cursor = new ScreenPoint(1, 1);
        _backend.Sent.Clear();

        // Dejarlas pulsadas es peor que enviar la liberación: no se comprueba el destino.
        Assert.True(_input.ReleaseAll());

        Assert.Equal(["up Middle", "up LeftControl"], _backend.Sent);
    }

    [Fact]
    public void AfterAFailedRelease_ThatInputStaysMarked_AndTheRestAreStillReleased()
    {
        _input.KeyDown(InputKey.LeftControl);
        _input.MiddleDown();
        _backend.FailSends = true;

        Assert.False(_input.ReleaseAll());
        Assert.True(_input.AnyHeld);

        _backend.FailSends = false;
        _backend.Sent.Clear();
        Assert.True(_input.ReleaseAll()); // el reintento libera lo que quedó
        Assert.Equal(["up Middle", "up LeftControl"], _backend.Sent);
    }

    [Fact]
    public void ARegularRelease_ClearsTheMark()
    {
        _input.KeyDown(InputKey.LeftControl);
        _input.KeyUp(InputKey.LeftControl);

        Assert.False(_input.AnyHeld);
        _backend.Sent.Clear();
        _input.ReleaseAll();
        Assert.Empty(_backend.Sent);
    }

    [Fact]
    public void DescribeHeld_ListsWhatIsPressed()
    {
        Assert.Equal(string.Empty, _input.DescribeHeld());

        _input.KeyDown(InputKey.LeftControl);
        _input.MiddleDown();

        Assert.Equal("Ctrl izquierdo, botón central", _input.DescribeHeld());
    }

    [Fact]
    public void RestoreCursor_OnlyMovesItIfNobodyElseMovedIt()
    {
        Assert.True(_input.RestoreCursorIfUntouched(new ScreenPoint(10, 10)));
        Assert.Equal(new ScreenPoint(10, 10), _backend.Cursor);

        _backend.Cursor = new ScreenPoint(700, 700); // el usuario lo movió
        _backend.Sent.Clear();
        Assert.False(_input.RestoreCursorIfUntouched(new ScreenPoint(10, 10)));
        Assert.Empty(_backend.Sent);
    }
}
