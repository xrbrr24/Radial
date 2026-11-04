using Radial.Core.Execution;

namespace Radial.Core.Tests.Execution;

/// <summary>Los códigos de error que añadieron los ejecutores de la fase 4, paso 3. Son parte del contrato: no pueden cambiar sin querer.</summary>
public class LaunchErrorCodesTests
{
    [Fact]
    public void TheLaunchErrorCodes_AreStable()
    {
        Assert.Equal("TargetNotFound", ActionErrorCodes.TargetNotFound);
        Assert.Equal("LaunchFailed", ActionErrorCodes.LaunchFailed);
        Assert.Equal("UrlNotAllowed", ActionErrorCodes.UrlNotAllowed);
        Assert.Equal("TargetNotAllowed", ActionErrorCodes.TargetNotAllowed);
        Assert.Equal("KeyReleaseFailed", ActionErrorCodes.KeyReleaseFailed);
    }

    [Fact]
    public void EveryErrorCode_IsDistinct()
    {
        var codes = new[]
        {
            ActionErrorCodes.UnsupportedAction,
            ActionErrorCodes.InvalidAction,
            ActionErrorCodes.TargetNotFound,
            ActionErrorCodes.LaunchFailed,
            ActionErrorCodes.UrlNotAllowed,
            ActionErrorCodes.ModifiersNotReleased,
            ActionErrorCodes.ForegroundChanged,
            ActionErrorCodes.InputInjectionFailed,
            ActionErrorCodes.KeyReleaseFailed,
            ActionErrorCodes.TargetNotAllowed,
            ActionErrorCodes.UnexpectedError,
        };

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryErrorCode_CanBeCarriedByAFailureResult()
    {
        var fields = typeof(ActionErrorCodes).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.Equal(11, fields.Count); // si se añade un código, esta prueba obliga a revisar la lista de arriba
        Assert.All(fields, code => Assert.Equal(code, ActionExecutionResult.Failure(code, "Mensaje.").Error!.Code));
    }
}
