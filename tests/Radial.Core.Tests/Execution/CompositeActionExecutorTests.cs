using Radial.Core.Execution;
using Radial.Core.Model;

namespace Radial.Core.Tests.Execution;

public class CompositeActionExecutorTests
{
    private sealed class FakeExecutor<TAction>(ActionExecutionResult result) : IActionExecutor
        where TAction : RadialAction
    {
        public List<RadialAction> Executed { get; } = [];

        public bool CanExecute(RadialAction action) => action is TAction;

        public Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
        {
            Executed.Add(action);
            return Task.FromResult(result);
        }
    }

    private static readonly SendKeysAction Keys = new(new KeyChord(KeyModifiers.Control, "C"));
    private static readonly OpenUrlAction Url = new("https://example.com");

    [Fact]
    public async Task EachAction_IsExecutedByTheExecutorThatSupportsIt()
    {
        var shortcuts = new FakeExecutor<SendKeysAction>(ActionExecutionResult.Success);
        var urls = new FakeExecutor<OpenUrlAction>(ActionExecutionResult.Failure(ActionErrorCodes.LaunchFailed, "No."));
        var composite = new CompositeActionExecutor([shortcuts, urls]);

        var first = await composite.ExecuteAsync(Keys);
        var second = await composite.ExecuteAsync(Url);

        Assert.True(first.IsSuccess);
        Assert.Equal(ActionErrorCodes.LaunchFailed, second.Error!.Code);
        Assert.Equal([Keys], shortcuts.Executed);
        Assert.Equal([Url], urls.Executed);
    }

    [Fact]
    public async Task SeveralActionsOfTheSameType_ShareTheSameExecutor()
    {
        var shortcuts = new FakeExecutor<SendKeysAction>(ActionExecutionResult.Success);
        var composite = new CompositeActionExecutor([shortcuts]);

        await composite.ExecuteAsync(new SendKeysAction(new KeyChord(KeyModifiers.Control, "C")));
        await composite.ExecuteAsync(new SendKeysAction(new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, "S")));

        Assert.Equal(2, shortcuts.Executed.Count);
    }

    [Fact]
    public async Task AnActionNoExecutorSupports_IsUnsupported_AndNothingRuns()
    {
        var shortcuts = new FakeExecutor<SendKeysAction>(ActionExecutionResult.Success);
        var composite = new CompositeActionExecutor([shortcuts]);

        var result = await composite.ExecuteAsync(Url);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.UnsupportedAction, result.Error!.Code);
        Assert.Empty(shortcuts.Executed);
        Assert.False(composite.CanExecute(Url));
    }

    [Fact]
    public void CanExecute_IsTrueWhenAnyExecutorSupportsTheAction()
    {
        var composite = new CompositeActionExecutor([new FakeExecutor<SendKeysAction>(ActionExecutionResult.Success), new FakeExecutor<OpenUrlAction>(ActionExecutionResult.Success)]);

        Assert.True(composite.CanExecute(Keys));
        Assert.True(composite.CanExecute(Url));
        Assert.False(composite.CanExecute(new OpenMenuAction(new MenuId("x"))));
    }

    [Fact]
    public void AnEmptyComposite_SupportsNothing() => Assert.False(new CompositeActionExecutor([]).CanExecute(Keys));

    [Fact]
    public async Task TheFirstExecutorThatSupportsTheActionWins()
    {
        var first = new FakeExecutor<SendKeysAction>(ActionExecutionResult.Success);
        var second = new FakeExecutor<SendKeysAction>(ActionExecutionResult.Cancelled);
        var composite = new CompositeActionExecutor([first, second]);

        var result = await composite.ExecuteAsync(Keys);

        Assert.True(result.IsSuccess);
        Assert.Empty(second.Executed);
    }

    [Fact]
    public async Task TheCancellationToken_ReachesTheExecutor()
    {
        var seen = default(CancellationToken);
        var composite = new CompositeActionExecutor([new TokenSpy(t => seen = t)]);
        using var cts = new CancellationTokenSource();

        await composite.ExecuteAsync(Keys, cts.Token);

        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public async Task InvalidArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeActionExecutor(null!));
        Assert.Throws<ArgumentException>(() => new CompositeActionExecutor([null!]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new CompositeActionExecutor([]).ExecuteAsync(null!));
    }

    private sealed class TokenSpy(Action<CancellationToken> onToken) : IActionExecutor
    {
        public bool CanExecute(RadialAction action) => true;

        public Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
        {
            onToken(cancellationToken);
            return Task.FromResult(ActionExecutionResult.Success);
        }
    }
}
