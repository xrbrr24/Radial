using Radial.Core.Timing;

namespace Radial.Core.Tests.Support;

/// <summary>Reloj controlado por la prueba: el tiempo solo avanza cuando se le pide.</summary>
internal sealed class ManualClock : IClock
{
    public TimeSpan Now { get; private set; }

    public void Advance(TimeSpan amount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, TimeSpan.Zero);
        Now += amount;
    }

    public void AdvanceMilliseconds(int milliseconds) => Advance(TimeSpan.FromMilliseconds(milliseconds));
}
