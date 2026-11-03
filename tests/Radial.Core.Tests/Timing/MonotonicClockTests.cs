using System.Diagnostics;
using Radial.Core.Engine;
using Radial.Core.Geometry;
using Radial.Core.Tests.Support;
using Radial.Core.Timing;

namespace Radial.Core.Tests.Timing;

/// <summary>
/// El reloj de producción cumple el contrato de <see cref="IClock"/>. Son las únicas pruebas de Core con tiempo real, y solo
/// el mínimo para comprobar que el reloj avanza a la escala correcta (el resto de la lógica usa <see cref="ManualClock"/>).
/// </summary>
public class MonotonicClockTests
{
    [Fact]
    public void ItIsAnIClock_AndStartsAtANonNegativeTime()
    {
        IClock clock = new MonotonicClock();

        Assert.True(clock.Now >= TimeSpan.Zero);
    }

    [Fact]
    public void Now_NeverGoesBackwards()
    {
        var clock = new MonotonicClock();
        var previous = clock.Now;

        for (var i = 0; i < 100_000; i++)
        {
            var current = clock.Now;
            Assert.True(current >= previous, $"El reloj retrocedió en la lectura {i}.");
            previous = current;
        }
    }

    [Fact]
    public void Now_AdvancesWithoutAnyoneAdvancingIt()
    {
        var clock = new MonotonicClock();
        var first = clock.Now;

        // Sin esperas fijas: el reloj es de alta resolución, así que avanza en microsegundos. El límite solo evita un bucle infinito.
        var advanced = SpinWait.SpinUntil(() => clock.Now > first, TimeSpan.FromSeconds(5));

        Assert.True(advanced);
    }

    [Fact]
    public void Now_MeasuresRealTimeInTheRightUnits()
    {
        var clock = new MonotonicClock();
        var before = clock.Now;
        var reference = Stopwatch.StartNew();

        SpinWait.SpinUntil(() => reference.ElapsedMilliseconds >= 20);
        var elapsed = clock.Now - before;

        // Cota inferior estricta (el reloj se leyó antes de empezar y después de acabar la referencia) y cota superior muy
        // holgada, para que un equipo cargado no la rompa; un error de unidades (ticks, milisegundos) quedaría muy fuera.
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(20), $"Transcurrido: {elapsed}.");
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"Transcurrido: {elapsed}.");
    }

    [Fact]
    public void TheEngine_WorksWithTheProductionClock_AsItDoesWithTheManualOne()
    {
        var engine = new RadialEngine(TestConfigs.Standard(), new MonotonicClock());
        engine.Start(EngineHarness.StartOrigin);

        engine.UpdatePointer(Pointers.At(engine.Origin, RadialDirection.North, 100));
        var result = engine.Confirm();

        Assert.Equal(RadialResultKind.Execute, result.Kind);
        Assert.Equal("browser", result.Item!.Id.Value);
    }
}
