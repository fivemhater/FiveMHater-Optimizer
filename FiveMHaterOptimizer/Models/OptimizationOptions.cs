namespace FiveMHaterOptimizer.Models;

public enum OptimizationProfile
{
    Extreme,
    Balanced,
    NetworkOnly
}

public sealed class OptimizationOptions
{
    public OptimizationProfile Profile { get; set; } = OptimizationProfile.Extreme;
    public bool ClearFiveMCache { get; set; } = true;
    public bool ApplyFiveMFpsPreset { get; set; } = true;
    public bool DisablePrinterServices { get; set; } = true;

    public string ProfileDisplayName => Profile switch
    {
        OptimizationProfile.Extreme => "Extreme / Competitive",
        OptimizationProfile.Balanced => "Balanced (Günlük + Oyun)",
        _ => "Sadece Ağ & Ping"
    };
}
