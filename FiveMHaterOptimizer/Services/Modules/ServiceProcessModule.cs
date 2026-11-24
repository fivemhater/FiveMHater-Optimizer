using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>MODÜL 2: Arka plan & işlem sayısı azaltma (svchost birleştirme, servisler, görevler, UWP/Edge/Widgets, tray süreçleri).</summary>
public sealed class ServiceProcessModule : IOptimizationModule
{
    public string Stage => "Servis & İşlem azaltma";
    public string Section => "Arka plan & İşlem sayısı azaltma";

    /// <summary>ASLA sonlandırılmayacak süreçler (güvenlik ağı).</summary>
    public static readonly HashSet<string> NeverKill = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "csrss", "winlogon", "wininit", "lsass", "services", "smss", "svchost", "System", "Idle", "Registry",
        "fontdrvhost", "sihost", "ctfmon", "audiodg", "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray",
        "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "RuntimeBroker", "conhost", "taskhostw", "spoolsv",
        "FiveM", "FiveM_GTAProcess", "FiveM_ChromeBrowser", "FiveM_SteamChild", "GTA5", "GTA5_Enhanced", "PlayGTAV",
        "Discord", "DiscordPTB", "DiscordCanary", "steam", "steamwebhelper", "steamservice", "Rockstar", "RockstarService",
        "LauncherPatcher", "SocialClubHelper", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "vgc", "FiveMHaterOptimizer"
    };

    private static readonly string[] HeavyExtreme =
    {
        "OneDrive", "MicrosoftEdgeUpdate", "AdobeARM", "AdobeCollabSync", "jusched", "jucheck", "CCleaner", "CCleaner64",
        "CCleanerBrowser", "Cortana", "Widgets", "WidgetService", "PhoneExperienceHost", "YourPhone", "GoogleUpdate",
        "SkypeApp", "SkypeBackgroundHost", "OfficeClickToRun_Background"
    };

    private static readonly string[] HeavyBalanced =
    {
        "MicrosoftEdgeUpdate", "AdobeARM", "jusched", "jucheck", "GoogleUpdate", "CCleaner", "CCleaner64"
    };

    public static readonly string[] TelemetryTasks =
    {
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Application Experience\StartupAppTask",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector"
    };

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        skipReason = "\"Sadece Ağ & Ping\" profili sistem ve servislere dokunmaz.";
        return !ctx.IsNetworkOnly;
    }

    public Task RunAsync(OptimizationContext ctx, CancellationToken ct) => Task.Run(() => Run(ctx, ct), ct);

    private void Run(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        var reg = ctx.Reg;
        var s = ctx.System;

        // ---- Svchost birleştirme ----
        log.Info("Svchost birleştirme (SvcHostSplitThresholdInKB):");
        ulong kb = s.InstalledRamKB > 0 ? s.InstalledRamKB : s.TotalRamBytes / 1024;
        // Kurulu RAM'i bir sonraki tam GB'a yuvarla ki eşik >= RAM olsun (svchost grupları birleşik kalsın)
        ulong gbCeil = (ulong)Math.Ceiling(kb / 1048576.0);
        ulong threshold = Math.Max(gbCeil * 1048576UL, 4UL * 1048576UL);
        if (threshold > uint.MaxValue) threshold = uint.MaxValue;
        reg.SetDword(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", (uint)threshold);
        log.Detail($"{gbCeil} GB RAM için eşik {threshold} KB. svchost.exe süreçleri yeniden başlatma sonrası birleşir (tipik olarak 40-60+ daha az işlem).");
        ctx.NeedsReboot("Svchost birleştirme (SvcHostSplitThresholdInKB)");
        ctx.SubProgress?.Invoke(0.1);

        // ---- Servisler ----
        log.Info($"Koruma listesi (dokunulmaz): {string.Join(", ", new[] { "WinDefend", "mpssvc", "BFE", "Dhcp", "Dnscache", "NlaSvc", "nsi", "AudioSrv", "AudioEndpointBuilder", "RpcSs", "DcomLaunch", "Steam Client Service", "EasyAntiCheat", "BEService", "GPU sürücü servisleri" })}");
        var plan = BuildServicePlan(ctx);
        int i = 0;
        foreach (var (name, start, reason) in plan)
        {
            ct.ThrowIfCancellationRequested();
            ctx.Services.SetStart(name, start, reason);
            ctx.SubProgress?.Invoke(0.1 + 0.4 * (++i) / Math.Max(1, plan.Count));
        }
        log.Info("DusmSvc (Veri kullanımı) korunuyor: ölçülü bağlantı/veri limiti özelliklerini bozmamak için dokunulmadı; yerine PcaSvc El ile yapıldı.");
        log.Ok($"Servisler: {ctx.Services.ChangedCount} başlangıç tipi değişti, {ctx.Services.StoppedCount} servis hemen durduruldu.");

        // ---- Zamanlanmış görevler ----
        log.Info("Telemetri zamanlanmış görevleri kapatılıyor (schtasks):");
        foreach (string t in TelemetryTasks)
        {
            ct.ThrowIfCancellationRequested();
            ctx.Tasks.Disable(t);
        }
        ctx.SubProgress?.Invoke(0.65);

        // ---- Arka plan uygulamaları / UWP ----
        if (ctx.IsExtreme)
        {
            log.Info("Arka plan UWP uygulamaları durduruluyor:");
            reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1);
            reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0);
            reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2);
        }
        else
        {
            log.Detail("Balanced: UWP arka plan uygulamaları (bildirimler/posta) korunuyor.");
        }

        // ---- Gizlilik (Extreme + Ultra, v2.2) ----
        if (ctx.IsExtreme)
        {
            log.Info("Gizlilik ayarları (telemetri, reklam kimliği, etkinlik geçmişi, geri bildirim, Wi-Fi Sense):");
            reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
            log.Detail("AllowTelemetry=0: Home/Pro sürümlerinde Windows bunu en düşük desteklenen düzey (1 = Gerekli) olarak uygular.");
            reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0);
            reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", 0);
            reg.SetDword(Hive.HKLM, @"SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config", "AutoConnectAllowedOEM", 0);
            reg.SetDword(Hive.HKCU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0);
            reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0);
            reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0);
            reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0);
            reg.SetString(Hive.HKCU, @"Control Panel\Desktop", "MenuShowDelay", "0");
        }

        // ---- Microsoft Edge arka planı ----
        log.Info("Microsoft Edge arka plan süreçleri kapatılıyor:");
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0);
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0);

        // ---- Widgets / News / Copilot / Arama önerileri / Reklamlar ----
        log.Info("Widgets, Haberler, Copilot, arama önerileri ve sessiz uygulama yükleme/reklamlar kapatılıyor:");
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0);
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds", 0);
        reg.SetDword(Hive.HKCU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
        reg.SetDword(Hive.HKCU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1);
        reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0);
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1);
        const string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        foreach (string v in new[]
                 {
                     "SilentInstalledAppsEnabled", "SystemPaneSuggestionsEnabled", "SoftLandingEnabled", "PreInstalledAppsEnabled",
                     "OemPreInstalledAppsEnabled", "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled",
                     "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled", "SubscribedContent-310093Enabled"
                 })
        {
            reg.SetDword(Hive.HKCU, cdm, v, 0);
        }
        ctx.SubProgress?.Invoke(0.8);

        // ---- Ağır arka plan süreçlerini sonlandır ----
        string[] targets = ctx.IsExtreme ? HeavyExtreme : HeavyBalanced;
        log.Info("Oyuna gerekmeyen arka plan güncelleyici/tray süreçleri sonlandırılıyor (explorer, dwm, csrss, FiveM, Discord, Steam ASLA kapatılmaz):");
        int killed = 0;
        foreach (string name in targets)
        {
            ct.ThrowIfCancellationRequested();
            if (NeverKill.Contains(name)) continue;
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { continue; }
            foreach (var p in procs)
            {
                try
                {
                    string pname = p.ProcessName;
                    int pid = p.Id;
                    if (pid == Environment.ProcessId || NeverKill.Contains(pname)) continue;
                    p.Kill(false);
                    p.WaitForExit(3000);
                    killed++;
                    log.Detail($"Sonlandırıldı: {pname}.exe (PID {pid})");
                }
                catch (Exception ex)
                {
                    log.Detail($"{name}.exe sonlandırılamadı: {ex.Message}");
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        ctx.KilledProcesses += killed;
        log.Ok($"{killed} arka plan süreci kapatıldı. (Bu süreçler bir sonraki oturumda kendi ayarlarına göre tekrar açılabilir.)");
        ctx.SubProgress?.Invoke(1.0);
    }

    private static List<(string name, ServiceStart start, string reason)> BuildServicePlan(OptimizationContext ctx)
    {
        var s = ctx.System;
        var plan = new List<(string, ServiceStart, string)>();
        if (ctx.IsExtreme)
        {
            plan.Add(("DiagTrack", ServiceStart.Disabled, "Telemetri"));
            plan.Add(("dmwappushservice", ServiceStart.Disabled, "Telemetri WAP push"));
            plan.Add(("diagnosticshub.standardcollector.service", ServiceStart.Disabled, "Tanılama toplayıcı"));
            plan.Add(("WdiServiceHost", ServiceStart.Manual, "Tanılama ana bilgisayarı"));
            plan.Add(("WdiSystemHost", ServiceStart.Manual, "Tanılama sistem ana bilgisayarı"));
            plan.Add(("WerSvc", ServiceStart.Manual, "Hata raporlama"));
            plan.Add(("PcaSvc", ServiceStart.Manual, "Program Uyumluluk Yardımcısı"));
            if (s.IsSystemDiskKnown && s.IsSystemDiskSsd)
                plan.Add(("SysMain", ServiceStart.Disabled, $"Superfetch — sistem diski {s.SystemDiskType}"));
            else
                ctx.Log.Detail($"SysMain korunuyor: sistem diski {s.SystemDiskType} (HDD/bilinmeyen diskte Superfetch faydalıdır).");
            plan.Add(("WSearch", ServiceStart.Disabled, "Arama indeksleme (oyun sırasında disk I/O düşüşü)"));
            plan.Add(("MapsBroker", ServiceStart.Disabled, "İndirilen haritalar"));
            plan.Add(("RetailDemo", ServiceStart.Disabled, "Mağaza demo"));
            plan.Add(("Fax", ServiceStart.Disabled, "Faks"));
            plan.Add(("wisvc", ServiceStart.Disabled, "Windows Insider"));
            plan.Add(("RemoteRegistry", ServiceStart.Disabled, "Uzak kayıt defteri (güvenlik)"));
            plan.Add(("TrkWks", ServiceStart.Manual, "Dağıtılmış bağlantı izleme"));
            plan.Add(("lfsvc", ServiceStart.Manual, "Coğrafi konum"));
            plan.Add(("PhoneSvc", ServiceStart.Manual, "Telefon hizmeti"));
            plan.Add(("SensorService", ServiceStart.Manual, "Sensör"));
            plan.Add(("SensorDataService", ServiceStart.Manual, "Sensör verisi"));
            plan.Add(("SensrSvc", ServiceStart.Manual, "Sensör izleme"));
            plan.Add(("WalletService", ServiceStart.Disabled, "Cüzdan"));
            plan.Add(("FrameServer", ServiceStart.Manual, "Kamera çerçeve sunucusu (kamera açılınca otomatik başlar)"));
        }
        else
        {
            plan.Add(("DiagTrack", ServiceStart.Disabled, "Telemetri"));
            plan.Add(("dmwappushservice", ServiceStart.Disabled, "Telemetri WAP push"));
            plan.Add(("diagnosticshub.standardcollector.service", ServiceStart.Disabled, "Tanılama toplayıcı"));
            plan.Add(("WerSvc", ServiceStart.Manual, "Hata raporlama"));
            plan.Add(("MapsBroker", ServiceStart.Manual, "İndirilen haritalar"));
            plan.Add(("RetailDemo", ServiceStart.Disabled, "Mağaza demo"));
            plan.Add(("RemoteRegistry", ServiceStart.Disabled, "Uzak kayıt defteri (güvenlik)"));
            plan.Add(("WalletService", ServiceStart.Manual, "Cüzdan"));
            ctx.Log.Detail("Balanced: Yazıcı, Arama (WSearch), Tablet girişi, SysMain ve sensör servisleri korunuyor.");
        }

        if (ctx.Options.DisablePrinterServices)
        {
            plan.Add(("Spooler", ServiceStart.Disabled, "Yazıcı biriktirici (kullanıcı seçimi)"));
            plan.Add(("PrintNotify", ServiceStart.Manual, "Yazıcı bildirimleri (kullanıcı seçimi)"));
            plan.Add(("StiSvc", ServiceStart.Manual, "Tarayıcı/Kamera görüntü alma (kullanıcı seçimi)"));
        }
        else
        {
            ctx.Log.Detail("Yazıcı & tarayıcı servisleri korunuyor (seçenek kapalı).");
        }

        if (s.HasNvidia) plan.Add(("NvTelemetryContainer", ServiceStart.Disabled, "NVIDIA telemetri"));
        return plan;
    }
}
