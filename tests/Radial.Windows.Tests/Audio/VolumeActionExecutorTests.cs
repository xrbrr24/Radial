using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Windows.Audio;

namespace Radial.Windows.Tests.Audio;

/// <summary>Controlar el volumen: la aritmética y los fallos, con un dispositivo falso. Nunca se toca el audio real.</summary>
public class VolumeActionExecutorTests
{
    private sealed class FakeEndpoint(float level, bool muted) : IAudioEndpoint
    {
        public float Level { get; private set; } = level;

        public bool Muted { get; private set; } = muted;

        public bool Disposed { get; private set; }

        public Exception? FailOnSetLevel { get; set; }

        public float GetLevel() => Level;

        public void SetLevel(float value)
        {
            if (FailOnSetLevel is { } exception)
            {
                throw exception;
            }

            Level = value;
        }

        public bool GetMuted() => Muted;

        public void SetMuted(bool value) => Muted = value;

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeProvider(FakeEndpoint? endpoint, AudioDeviceException? failure = null) : IAudioEndpointProvider
    {
        public int Opened { get; private set; }

        public int ThreadOfOpen { get; private set; }

        public IAudioEndpoint OpenDefaultOutput()
        {
            Opened++;
            ThreadOfOpen = Environment.CurrentManagedThreadId;
            return failure is not null ? throw failure : endpoint!;
        }
    }

    private static async Task<(ActionExecutionResult Result, FakeEndpoint Endpoint, FakeProvider Provider)> Run(VolumeAction action, float level = 0.5f, bool muted = false)
    {
        var endpoint = new FakeEndpoint(level, muted);
        var provider = new FakeProvider(endpoint);
        var result = await new VolumeActionExecutor(provider).ExecuteAsync(action);
        return (result, endpoint, provider);
    }

    [Theory]
    [InlineData(0.50f, 10, 0.60)]
    [InlineData(0.00f, 1, 0.01)]
    [InlineData(0.95f, 10, 1.00)]   // se acota a 100
    [InlineData(1.00f, 5, 1.00)]
    public async Task Increase_AddsPointsToTheCurrentLevel_ClampedToOneHundred(float start, int points, double expected)
    {
        var (result, endpoint, _) = await Run(new VolumeAction(VolumeOperation.Increase, points), start);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, endpoint.Level, 3);
    }

    [Theory]
    [InlineData(0.50f, 10, 0.40)]
    [InlineData(0.03f, 5, 0.00)]    // se acota a 0
    [InlineData(0.00f, 5, 0.00)]
    [InlineData(1.00f, 100, 0.00)]
    public async Task Decrease_SubtractsPointsFromTheCurrentLevel_ClampedToZero(float start, int points, double expected)
    {
        var (result, endpoint, _) = await Run(new VolumeAction(VolumeOperation.Decrease, points), start);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, endpoint.Level, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(100)]
    public async Task SetLevel_SetsExactlyThatLevel(int percent)
    {
        var (result, endpoint, _) = await Run(new VolumeAction(VolumeOperation.SetLevel, percent), 0.8f);

        Assert.True(result.IsSuccess);
        Assert.Equal(percent / 100.0, endpoint.Level, 3);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ToggleMute_FlipsTheMute_AndLeavesTheLevelAlone(bool before, bool after)
    {
        var (result, endpoint, _) = await Run(new VolumeAction(VolumeOperation.ToggleMute), 0.42f, before);

        Assert.True(result.IsSuccess);
        Assert.Equal(after, endpoint.Muted);
        Assert.Equal(0.42, endpoint.Level, 3);
    }

    [Fact]
    public async Task IncreasingWhileMuted_Unmutes()
    {
        var (_, endpoint, _) = await Run(new VolumeAction(VolumeOperation.Increase, 5), 0.3f, muted: true);

        Assert.False(endpoint.Muted);
        Assert.Equal(0.35, endpoint.Level, 3);
    }

    [Fact]
    public async Task DecreasingWhileMuted_KeepsTheMute()
    {
        var (_, endpoint, _) = await Run(new VolumeAction(VolumeOperation.Decrease, 5), 0.3f, muted: true);

        Assert.True(endpoint.Muted);
    }

    [Fact]
    public async Task SettingALevelAboveZeroWhileMuted_Unmutes_ButSettingZeroDoesNot()
    {
        var (_, loud, _) = await Run(new VolumeAction(VolumeOperation.SetLevel, 20), 0.3f, muted: true);
        var (_, silent, _) = await Run(new VolumeAction(VolumeOperation.SetLevel, 0), 0.3f, muted: true);

        Assert.False(loud.Muted);
        Assert.True(silent.Muted);
    }

    [Fact]
    public async Task TheDevice_IsAlwaysReleased()
    {
        var (_, endpoint, _) = await Run(new VolumeAction(VolumeOperation.ToggleMute));

        Assert.True(endpoint.Disposed);
    }

    [Fact]
    public async Task TheDevice_IsReleased_EvenIfWindowsRefusesTheChange()
    {
        var endpoint = new FakeEndpoint(0.5f, false) { FailOnSetLevel = new AudioDeviceException("x", deviceUnavailable: false) };

        var result = await new VolumeActionExecutor(new FakeProvider(endpoint)).ExecuteAsync(new VolumeAction(VolumeOperation.Increase, 5));

        Assert.Equal(ActionErrorCodes.AudioControlFailed, result.Error!.Code);
        Assert.True(endpoint.Disposed);
    }

    [Fact]
    public async Task WithoutAnOutputDevice_ItReportsAudioUnavailable()
    {
        var provider = new FakeProvider(null, new AudioDeviceException("sin dispositivo", deviceUnavailable: true));

        var result = await new VolumeActionExecutor(provider).ExecuteAsync(new VolumeAction(VolumeOperation.ToggleMute));

        Assert.Equal(ActionErrorCodes.AudioUnavailable, result.Error!.Code);
        Assert.Equal(1, provider.Opened);
    }

    [Fact]
    public async Task IfWindowsRefusesToOpenTheDevice_ItReportsAControlFailure_WithoutTheInternalMessage()
    {
        var provider = new FakeProvider(null, new AudioDeviceException("detalle-interno-0x8899", deviceUnavailable: false));

        var result = await new VolumeActionExecutor(provider).ExecuteAsync(new VolumeAction(VolumeOperation.ToggleMute));

        Assert.Equal(ActionErrorCodes.AudioControlFailed, result.Error!.Code);
        Assert.DoesNotContain("detalle-interno", result.Error.Message);
    }

    [Theory]
    [InlineData(VolumeOperation.Increase, 0)]
    [InlineData(VolumeOperation.Decrease, 101)]
    [InlineData(VolumeOperation.SetLevel, 150)]
    public async Task AnInvalidAction_IsRejected_WithoutTouchingTheDevice(VolumeOperation operation, int percent)
    {
        var (result, endpoint, provider) = await Run(new VolumeAction(operation, percent));

        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Equal(0, provider.Opened);
        Assert.Equal(0.5, endpoint.Level, 3);
    }

    [Fact]
    public async Task ACancelledToken_DoesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var endpoint = new FakeEndpoint(0.5f, false);
        var provider = new FakeProvider(endpoint);

        var result = await new VolumeActionExecutor(provider).ExecuteAsync(new VolumeAction(VolumeOperation.ToggleMute), cancellation.Token);

        Assert.Equal(ActionExecutionStatus.Cancelled, result.Status);
        Assert.Equal(0, provider.Opened);
    }

    [Fact]
    public async Task TheDevice_IsOpenedOffTheCallingThread_SoTheInterfaceThreadIsNeverBlocked()
    {
        var provider = new FakeProvider(new FakeEndpoint(0.5f, false));
        var caller = 0;

        // Quien llama va en un hilo propio (LongRunning no usa el pool): el dispositivo solo puede haberse abierto en otro hilo (el del pool).
        await Task.Factory.StartNew(
            () =>
            {
                caller = Environment.CurrentManagedThreadId;
                return new VolumeActionExecutor(provider).ExecuteAsync(new VolumeAction(VolumeOperation.ToggleMute));
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

        Assert.Equal(1, provider.Opened);
        Assert.NotEqual(0, provider.ThreadOfOpen);
        Assert.NotEqual(caller, provider.ThreadOfOpen);
    }

    [Fact]
    public async Task AnotherKindOfAction_IsNotSupported()
    {
        var executor = new VolumeActionExecutor(new FakeProvider(new FakeEndpoint(0.5f, false)));

        Assert.False(executor.CanExecute(new WindowControlAction(WindowOperation.Close)));
        Assert.True(executor.CanExecute(new VolumeAction(VolumeOperation.ToggleMute)));
        Assert.Equal(ActionErrorCodes.UnsupportedAction, (await executor.ExecuteAsync(new WindowControlAction(WindowOperation.Close))).Error!.Code);
    }
}
