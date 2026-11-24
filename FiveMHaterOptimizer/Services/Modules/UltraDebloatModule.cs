using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FiveMHaterOptimizer.Models;
using Microsoft.Win32;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>
/// MODÜL 2b (yalnızca Ultra / Full+Full): başlangıç uygulamaları, ek servisler, ek zamanlanmış görevler,
/// Widgets/Copilot/Teams/Phone Link/Edge ön yükleme kapatma. Her değişiklik backup.json'a yazılır ve
/// "Tam Geri Al" ile geri alınır. Donanıma/CPU modeline özel ayar YOKTUR (genel ve donanım bağımsız).
/// </summary>
public sealed class UltraDebloatModule : IOptimizationModule
{
    public string Stage => "Ultra: Başlangıç & Derin servis azaltma";
    public string Section => "Ultra / Full+Full — Başlangıç uygulamaları & derin arka plan azaltma";

    private const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const string ApprovedBaseLm = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const string AppModelStartup = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

    /// <summary>Başlangıçta ASLA kapatılmayan girişler (ad veya komut satırında geçen parçalar).</summary>
    private static readonly string[] StartupWhitelist =
    {
        // Windows / güvenlik
        "securityhealth", "windowsdefender", "msascui", "ctfmon", "fivemhater",
        // Ses sürücüleri
        "rtkaud", "realtek", "rthdvcpl", "ravcpl", "ravbg", "waves", "maxxaudio", "nahimic", "a-volute", "dolby", "sonicstudio",
        "conexant", "idtnc", "sbaudio", "creative", "voicemeeter",
        // Ekran kartı denetim masaları (sürücü özellikleri için korunur)
        "nvidia", "nvbackend", "nvcpl", "radeonsoftware", "amdrs", "cncmd", "igfx", "intelgraphics", "igcc",
        // Dokunmatik yüzey / çevre birimleri (DPI, makro, RGB profilleri bozulmasın)
        "synaptics", "syntp", "elan", "etdctrl", "alps", "lghub", "logitech", "razer", "synapse", "icue", "corsair", "steelseries",
        "hyperx", "ngenuity", "roccat", "wooting", "glorious", "bloody", "redragon",
        // Antivirüs / güvenlik yazılımları
        "avast", "avgui", "kaspersky", "avp", "eset", "egui", "bitdefender", "norton", "malwarebytes", "mbam", "mcafee", "sophos",
        // Anti-cheat / oyun
        "easyanticheat", "battleye", "vgtray", "vanguard", "faceit", "fivem", "citizenfx", "rockstar", "socialclub",
        // VPN / ağ araçları
        "nordvpn", "protonvpn", "expressvpn", "wireguard", "openvpn", "tailscale", "radmin", "hamachi", "zerotier", "cloudflare warp",
        // Bluetooth tepsi araçları
        "bluetooth", "btray"
    };

    private static readonly string[] DiscordSteam = { "discord", "steam" };

    /// <summary>Paketli (UWP) başlangıç görevlerinde korunan paket aileleri.</summary>
    private static readonly string[] PackageWhitelist =
    {
        "RealtekSemiconductor", "NVIDIACorp", "AdvancedMicroDevices", "AppUp.IntelGraphics", "Microsoft.SecHealthUI",
        "DolbyLaboratories", "WavesAudio", "Discord", "Valve"
    };

    private static readonly Regex UpdaterTaskRegex = new(
        @"^\\(MicrosoftEdgeUpdateTask|GoogleUpdateTask|GoogleUpdaterTask|Adobe Acrobat Update Task|AdobeGCInvoker|CCleaner|" +
        @"Opera (GX )?scheduled Autoupdate|BraveSoftwareUpdateTask|DropboxUpdateTask|Mozilla\\Firefox Default Browser Agent|" +
        @"OneDrive Reporting Task|NvTmRep|NVIDIA GeForce Experience SelfUpdate|NvProfileUpdater)",
        RegexOptions.IgnoreCase);

    private static readonly Regex OneDriveUpdateTaskRegex = new(@"^\\OneDrive Standalone Update Task", RegexOptions.IgnoreCase);

    public static readonly string[] UltraTasks =
    {
        @"\Microsoft\Office\OfficeTelemetryAgentLogOn2016",
        @"\Microsoft\Office\OfficeTelemetryAgentFallBack2016",
        @"\Microsoft\Office\OfficeTelemetryAgentLogOn",
        @"\Microsoft\Office\OfficeTelemetryAgentFallBack",
        @"\Microsoft\Office\Office ClickToRun Service Monitor",
        @"\Microsoft\Windows\Feedback\Siuf\DmClient",
        @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
        @"\Microsoft\Windows\Maps\MapsToastTask",
        @"\Microsoft\Windows\Maps\MapsUpdateTask",
        @"\Microsoft\Windows\CloudExperienceHost\CreateObjectTask",
        @"\Microsoft\Windows\Windows Error Reporting\QueueReporting",
        @"\Microsoft\Windows\Autochk\Proxy",
        @"\Microsoft\Windows\DiskFootprint\Diagnostics",
        @"\Microsoft\Windows\MemoryDiagnostic\ProcessMemoryDiagnosticEvents",
        @"\Microsoft\Windows\MemoryDiagnostic\RunFullMemoryDiagnostic",
        @"\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem",
        @"\Microsoft\XblGameSave\XblGameSaveTask",
        @"\Microsoft\Windows\Application Experience\PcaPatchDbTask",
        @"\Microsoft\Windows\Application Experience\MareBackup",
        @"\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask",
        @"\Microsoft\Windows\Device Information\Device",
        @"\Microsoft\Windows\Device Information\Device User",
        @"\Microsoft\Windows\Shell\FamilySafetyMonitor",
        @"\Microsoft\Windows\Shell\FamilySafetyRefreshTask",
        @"\Microsoft\Windows\Windows Media Sharing\UpdateLibrary",
        @"\Microsoft\Windows\Flighting\FeatureConfig\UsageDataReporting",
        @"\Microsoft\Windows\NetTrace\GatherNetworkInfo",
        @"\Microsoft\Windows\Maintenance\WinSAT",
        // v2.2 (kullanıcı onaylı ek liste)
        @"\Microsoft\Windows\Registry\RegIdleBackup",
        @"\Microsoft\Windows\RemoteAssistance\RemoteAssistanceTask",
        @"\Microsoft\Windows\Ras\MobilityManager",
        @"\Microsoft\Windows\Mobile Broadband Accounts\MNO Metadata Parser",
        @"\Microsoft\Windows\UPnP\UPnPHostConfig",
        @"\Microsoft\Windows\WDI\ResolutionHost",
        @"\Microsoft\Windows\Wininet\CacheTask",
        @"\Microsoft\Windows\SettingSync\BackgroundUploadTask",
        @"\Microsoft\Windows\SettingSync\NetworkStateChangeTask"
    };

    private static readonly string[] UltraKill =
    {
        "Teams", "ms-teams", "msteams", "CrossDeviceResume", "GameBarPresenceWriter", "GameBar", "Copilot",
        "PhoneExperienceHost", "YourPhone", "Widgets", "WidgetService", "MicrosoftEdgeUpdate", "GoogleUpdate", "updater",
        "AdobeARM", "OfficeClickToRun_Background"
    };

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        skipReason = "Bu adım yalnızca \"Ultra / Full+Full\" profilinde çalışır.";
        return ctx.IsUltra;
    }

    public Task RunAsync(OptimizationContext ctx, CancellationToken ct) => Task.Run(() => Run(ctx, ct), ct);

    private void Run(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        var o = ctx.Options;
        log.Info($"Ultra seçenekleri: Discord/Steam başlangıçta {(o.KeepDiscordSteamStartup ? "AÇIK kalır" : "kapatılır")}, " +
                 $"Bluetooth {(o.DisableBluetooth ? "KAPATILIR" : "korunur")}, OneDrive otomatik başlatma {(o.DisableOneDriveStartup ? "kapatılır" : "korunur")}.");
        log.Info("Asla dokunulmaz: ses, ağ, GPU sürücüsü, Windows Güvenliği/Defender, Windows Update çekirdeği, Explorer/Başlat/Görev çubuğu, oturum açma, bildirimler (WpnService), FiveM/GTA/Steam/Rockstar/Discord/EAC/BattlEye.");

        StartupApps(ctx, ct);
        ctx.SubProgress?.Invoke(0.25);
        Services(ctx, ct);
        ctx.SubProgress?.Invoke(0.55);
        Tasks(ctx, ct);
        ctx.SubProgress?.Invoke(0.75);
        Policies(ctx);
        ctx.SubProgress?.Invoke(0.9);
        KillExtra(ctx, ct);

        log.Info("Svchost: SvcHostSplitThresholdInKB kurulu RAM'e (KB) eşitlendi (önceki adım). Bu değer >= RAM olduğunda Windows aynı gruptaki servisleri tek svchost.exe içinde çalıştırır; 0xFFFFFFFF ile aynı etkiyi verir. Geri Al ile orijinal değere döner.");
        log.Info("ÖNEMLİ: İşlem sayısını ölçmek için yeniden başlattıktan sonra 2-3 dakika bekleyin (açılışta Windows geçici güncelleme/indeksleme süreçleri çalıştırır).");
        ctx.NeedsReboot("Ultra: başlangıç uygulamaları ve servis değişiklikleri bir sonraki açılışta tam etkinleşir");
        ctx.SubProgress?.Invoke(1.0);
    }

    // ===================== Başlangıç uygulamaları =====================

    private static bool IsWhitelisted(string name, string command, OptimizationOptions o, out string why)
    {
        string hay = (name + " " + command).ToLowerInvariant();
        foreach (string w in StartupWhitelist)
            if (hay.Contains(w)) { why = $"koruma listesi ({w})"; return true; }
        if (o.KeepDiscordSteamStartup)
            foreach (string w in DiscordSteam)
                if (hay.Contains(w)) { why = "kullanıcı seçimi: Discord/Steam başlangıçta açılsın"; return true; }
        if (!o.DisableOneDriveStartup && hay.Contains("onedrive")) { why = "kullanıcı seçimi: OneDrive korunuyor"; return true; }
        why = string.Empty;
        return false;
    }

    private static bool IsApprovedDisabled(Hive hive, string approvedPath, string name)
    {
        if (RegistryUtil.GetValue(hive, approvedPath, name) is byte[] b && b.Length > 0) return (b[0] & 1) == 1; // 02/06 = açık, 03/01 = kapalı
        return false;
    }

    private static byte[] DisabledBlob()
    {
        var blob = new byte[12];
        blob[0] = 0x03;
        BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(blob, 4);
        return blob;
    }

    private void StartupApps(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        log.Info("Başlangıç uygulamaları taranıyor (Run / RunOnce / StartupApproved / Başlangıç klasörleri / paketli uygulamalar):");
        int disabled = 0, kept = 0, already = 0;

        var sources = new List<(Hive hive, string runKey, string approved, string label)>
        {
            (Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Run", ApprovedBase + @"\Run", "HKCU Run"),
            (Hive.HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", ApprovedBaseLm + @"\Run", "HKLM Run"),
            (Hive.HKLM, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", ApprovedBaseLm + @"\Run32", "HKLM Run (32-bit)")
        };

        foreach (var (hive, runKey, approved, label) in sources)
        {
            foreach (var (name, cmd) in ReadValues(hive, runKey))
            {
                ct.ThrowIfCancellationRequested();
                Handle(ctx, hive, approved, name, cmd, label, ref disabled, ref kept, ref already);
            }
        }

        // RunOnce: tek seferlik kurulum adımları — yalnızca listelenir, dokunulmaz.
        foreach (var (hive, key) in new[] { (Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"), (Hive.HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce") })
            foreach (var (name, cmd) in ReadValues(hive, key))
                log.Detail($"{hive} RunOnce (tek seferlik, dokunulmadı): {name} → {cmd}");

        // Başlangıç klasörleri
        var folders = new List<(Hive hive, string dir, string label)>();
        try { folders.Add((Hive.HKCU, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Kullanıcı Başlangıç klasörü")); } catch { }
        try { folders.Add((Hive.HKLM, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Ortak Başlangıç klasörü")); } catch { }
        foreach (var (hive, dir, label) in folders)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            string approved = (hive == Hive.HKCU ? ApprovedBase : ApprovedBaseLm) + @"\StartupFolder";
            foreach (string file in Directory.GetFiles(dir))
            {
                string fname = Path.GetFileName(file);
                if (fname.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                Handle(ctx, hive, approved, fname, file, label, ref disabled, ref kept, ref already);
            }
        }

        // Paketli (Microsoft Store) uygulamaların başlangıç görevleri: Teams, Phone Link, Spotify vb.
        foreach (string pkg in RegistryUtil.GetSubKeyNames(Hive.HKCU, AppModelStartup))
        {
            foreach (string task in RegistryUtil.GetSubKeyNames(Hive.HKCU, $@"{AppModelStartup}\{pkg}"))
            {
                string path = $@"{AppModelStartup}\{pkg}\{task}";
                long? state = RegistryUtil.GetNumber(Hive.HKCU, path, "State");
                if (state == null) continue;
                if (state != 2 && state != 4) { already++; continue; } // 2 = açık, 4 = politika ile açık
                bool keep = PackageWhitelist.Any(w => pkg.Contains(w, StringComparison.OrdinalIgnoreCase)) ||
                            (ctx.Options.KeepDiscordSteamStartup && DiscordSteam.Any(w => pkg.Contains(w, StringComparison.OrdinalIgnoreCase)));
                if (keep) { kept++; log.Detail($"Korundu (paketli uygulama): {pkg}\\{task}"); continue; }
                if (ctx.Reg.SetDword(Hive.HKCU, path, "State", 1)) disabled++; // 1 = kullanıcı tarafından kapatıldı
            }
        }

        // Üçüncü taraf güncelleyicilerin oturum açma / periyodik görevleri
        log.Info("Üçüncü taraf güncelleyici zamanlanmış görevleri (Edge/Google/Adobe/CCleaner/Opera/Brave/Dropbox/NVIDIA telemetri):");
        var r = ProcessRunner.Run("schtasks.exe", "/Query /FO CSV /NH", null, 60_000, logCommand: false, logOutput: false);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (r.Success)
        {
            foreach (string line in r.Output.Split('\n'))
            {
                var m = Regex.Match(line.Trim(), "^\"([^\"]+)\"");
                if (!m.Success) continue;
                string tn = m.Groups[1].Value;
                if (!seen.Add(tn)) continue;
                bool match = UpdaterTaskRegex.IsMatch(tn) || (ctx.Options.DisableOneDriveStartup && OneDriveUpdateTaskRegex.IsMatch(tn));
                if (match) ctx.Tasks.Disable(tn);
            }
        }
        else log.Warn("Zamanlanmış görev listesi okunamadı; güncelleyici görevleri atlandı.");

        log.Ok($"Başlangıç: {disabled} giriş kapatıldı (StartupApproved, geri alınabilir), {kept} giriş korundu, {already} giriş zaten kapalıydı.");
    }

    private static IEnumerable<(string name, string cmd)> ReadValues(Hive hive, string path)
    {
        var list = new List<(string, string)>();
        try
        {
            using var b = RegistryUtil.OpenBase(hive);
            using var k = b.OpenSubKey(path, false);
            if (k == null) return list;
            foreach (string n in k.GetValueNames())
            {
                if (string.IsNullOrEmpty(n)) continue;
                list.Add((n, k.GetValue(n, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? ""));
            }
        }
        catch { }
        return list;
    }

    private static void Handle(OptimizationContext ctx, Hive hive, string approved, string name, string cmd, string label,
        ref int disabled, ref int kept, ref int already)
    {
        var log = ctx.Log;
        if (IsWhitelisted(name, cmd, ctx.Options, out string why))
        {
            kept++;
            log.Detail($"Korundu ({label}): {name} — {why}");
            return;
        }
        if (IsApprovedDisabled(hive, approved, name))
        {
            already++;
            log.Detail($"Zaten kapalı ({label}): {name}");
            return;
        }
        if (ctx.Reg.SetBinary(hive, approved, name, DisabledBlob()))
        {
            disabled++;
            log.Info($"Başlangıçta kapatıldı ({label}): {name} → {Truncate(cmd, 110)}");
        }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    // ===================== Servisler =====================

    private static bool IsVirtualMachine()
    {
        var rows = WmiUtil.Query(@"root\cimv2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem", "Manufacturer", "Model");
        if (rows.Count == 0) return true; // bilinmiyorsa güvenli taraf: VM varsay (vmic* servislerine dokunma)
        string s = (WmiUtil.Str(rows[0], "Manufacturer") + " " + WmiUtil.Str(rows[0], "Model")).ToLowerInvariant();
        return s.Contains("virtual") || s.Contains("vmware") || s.Contains("qemu") || s.Contains("kvm") || s.Contains("xen") || s.Contains("parallels") || s.Contains("innotek");
    }

    private static bool HypervisorInUse()
    {
        var cs = WmiUtil.Query(@"root\cimv2", "SELECT HypervisorPresent FROM Win32_ComputerSystem", "HypervisorPresent");
        if (cs.Count == 0) return true;
        if (cs[0].TryGetValue("HypervisorPresent", out var hp) && hp is bool b && b) return true;
        var cpu = WmiUtil.Query(@"root\cimv2", "SELECT VirtualizationFirmwareEnabled FROM Win32_Processor", "VirtualizationFirmwareEnabled");
        if (cpu.Count == 0) return true;
        return cpu.Any(r => r.TryGetValue("VirtualizationFirmwareEnabled", out var v) && v is bool e && e);
    }

    private static bool HasBiometricDevice()
    {
        var rows = WmiUtil.Query(@"root\cimv2", "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Biometric'", "Name");
        return rows.Count > 0;
    }

    private void Services(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        var plan = new List<(string name, ServiceStart start, string reason)>
        {
            ("CDPSvc", ServiceStart.Manual, "Bağlı Cihazlar Platformu (gerekince başlar)"),
            ("CDPUserSvc", ServiceStart.Manual, "Bağlı Cihazlar kullanıcı servisi (şablon, gerekince başlar)"),
            ("OneSyncSvc", ServiceStart.Disabled, "Posta/Kişi/Takvim eşitleme (şablon)"),
            ("MessagingService", ServiceStart.Disabled, "SMS/mesaj aktarımı (şablon)"),
            ("PimIndexMaintenanceSvc", ServiceStart.Disabled, "Kişi verisi indeksleme (şablon)"),
            ("BcastDVRUserService", ServiceStart.Disabled, "Game DVR yayın servisi (şablon)"),
            ("cbdhsvc", ServiceStart.Manual, "Pano geçmişi servisi (Win+V gerekince başlar; normal kopyala-yapıştır etkilenmez)"),
            ("XblAuthManager", ServiceStart.Manual, "Xbox Live kimlik (gerekince başlar)"),
            ("XblGameSave", ServiceStart.Manual, "Xbox oyun kaydı (gerekince başlar)"),
            ("XboxNetApiSvc", ServiceStart.Manual, "Xbox ağ (gerekince başlar)"),
            ("XboxGipSvc", ServiceStart.Manual, "Xbox aksesuar (kol takılınca tetiklenir)"),
            ("WMPNetworkSvc", ServiceStart.Disabled, "Windows Media Player ağ paylaşımı"),
            ("icssvc", ServiceStart.Disabled, "Mobil etkin nokta (Windows hotspot)"),
            ("SharedAccess", ServiceStart.Manual, "İnternet Bağlantısı Paylaşımı"),
            ("lmhosts", ServiceStart.Manual, "NetBIOS yardımcı"),
            ("SSDPSRV", ServiceStart.Manual, "SSDP keşfi (gerekince başlar)"),
            ("upnphost", ServiceStart.Manual, "UPnP aygıt ana bilgisayarı"),
            ("fdPHost", ServiceStart.Manual, "İşlev bulma sağlayıcısı"),
            ("FDResPub", ServiceStart.Manual, "İşlev bulma kaynak yayını"),
            ("SCardSvr", ServiceStart.Manual, "Akıllı kart"),
            ("ScDeviceEnum", ServiceStart.Manual, "Akıllı kart aygıt numaralandırma"),
            ("TapiSrv", ServiceStart.Manual, "Telefon API"),
            ("wercplsupport", ServiceStart.Manual, "Sorun raporları denetim masası"),
            ("WerSvc", ServiceStart.Manual, "Hata raporlama"),
            ("PcaSvc", ServiceStart.Disabled, "Program Uyumluluk Yardımcısı"),
            ("TrkWks", ServiceStart.Disabled, "Dağıtılmış bağlantı izleme"),
            ("DPS", ServiceStart.Manual, "Tanılama İlkesi (sorun gidericiler gerekince başlatır)"),
            ("diagsvc", ServiceStart.Manual, "Tanılama yürütme"),
            ("TroubleshootingSvc", ServiceStart.Manual, "Önerilen sorun giderme"),
            ("InventorySvc", ServiceStart.Manual, "Envanter ve uyumluluk değerlendirme"),
            ("whesvc", ServiceStart.Manual, "Windows Sağlık ve Optimize Deneyimler"),
            ("AJRouter", ServiceStart.Disabled, "AllJoyn yönlendirici (IoT)"),
            ("SEMgrSvc", ServiceStart.Disabled, "Ödemeler ve NFC/SE"),
            ("WpcMonSvc", ServiceStart.Disabled, "Ebeveyn denetimleri"),
            ("SmsRouter", ServiceStart.Disabled, "SMS yönlendirici"),
            ("NetTcpPortSharing", ServiceStart.Disabled, "Net.Tcp port paylaşımı"),
            ("perceptionsimulation", ServiceStart.Disabled, "Karma gerçeklik simülasyonu"),
            ("MixedRealityOpenXRSvc", ServiceStart.Manual, "Karma gerçeklik OpenXR"),
            ("spectrum", ServiceStart.Manual, "Windows Algılama"),
            ("lltdsvc", ServiceStart.Manual, "Bağlantı katmanı topoloji bulma"),
            ("QWAVE", ServiceStart.Manual, "Kaliteli Windows Ses Video Deneyimi"),
            ("WiaRpc", ServiceStart.Manual, "Hareketsiz görüntü olayları"),
            ("PeerDistSvc", ServiceStart.Disabled, "BranchCache"),
            ("CscService", ServiceStart.Disabled, "Çevrimdışı Dosyalar"),
            ("WalletService", ServiceStart.Disabled, "Cüzdan"),
            ("edgeupdate", ServiceStart.Manual, "Edge güncelleyici (Edge açılınca güncellenir)"),
            ("edgeupdatem", ServiceStart.Manual, "Edge güncelleyici"),
            ("MicrosoftEdgeElevationService", ServiceStart.Manual, "Edge yükseltme servisi"),
            ("gupdate", ServiceStart.Manual, "Google güncelleyici"),
            ("gupdatem", ServiceStart.Manual, "Google güncelleyici"),
            ("AdobeARMservice", ServiceStart.Manual, "Adobe Acrobat güncelleyici"),
            ("MozillaMaintenance", ServiceStart.Manual, "Mozilla bakım servisi"),
            ("brave", ServiceStart.Manual, "Brave güncelleyici"),
            ("bravem", ServiceStart.Manual, "Brave güncelleyici"),
            // v2.2 (kullanıcı onaylı ek liste)
            ("TermService", ServiceStart.Disabled, "Uzak Masaüstü Hizmetleri (bu PC'ye RDP ile bağlanılamaz)"),
            ("SessionEnv", ServiceStart.Disabled, "Uzak Masaüstü yapılandırma"),
            ("UmRdpService", ServiceStart.Disabled, "Uzak Masaüstü UserMode bağlantı yönlendirici"),
            ("RemoteAccess", ServiceStart.Disabled, "Yönlendirme ve Uzaktan Erişim"),
            ("NcbService", ServiceStart.Disabled, "Ağ Bağlantısı Aracısı"),
            ("SCPolicySvc", ServiceStart.Disabled, "Akıllı kart çıkarma ilkesi"),
            ("AssignedAccessManagerSvc", ServiceStart.Disabled, "Atanmış Erişim (kiosk modu)"),
            ("NaturalAuthentication", ServiceStart.Disabled, "Doğal Kimlik Doğrulama (Dinamik Kilit)")
        };

        // Yeni Google Updater servisleri sürüm numaralı adlar taşır (GoogleUpdaterService128.0... vb.).
        try
        {
            foreach (var sc in ServiceController.GetServices())
            {
                using (sc)
                    if (sc.ServiceName.StartsWith("GoogleUpdater", StringComparison.OrdinalIgnoreCase))
                        plan.Add((sc.ServiceName, ServiceStart.Manual, "Google Updater (Chrome açılınca güncellenir)"));
            }
        }
        catch { }

        if (!IsVirtualMachine())
        {
            foreach (string v in new[] { "vmickvpexchange", "vmicguestinterface", "vmicshutdown", "vmicheartbeat", "vmicvmsession", "vmicrdv", "vmictimesync", "vmicvss" })
                plan.Add((v, ServiceStart.Disabled, "Hyper-V konuk servisi (bu bilgisayar sanal makine değil)"));
        }
        else log.Detail("Sanal makine algılandı (veya belirlenemedi): Hyper-V konuk servisleri (vmic*) korunuyor.");

        if (!HypervisorInUse())
            plan.Add(("HvHost", ServiceStart.Disabled, "Hyper-V ana bilgisayar servisi (hipervizör çalışmıyor, BIOS sanallaştırması kapalı)"));
        else log.Detail("Hipervizör/sanallaştırma etkin (Hyper-V, WSL2, Bellek Bütünlüğü/VBS olabilir): HvHost korunuyor.");

        if (!HasBiometricDevice())
            plan.Add(("WbioSrvc", ServiceStart.Disabled, "Biyometrik servis (parmak izi / Windows Hello yüz cihazı bulunamadı)"));
        else log.Detail("Biyometrik cihaz algılandı: WbioSrvc (Windows Hello) korunuyor.");

        if (ctx.Options.DisableBluetooth)
        {
            log.Warn("Kullanıcı seçimi \"Bluetooth kullanmıyorum\": Bluetooth servisleri devre dışı bırakılıyor (Bluetooth kulaklık/kol/mouse çalışmaz; Geri Al ile döner).");
            plan.Add(("bthserv", ServiceStart.Disabled, "Bluetooth destek servisi (kullanıcı seçimi)"));
            plan.Add(("BTAGService", ServiceStart.Disabled, "Bluetooth ses ağ geçidi (kullanıcı seçimi)"));
            plan.Add(("BthAvctpSvc", ServiceStart.Disabled, "Bluetooth AVCTP (kullanıcı seçimi)"));
            plan.Add(("BluetoothUserService", ServiceStart.Disabled, "Bluetooth kullanıcı servisi (şablon, kullanıcı seçimi)"));
        }
        else log.Detail("Bluetooth servisleri korunuyor (\"Bluetooth kullanmıyorum\" kapalı).");

        log.Info("Ultra servis planı (WpnService/bildirimler, TabletInputService/TextInput, UnistoreSvc, UserDataSvc, AppXSvc, StateRepository, DoSvc, InstallService ve tüm güvenlik/ses/ağ servisleri bilinçli olarak korunur):");
        int i = 0;
        foreach (var (name, start, reason) in plan)
        {
            ct.ThrowIfCancellationRequested();
            ctx.Services.SetStart(name, start, reason);
            ctx.SubProgress?.Invoke(0.25 + 0.3 * (++i) / Math.Max(1, plan.Count));
        }

        // Kullanıcı başına servis örneklerini (ör. CDPUserSvc_1a2b3) şimdi durdur; şablon ayarı bir sonraki oturumda uygulanır.
        string[] templates = { "CDPUserSvc", "OneSyncSvc", "MessagingService", "PimIndexMaintenanceSvc", "BcastDVRUserService" };
        if (ctx.Options.DisableBluetooth) templates = templates.Append("BluetoothUserService").ToArray();
        try
        {
            foreach (var sc in ServiceController.GetServices())
            {
                using (sc)
                {
                    int us = sc.ServiceName.IndexOf('_');
                    if (us <= 0) continue;
                    string baseName = sc.ServiceName[..us];
                    if (!templates.Contains(baseName, StringComparer.OrdinalIgnoreCase)) continue;
                    if (sc.Status != ServiceControllerStatus.Running || !sc.CanStop) continue;
                    try
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        log.Detail($"Kullanıcı servisi örneği durduruldu: {sc.ServiceName}");
                    }
                    catch (Exception ex) { log.Detail($"{sc.ServiceName} durdurulamadı ({ex.Message}); yeniden başlatmada uygulanır."); }
                }
            }
        }
        catch { }

        log.Ok($"Ultra servisleri işlendi ({plan.Count} servis değerlendirildi). Toplam: {ctx.Services.ChangedCount} başlangıç tipi değişikliği.");
    }

    // ===================== Zamanlanmış görevler =====================

    private void Tasks(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        log.Info("Ek zamanlanmış görevler kapatılıyor (Office telemetri, Geri bildirim/Siuf, Haritalar, Hata raporlama kuyruğu, Autochk proxy, DiskFootprint, Bellek tanılama, Güç verimliliği tanılama, Xbox Live):");
        foreach (string t in UltraTasks)
        {
            ct.ThrowIfCancellationRequested();
            ctx.Tasks.Disable(t);
        }
        log.Detail("Korunan görev: \\Microsoft\\Windows\\Defrag\\ScheduledDefrag (SSD'de TRIM/Optimize için gereklidir).");
    }

    // ===================== İlkeler =====================

    private void Policies(OptimizationContext ctx)
    {
        var reg = ctx.Reg;
        var log = ctx.Log;
        log.Info("Widgets / Copilot / Teams Sohbet / Arama vurguları / Cortana / Phone Link / Edge ön yükleme / Teslim Optimizasyonu P2P kapatılıyor:");

        const string adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        if (!reg.SetDword(Hive.HKCU, adv, "TaskbarDa", 0))
            log.Detail("TaskbarDa bazı Windows 11 sürümlerinde korumalıdır; Widgets zaten AllowNewsAndInterests=0 ilkesiyle kapatıldı.");
        reg.SetDword(Hive.HKCU, adv, "TaskbarMn", 0);
        reg.SetDword(Hive.HKCU, adv, "ShowCopilotButton", 0);
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Chat", "ChatIcon", 3);

        const string ws = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search";
        reg.SetDword(Hive.HKLM, ws, "AllowCortana", 0);
        reg.SetDword(Hive.HKLM, ws, "EnableDynamicContentInWSB", 0);
        reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0);

        const string yp = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\Microsoft.YourPhone_8wekyb3d8bbwe";
        reg.SetDword(Hive.HKCU, yp, "Disabled", 1);
        reg.SetDword(Hive.HKCU, yp, "DisabledByUser", 1);

        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\MicrosoftEdge\Main", "AllowPrelaunch", 0);
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\MicrosoftEdge\TabPreloader", "AllowTabPreloading", 0);

        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0);
        log.Detail("Teslim Optimizasyonu: yalnızca HTTP (başka PC'lere yükleme yok → oyun sırasında bant genişliği boşa gitmez). Windows Update çalışmaya devam eder.");

        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1);
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);

        const string gbp = @"SOFTWARE\Microsoft\WindowsRuntime\ActivatableClassId\Windows.Gaming.GameBar.PresenceServer.Internal.PresenceWriter";
        if (RegistryUtil.KeyExists(Hive.HKLM, gbp))
        {
            if (!reg.SetDword(Hive.HKLM, gbp, "ActivationType", 0))
                log.Detail("GameBarPresenceWriter anahtarı TrustedInstaller korumalı; GameDVR ilkesi kapalı olduğu için süreç genelde başlamaz.");
        }

        if (ctx.Options.DisableOneDriveStartup)
            log.Detail("OneDrive: otomatik başlatma kapatıldı (dosyalarınız silinmez; OneDrive'ı Başlat menüsünden açınca eşitleme devam eder).");
    }

    // ===================== Ek süreçler =====================

    private void KillExtra(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        log.Info("Ek arka plan süreçleri kapatılıyor (Teams, Phone Link, Copilot, GameBar, güncelleyiciler):");
        var targets = UltraKill.ToList();
        if (ctx.Options.DisableOneDriveStartup) targets.Add("OneDrive");
        int killed = 0;
        foreach (string name in targets)
        {
            ct.ThrowIfCancellationRequested();
            if (ServiceProcessModule.NeverKill.Contains(name)) continue;
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); } catch { continue; }
            foreach (var p in procs)
            {
                try
                {
                    string pname = p.ProcessName;
                    int pid = p.Id;
                    if (pid == Environment.ProcessId || ServiceProcessModule.NeverKill.Contains(pname)) continue;
                    // "updater" adı genel olduğu için yalnızca Google yolundakiler kapatılır.
                    if (name == "updater")
                    {
                        string? path = null;
                        try { path = p.MainModule?.FileName; } catch { }
                        if (path == null || !path.Contains(@"\Google\", StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    p.Kill(false);
                    p.WaitForExit(3000);
                    killed++;
                    log.Detail($"Sonlandırıldı: {pname}.exe (PID {pid})");
                }
                catch (Exception ex) { log.Detail($"{name}.exe sonlandırılamadı: {ex.Message}"); }
                finally { p.Dispose(); }
            }
        }
        ctx.KilledProcesses += killed;
        log.Ok($"Ultra: {killed} ek süreç kapatıldı.");
    }
}
