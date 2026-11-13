using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Windows.WindowControl;

namespace Radial.Windows.Tests.WindowControl;

/// <summary>Controlar la ventana activa, con un sistema de ventanas falso: nunca se toca una ventana real.</summary>
public class WindowControlActionExecutorTests
{
    private sealed class FakeWindows : IWindowSystem
    {
        public IntPtr Foreground { get; set; } = new(100);

        public WindowSnapshot Snapshot { get; set; } = Normal;

        /// <summary>Si se indica, la segunda inspección devuelve esto (la ventana cambió entre la comprobación y la orden).</summary>
        public WindowSnapshot? SnapshotOnSecondInspection { get; set; }

        public WindowCommandResult Result { get; set; } = new(true);

        public List<(IntPtr Window, WindowOperation Operation)> Commands { get; } = [];

        public int Inspections { get; private set; }

        public int ThreadOfForegroundRead { get; private set; }

        public IntPtr GetForegroundWindow()
        {
            ThreadOfForegroundRead = Environment.CurrentManagedThreadId;
            return Foreground;
        }

        public WindowSnapshot Inspect(IntPtr window)
        {
            Inspections++;
            return Inspections >= 2 && SnapshotOnSecondInspection is { } second ? second : Snapshot;
        }

        public WindowCommandResult Execute(IntPtr window, WindowOperation operation)
        {
            Commands.Add((window, operation));
            return Result;
        }
    }

    private static readonly WindowSnapshot Normal = new(true, false, false, true, true, true, WindowShowState.Normal);

    private readonly FakeWindows _windows = new();

    private Task<ActionExecutionResult> Run(WindowOperation operation, CancellationToken token = default) =>
        new WindowControlActionExecutor(_windows).ExecuteAsync(new WindowControlAction(operation), token);

    [Theory]
    [InlineData(WindowOperation.Minimize)]
    [InlineData(WindowOperation.Maximize)]
    [InlineData(WindowOperation.Close)]
    public async Task OnANormalWindow_TheOperationIsRequestedOnTheForegroundWindow(WindowOperation operation)
    {
        var result = await Run(operation);

        Assert.True(result.IsSuccess);
        var command = Assert.Single(_windows.Commands);
        Assert.Equal(new IntPtr(100), command.Window);
        Assert.Equal(operation, command.Operation);
    }

    [Theory]
    [InlineData(WindowShowState.Maximized)]
    [InlineData(WindowShowState.Minimized)]
    public async Task Restore_WorksOnAMaximizedOrMinimizedWindow(WindowShowState state)
    {
        _windows.Snapshot = Normal with { State = state };

        var result = await Run(WindowOperation.Restore);

        Assert.True(result.IsSuccess);
        Assert.Equal(WindowOperation.Restore, Assert.Single(_windows.Commands).Operation);
    }

    [Fact]
    public async Task Restore_OnAWindowAlreadyAtNormalSize_IsNotSupported_AndSendsNothing()
    {
        var result = await Run(WindowOperation.Restore);

        Assert.Equal(ActionErrorCodes.WindowOperationNotSupported, result.Error!.Code);
        Assert.Empty(_windows.Commands);
    }

    [Fact]
    public async Task Minimize_OnAWindowWithoutAMinimizeBox_IsNotSupported()
    {
        _windows.Snapshot = Normal with { CanMinimize = false };

        var result = await Run(WindowOperation.Minimize);

        Assert.Equal(ActionErrorCodes.WindowOperationNotSupported, result.Error!.Code);
        Assert.Empty(_windows.Commands);
    }

    [Fact]
    public async Task Maximize_OnAWindowWithoutAMaximizeBox_OrAlreadyMaximized_IsNotSupported()
    {
        _windows.Snapshot = Normal with { CanMaximize = false };
        var withoutBox = await Run(WindowOperation.Maximize);

        _windows.Snapshot = Normal with { State = WindowShowState.Maximized };
        var alreadyMaximized = await Run(WindowOperation.Maximize);

        Assert.Equal(ActionErrorCodes.WindowOperationNotSupported, withoutBox.Error!.Code);
        Assert.Equal(ActionErrorCodes.WindowOperationNotSupported, alreadyMaximized.Error!.Code);
        Assert.Empty(_windows.Commands);
    }

    [Fact]
    public async Task Minimize_OnAnAlreadyMinimizedWindow_IsNotSupported()
    {
        _windows.Snapshot = Normal with { State = WindowShowState.Minimized };

        Assert.Equal(ActionErrorCodes.WindowOperationNotSupported, (await Run(WindowOperation.Minimize)).Error!.Code);
    }

    [Fact]
    public async Task Close_IsAllowedEvenWithoutMinimizeOrMaximizeBoxes()
    {
        _windows.Snapshot = Normal with { CanMinimize = false, CanMaximize = false };

        Assert.True((await Run(WindowOperation.Close)).IsSuccess);
    }

    // Nunca sobre Radial, el escritorio ni lo que no existe

    [Theory]
    [InlineData(WindowOperation.Minimize)]
    [InlineData(WindowOperation.Maximize)]
    [InlineData(WindowOperation.Restore)]
    [InlineData(WindowOperation.Close)]
    public async Task NeverActsOnARadialWindow_WhateverTheOperation(WindowOperation operation)
    {
        _windows.Snapshot = Normal with { IsOwnWindow = true, State = WindowShowState.Maximized };

        var result = await Run(operation);

        Assert.Equal(ActionErrorCodes.WindowNotAvailable, result.Error!.Code);
        Assert.Contains("Radial", result.Error.Message);
        Assert.Empty(_windows.Commands);
    }

    [Theory]
    [InlineData(WindowOperation.Minimize)]
    [InlineData(WindowOperation.Close)]
    public async Task NeverActsOnTheDesktopOrTheTaskbar(WindowOperation operation)
    {
        _windows.Snapshot = Normal with { IsShell = true };

        var result = await Run(operation);

        Assert.Equal(ActionErrorCodes.WindowNotAvailable, result.Error!.Code);
        Assert.Empty(_windows.Commands);
    }

    [Fact]
    public async Task WithoutAForegroundWindow_NothingIsDone()
    {
        _windows.Foreground = IntPtr.Zero;

        var result = await Run(WindowOperation.Close);

        Assert.Equal(ActionErrorCodes.WindowNotAvailable, result.Error!.Code);
        Assert.Empty(_windows.Commands);
        Assert.Equal(0, _windows.Inspections);
    }

    [Fact]
    public async Task AWindowThatNoLongerExists_IsReported()
    {
        _windows.Snapshot = Normal with { Exists = false };

        var result = await Run(WindowOperation.Close);

        Assert.Equal(ActionErrorCodes.WindowNotAvailable, result.Error!.Code);
        Assert.Empty(_windows.Commands);
    }

    [Fact]
    public async Task AnInvisibleWindow_IsNotAValidTarget()
    {
        _windows.Snapshot = Normal with { IsVisible = false };

        Assert.Equal(ActionErrorCodes.WindowNotAvailable, (await Run(WindowOperation.Close)).Error!.Code);
    }

    [Fact]
    public async Task IfTheWindowDisappearsBetweenTheCheckAndTheOrder_NothingIsSent()
    {
        _windows.SnapshotOnSecondInspection = Normal with { Exists = false };

        var result = await Run(WindowOperation.Close);

        Assert.Equal(ActionErrorCodes.WindowNotAvailable, result.Error!.Code);
        Assert.Empty(_windows.Commands);
        Assert.Equal(2, _windows.Inspections);
    }

    [Fact]
    public async Task IfTheForegroundBecomesARadialWindowBetweenTheCheckAndTheOrder_NothingIsSent()
    {
        _windows.SnapshotOnSecondInspection = Normal with { IsOwnWindow = true };

        var result = await Run(WindowOperation.Minimize);

        Assert.Equal(ActionErrorCodes.WindowNotAvailable, result.Error!.Code);
        Assert.Empty(_windows.Commands);
    }

    // Fallos del sistema

    [Fact]
    public async Task AccessDenied_IsReportedAsAnElevatedWindow_WithoutNamingIt()
    {
        _windows.Result = new WindowCommandResult(false, 5);

        var result = await Run(WindowOperation.Close);

        Assert.Equal(ActionErrorCodes.WindowOperationFailed, result.Error!.Code);
        Assert.Contains("privilegios", result.Error.Message);
    }

    [Theory]
    [InlineData(0, "no aceptó la operación sobre la ventana.")]
    [InlineData(1400, "error 1400")]
    public async Task OtherSystemFailures_AreReportedWithTheirCodeWhenThereIsOne(int error, string expectedPart)
    {
        _windows.Result = new WindowCommandResult(false, error);

        var result = await Run(WindowOperation.Minimize);

        Assert.Equal(ActionErrorCodes.WindowOperationFailed, result.Error!.Code);
        Assert.Contains(expectedPart, result.Error.Message);
        if (error == 0)
        {
            Assert.DoesNotContain("error 0", result.Error.Message);
        }
    }

    // Contrato

    [Fact]
    public async Task ACancelledToken_DoesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await Run(WindowOperation.Close, cancellation.Token);

        Assert.Equal(ActionExecutionStatus.Cancelled, result.Status);
        Assert.Empty(_windows.Commands);
        Assert.Equal(0, _windows.Inspections);
    }

    [Fact]
    public async Task AnInvalidOperation_IsRejected_AndNothingIsInspected()
    {
        var result = await new WindowControlActionExecutor(_windows).ExecuteAsync(new WindowControlAction((WindowOperation)77));

        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Equal(0, _windows.Inspections);
        Assert.Empty(_windows.Commands);
    }

    [Fact]
    public async Task AnotherKindOfAction_IsNotSupported()
    {
        var executor = new WindowControlActionExecutor(_windows);

        Assert.False(executor.CanExecute(new VolumeAction(VolumeOperation.ToggleMute)));
        Assert.True(executor.CanExecute(new WindowControlAction(WindowOperation.Close)));
        Assert.Equal(ActionErrorCodes.UnsupportedAction, (await executor.ExecuteAsync(new VolumeAction(VolumeOperation.ToggleMute))).Error!.Code);
    }

    [Fact]
    public async Task TheWindowIsInspectedOffTheCallingThread_SoTheInterfaceThreadIsNeverBlocked()
    {
        var caller = 0;

        // Quien llama va en un hilo propio (LongRunning no usa el pool): la ventana solo puede haberse leído en otro hilo (el del pool).
        await Task.Factory.StartNew(
            () =>
            {
                caller = Environment.CurrentManagedThreadId;
                return Run(WindowOperation.Close);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

        Assert.NotEqual(0, _windows.ThreadOfForegroundRead);
        Assert.NotEqual(caller, _windows.ThreadOfForegroundRead);
    }
}
