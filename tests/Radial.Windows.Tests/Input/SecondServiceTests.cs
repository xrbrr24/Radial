using Radial.Windows.Hooks;
using Radial.Windows.Input;

namespace Radial.Windows.Tests.Input;

/// <summary>
/// Un segundo servicio en el mismo proceso: debe rechazarse sin duplicar hooks y sin dejar ningún estado a medias
/// (defecto D2 de la auditoría: se quedaba en Starting).
/// </summary>
public class SecondServiceTests
{
    private static GlobalInputService CreateService(ExclusiveFakeHookHost host) =>
        new(host, new FakePhysicalReader(), new FakeTimerFactory(), new FakeSystemEvents());

    [Fact]
    public void ASecondService_IsRejected_WithoutDuplicatingHooks_AndTheFirstKeepsWorking()
    {
        var slot = new ExclusiveHookSlot();
        var hostA = new ExclusiveFakeHookHost(slot);
        var hostB = new ExclusiveFakeHookHost(slot);
        using var first = CreateService(hostA);
        using var second = CreateService(hostB);
        Assert.True(first.Start().Success);

        var result = second.Start();

        // Rechazo del segundo inicio, con un motivo legible y sin dejar el estado en Starting.
        Assert.False(result.Success);
        Assert.Contains("otra captura", result.Message);
        Assert.Equal(CaptureState.Faulted, second.State);
        Assert.Contains(second.GetDiagnostics().Errors, e => e.Contains("otra captura"));

        // Ausencia de hooks duplicados: la plaza se ocupó una sola vez y el segundo host no instaló nada.
        Assert.Equal(1, slot.TotalAcquisitions);
        Assert.False(hostB.Inner.IsRunning);
        Assert.False(second.GetDiagnostics().KeyboardHook.Installed);
        Assert.False(second.GetDiagnostics().MouseHook.Installed);

        // El original conserva su estado y sigue procesando entrada.
        Assert.Equal(CaptureState.Running, first.State);
        Assert.True(hostA.Inner.IsRunning);
        hostA.Inner.Send(RawInputEvent.KeyDown(InputKey.LeftControl));
        Assert.True(hostA.Inner.Send(RawInputEvent.MiddleDown(5, 5)));
        Assert.True(first.Events.TryRead(out var activated));
        Assert.Equal(GestureEventKind.Activated, activated.Kind);
    }

    [Fact]
    public void AfterTheRejection_TheFirstServiceCanBeStopped_AndThenTheSecondOneStarts()
    {
        var slot = new ExclusiveHookSlot();
        var hostA = new ExclusiveFakeHookHost(slot);
        var hostB = new ExclusiveFakeHookHost(slot);
        using var first = CreateService(hostA);
        using var second = CreateService(hostB);
        first.Start();
        second.Start();

        // Detener el rechazado no libera los hooks del original.
        second.Stop();
        Assert.True(slot.Held);
        Assert.Equal(CaptureState.Running, first.State);

        // Detener el original libera la plaza.
        first.Stop();
        Assert.Equal(CaptureState.Stopped, first.State);
        Assert.False(slot.Held);
        Assert.False(hostA.Inner.IsRunning);

        // Ahora el segundo puede iniciar.
        Assert.True(second.Start().Success);
        Assert.Equal(CaptureState.Running, second.State);
        Assert.Equal(2, slot.TotalAcquisitions);
    }

    [Fact]
    public void ARejectedService_CanRetryWithoutRestarting()
    {
        var slot = new ExclusiveHookSlot();
        using var first = CreateService(new ExclusiveFakeHookHost(slot));
        using var second = CreateService(new ExclusiveFakeHookHost(slot));
        first.Start();
        Assert.False(second.Start().Success);
        Assert.False(second.Start().Success); // reintentar mientras sigue ocupada: sigue rechazándose, sin lanzar

        first.Stop();

        Assert.True(second.Start().Success);
    }
}

/// <summary>
/// Con los hooks REALES de Windows (sin inyectar nada): la regla de "un solo host por proceso" y la liberación al parar.
/// El sink devuelve siempre falso, así que no se consume ningún evento. Instala hooks unos milisegundos.
/// </summary>
public class RealHookHostGuardTests
{
    [Fact]
    public void OnlyOneRealHostCanBeActive_AndStopFreesTheSlot()
    {
        RawInputSink sink = (in RawInputEvent _) => false;
        var first = new LowLevelHookHost();
        var second = new LowLevelHookHost();

        var started = first.Start(sink);
        try
        {
            Assert.True(started.BothInstalled);
            Assert.Throws<InvalidOperationException>(() => second.Start(sink));
            Assert.Throws<InvalidOperationException>(() => first.Start(sink));
        }
        finally
        {
            first.Stop();
        }

        var again = second.Start(sink);
        try
        {
            Assert.True(again.BothInstalled);
        }
        finally
        {
            second.Stop();
        }
    }
}
