namespace Radial.Core.Timing;

/// <summary>Reloj monotónico. Solo importan las diferencias entre lecturas; el origen es arbitrario.</summary>
public interface IClock
{
    TimeSpan Now { get; }
}
