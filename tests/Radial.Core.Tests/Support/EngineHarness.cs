using Radial.Core.Engine;
using Radial.Core.Geometry;
using Radial.Core.Model;

namespace Radial.Core.Tests.Support;

/// <summary>Motor con reloj simulado y movimientos expresados respecto al centro actual del radial.</summary>
internal sealed class EngineHarness
{
    public static readonly PointD StartOrigin = new(500, 500);

    public EngineHarness(RadialConfiguration? configuration = null)
    {
        Clock = new ManualClock();
        Engine = new RadialEngine(configuration ?? TestConfigs.Standard(), Clock);
    }

    public ManualClock Clock { get; }

    public RadialEngine Engine { get; }

    public EngineHarness Start()
    {
        Engine.Start(StartOrigin);
        return this;
    }

    /// <summary>Mueve el cursor al centro del sector indicado, respecto al origen actual.</summary>
    public EngineHarness MoveTo(RadialDirection direction, double distance = 100)
    {
        Engine.UpdatePointer(Pointers.At(Engine.Origin, direction, distance));
        return this;
    }

    public EngineHarness MoveToCenter()
    {
        Engine.UpdatePointer(Engine.Origin);
        return this;
    }

    public EngineHarness Advance(int milliseconds)
    {
        Clock.AdvanceMilliseconds(milliseconds);
        Engine.Tick();
        return this;
    }

    public IReadOnlyList<string> StackIds() => Engine.NavigationStack.Select(m => m.Id.Value).ToList();
}
