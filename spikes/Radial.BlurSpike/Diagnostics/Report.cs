namespace Radial.BlurSpike.Diagnostics;

public sealed class SuiteReport
{
    public string Date { get; set; } = string.Empty;
    public string Windows { get; set; } = string.Empty;
    public string Dotnet { get; set; } = string.Empty;
    public string Gpu { get; set; } = string.Empty;
    public string Monitors { get; set; } = string.Empty;
    public int Dpi { get; set; }
    public int OverlaySizeDip { get; set; }
    public List<VariantReport> Variants { get; set; } = [];
}

public sealed class VariantReport
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string ExStyle { get; set; } = string.Empty;
    public string RuntimeWindowSize { get; set; } = string.Empty;
    public bool AltTabEligibleHeuristic { get; set; }

    // Foco
    public string ForegroundBefore { get; set; } = string.Empty;
    public string ForegroundAfter { get; set; } = string.Empty;
    public bool ForegroundUnchanged { get; set; }

    // Visual (modo sin click-through)
    public double ReferenceSharpness { get; set; }
    public double Sharpness { get; set; }
    public double ContrastRange { get; set; }
    public double MeanLuminance { get; set; }
    public string BlurVerdict { get; set; } = string.Empty;
    public bool CornerTransparent { get; set; }

    // Experimento: mismo overlay pero ACTIVADO (con un clic real sobre él), para comparar material activo e inactivo.
    public bool ActivationTested { get; set; }
    public bool ActivationBecameForeground { get; set; }
    public double ActiveSharpness { get; set; }
    public double ActiveContrastRange { get; set; }
    public double ActiveMeanLuminance { get; set; }
    public string ActiveVerdict { get; set; } = string.Empty;

    public List<ClickResult> Clicks { get; set; } = [];
    public string ChosenClickThrough { get; set; } = string.Empty;
    public PerfReport Perf { get; set; } = new();
    public List<string> Errors { get; set; } = [];
}

public sealed class ClickResult
{
    public string Mode { get; set; } = string.Empty;
    public int ReceiverClicks { get; set; }
    public int ReceiverMoves { get; set; }
    public int OverlayClicks { get; set; }
    public string ForegroundAfter { get; set; } = string.Empty;
    public double SharpnessWhileActive { get; set; }
    public string BlurVerdict { get; set; } = string.Empty;
    public string ExStyle { get; set; } = string.Empty;
    public bool PassesThrough { get; set; }
    public string Skipped { get; set; } = string.Empty;
}

public sealed class PerfReport
{
    public double ColdShowCallMs { get; set; }
    public double ColdVisibleMs { get; set; }
    public int Cycles { get; set; }
    public double ShowCallMedianMs { get; set; }
    public double ShowCallP95Ms { get; set; }
    public double VisibleMedianMs { get; set; }
    public double VisibleP95Ms { get; set; }
    public double FirstRenderMedianMs { get; set; }
    public double HideMedianMs { get; set; }
    public double HideP95Ms { get; set; }
    public int MissedSamples { get; set; }
    public int ShowMisses { get; set; }
    public int HideMisses { get; set; }
    public double CpuVisiblePercentOfOneCore { get; set; }
    public double CpuHiddenPercentOfOneCore { get; set; }
    public double PrivateMbHidden { get; set; }
    public double PrivateMbVisible { get; set; }
    public double WorkingSetMbVisible { get; set; }
    public double MoveFrameMedianMs { get; set; }
    public double MoveFrameP95Ms { get; set; }
    public double MoveFrameMaxMs { get; set; }
    public int MoveFrames { get; set; }
    public int MoveSlowFrames { get; set; }
    public double CaptureMedianMs { get; set; }
    public double CaptureP95Ms { get; set; }
    public double CaptureMegabytes { get; set; }
}
