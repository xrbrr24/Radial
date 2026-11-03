using Radial.Core.Execution;

namespace Radial.Core.Tests.Execution;

public class ActionExecutionResultTests
{
    [Fact]
    public void Success_IsSuccessful_AndHasNoError()
    {
        var result = ActionExecutionResult.Success;

        Assert.Equal(ActionExecutionStatus.Succeeded, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_IsNotSuccessful_AndCarriesTheErrorInformation()
    {
        var result = ActionExecutionResult.Failure("SomeCode", "Algo falló.");

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal("SomeCode", result.Error.Code);
        Assert.Equal("Algo falló.", result.Error.Message);
    }

    [Fact]
    public void Cancelled_IsNeitherSuccessNorAnError()
    {
        var result = ActionExecutionResult.Cancelled;

        Assert.Equal(ActionExecutionStatus.Cancelled, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(null, "mensaje")]
    [InlineData("", "mensaje")]
    [InlineData("   ", "mensaje")]
    [InlineData("Code", null)]
    [InlineData("Code", "")]
    [InlineData("Code", "   ")]
    public void Failure_RejectsAnEmptyCodeOrMessage(string? code, string? message)
    {
        Assert.ThrowsAny<ArgumentException>(() => ActionExecutionResult.Failure(code!, message!));
    }

    [Fact]
    public void TheThreeOutcomes_AreDistinguishable()
    {
        var outcomes = new[]
        {
            ActionExecutionResult.Success,
            ActionExecutionResult.Failure("Code", "Mensaje."),
            ActionExecutionResult.Cancelled,
        };

        Assert.Equal(3, outcomes.Select(o => o.Status).Distinct().Count());
        Assert.Single(outcomes, o => o.IsSuccess);
    }

    [Fact]
    public void Results_AreComparedByValue()
    {
        Assert.Equal(ActionExecutionResult.Failure("Code", "Mensaje."), ActionExecutionResult.Failure("Code", "Mensaje."));
        Assert.NotEqual(ActionExecutionResult.Failure("Code", "Mensaje."), ActionExecutionResult.Failure("Other", "Mensaje."));
        Assert.NotEqual(ActionExecutionResult.Success, ActionExecutionResult.Cancelled);
    }

    [Fact]
    public void ErrorCodes_AreStableAndDistinct()
    {
        // Los códigos son parte del contrato: las pruebas y los avisos dependen de ellos, así que no pueden cambiar sin querer.
        Assert.Equal("UnsupportedAction", ActionErrorCodes.UnsupportedAction);
        Assert.Equal("InvalidAction", ActionErrorCodes.InvalidAction);
        Assert.NotEqual(ActionErrorCodes.UnsupportedAction, ActionErrorCodes.InvalidAction);
    }
}
