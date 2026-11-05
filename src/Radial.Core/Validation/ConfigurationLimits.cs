namespace Radial.Core.Validation;

public static class ConfigurationLimits
{
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 64;
    public const int MaxIconValueLength = 1024;
    public const int MaxDescriptionLength = 500;
    public const int MaxTags = 12;
    public const int MaxTagLength = 24;

    /// <summary>Máximo de definiciones en la biblioteca de acciones.</summary>
    public const int MaxLibraryActions = 2000;

    public const double MaxDeadZoneRadius = 10_000;

    /// <summary>Por debajo de este tiempo el progreso saltaría de 0 a 1 sin que el usuario lo perciba.</summary>
    public static readonly TimeSpan MinDwell = TimeSpan.FromMilliseconds(50);

    public static readonly TimeSpan MaxDwell = TimeSpan.FromSeconds(10);

    public const int MaxDepthLimit = 10;
}
