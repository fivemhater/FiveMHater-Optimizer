namespace FiveMHaterOptimizer.Models;

public enum OptimizationProfile
{
    Extreme,
    Balanced,
    NetworkOnly,
    Ultra
}

public sealed class OptimizationOptions
{
    public OptimizationProfile Profile { get; set; } = OptimizationProfile.Ultra;
    public bool ClearFiveMCache { get; set; } = true;
    public bool ApplyFiveMFpsPreset { get; set; } = true;
    public bool DisablePrinterServices { get; set; } = true;

    // ---- Yalnızca Ultra / Full+Full ----
    /// <summary>Discord ve Steam başlangıçta açılmaya devam etsin (başlangıç taramasında korunur).</summary>
    public bool KeepDiscordSteamStartup { get; set; } = true;
    /// <summary>Kullanıcı Bluetooth kullanmıyor: Bluetooth servisleri devre dışı bırakılır.</summary>
    public bool DisableBluetooth { get; set; }
    /// <summary>OneDrive oturum açılışında otomatik başlamasın.</summary>
    public bool DisableOneDriveStartup { get; set; } = true;

    public string ProfileDisplayName => Profile switch
    {
        OptimizationProfile.Ultra => "Ultra / Full+Full",
        OptimizationProfile.Extreme => "Extreme / Competitive",
        OptimizationProfile.Balanced => "Balanced (Günlük + Oyun)",
        _ => "Sadece Ağ & Ping"
    };
}
