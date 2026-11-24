using System;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using FiveMHaterOptimizer.Models;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>MODÜL 1: Sistem hazırlığı, ayrıcalıklar, donanım algılama, geri yükleme noktası, yedek ve başlangıç benchmark'ı.</summary>
public sealed class SystemPrepModule : IOptimizationModule
{
    public string Stage => "Sistem hazırlığı";
    public string Section => "Sistem hazırlığı";

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        skipReason = string.Empty;
        return true;
    }

    public async Task RunAsync(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        log.Info($"FiveMHater Optimizer v2.0.0 — Profil: {ctx.Options.ProfileDisplayName}");
        log.Info($"Seçenekler: FiveM önbellek temizliği = {(ctx.Options.ClearFiveMCache ? "Açık" : "Kapalı")}, " +
                 $"FiveM Competitive FPS = {(ctx.Options.ApplyFiveMFpsPreset ? "Açık" : "Kapalı")}, " +
                 $"Yazıcı/Tarayıcı servisleri kapat = {(ctx.Options.DisablePrinterServices ? "Açık" : "Kapalı")}");

        // --- Yönetici + ayrıcalıklar ---
        if (PrivilegeHelper.IsAdministrator()) log.Ok("Yönetici yetkisi doğrulandı.");
        else log.Error("Uygulama yönetici olarak çalışmıyor! Birçok ayar uygulanamayacak.");

        foreach (string priv in new[] { "SeDebugPrivilege", "SeIncreaseQuotaPrivilege", "SeShutdownPrivilege" })
        {
            if (PrivilegeHelper.EnablePrivilege(priv, out string err)) log.Ok($"{priv} etkinleştirildi.");
            else log.Warn($"{priv} etkinleştirilemedi: {err}");
        }
        ctx.SubProgress?.Invoke(0.1);

        // --- Donanım algılama (taze) ---
        log.Info("Donanım algılanıyor...");
        ctx.System = await Task.Run(HardwareDetector.Detect, ct).ConfigureAwait(false);
        var s = ctx.System;
        log.Detail($"İşletim sistemi: {s.OsCaption} (Build {s.OsBuild}) — {(s.IsWindows11 ? "Windows 11" : "Windows 10")}");
        string hybrid = s.IsHybrid ? $"Hibrit (P-Core: {s.PerformanceCores}, E-Core: {s.EfficiencyCores})" : "Hibrit değil";
        log.Detail($"CPU: {s.CpuName} — {s.CpuCores} çekirdek / {s.CpuThreads} iş parçacığı — {(s.IsAmdCpu ? "AMD" : s.IsIntelCpu ? "Intel" : "Diğer")} — {hybrid}{(s.IsX3D ? " — X3D V-Cache" : "")}");
        log.Detail($"RAM: {s.InstalledRamGB:0.#} GB kurulu ({s.TotalRamGB:0.0} GB kullanılabilir){(s.RamSpeedMHz > 0 ? $" @ {s.RamSpeedMHz} MHz" : "")}");
        foreach (var g in s.Gpus)
            log.Detail($"GPU: {g.Name} — {g.Vendor} — VRAM {g.VramGB:0.#} GB — Sürücü {g.DriverVersion}");
        if (s.Gpus.Count == 0) log.Warn("GPU algılanamadı.");
        log.Detail($"Sistem türü: {(s.IsLaptop ? "Bataryalı / Dizüstü" : "Masaüstü")} (Batarya: {(s.HasBattery ? "Var" : "Yok")})");
        log.Detail($"Sistem diski: {s.SystemDiskType}");
        foreach (var a in s.ActiveAdapters)
            log.Detail($"Aktif ağ: {a.Name} — {a.Description} — {(a.IsWireless ? "Wi-Fi" : "Ethernet")} {(a.SpeedMbps > 0 ? $"{a.SpeedMbps} Mbps" : "")}");
        if (s.ActiveAdapters.Count == 0) log.Warn("Ağ geçidi olan fiziksel ağ adaptörü bulunamadı.");
        ctx.SubProgress?.Invoke(0.3);

        // --- Sistem Geri Yükleme Noktası ---
        log.Info("Sistem Geri Yükleme Noktası oluşturuluyor: \"FiveMHater Pre-Optimization\" (bu işlem 1 dakikaya kadar sürebilir)...");
        // Windows varsayılan olarak 24 saatte bir nokta oluşturur; sıklık kısıtını yedekleyerek kaldırıyoruz.
        ctx.Reg.SetDword(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", 0, verify: false);
        await Task.Run(() => CreateRestorePoint(log), ct).ConfigureAwait(false);
        ctx.SubProgress?.Invoke(0.6);

        // --- JSON yedek durumu ---
        var d = ctx.Backup.Data;
        log.Ok($"JSON yedek: {AppPaths.BackupFile} ({(ctx.Backup.WasCreatedNow ? "ilk çalıştırma — yeni oluşturuldu" : $"mevcut yedek korunuyor, {d.Registry.Count} kayıt defteri değeri, {d.Services.Count} servis")})");
        log.Detail("Değiştirilecek her Registry değeri (eski değer, ValueKind veya Existed=false), servis Start tipi, güç planı GUID'si, DNS ve görev durumları değişiklikten ÖNCE yedeğe yazılır.");

        // --- Başlangıç benchmark'ı ---
        log.Info("Başlangıç benchmark'ı (1.1.1.1 ve 8.8.8.8'e 6'şar paket)...");
        ctx.Before = await BenchmarkService.MeasureAsync(log, "Önce", ct).ConfigureAwait(false);
        ctx.SubProgress?.Invoke(1.0);
    }

    private static void CreateRestorePoint(Logger log)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\default");
            scope.Connect();
            using var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            using ManagementBaseObject inParams = cls.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = "FiveMHater Pre-Optimization";
            inParams["RestorePointType"] = 12u; // MODIFY_SETTINGS
            inParams["EventType"] = 100u;       // BEGIN_SYSTEM_CHANGE
            using ManagementBaseObject outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null);
            uint rv = Convert.ToUInt32(outParams["ReturnValue"]);
            if (rv == 0) log.Ok("Sistem Geri Yükleme Noktası oluşturuldu.");
            else if (rv == 1058) log.Warn("Sistem Koruması (System Restore) bu bilgisayarda kapalı. JSON yedeği ile devam ediliyor. (Açmak için: sysdm.cpl → Sistem Koruması)");
            else log.Warn($"Geri yükleme noktası oluşturulamadı (kod {rv}). JSON yedeği ile devam ediliyor.");
        }
        catch (Exception ex)
        {
            log.Warn($"Geri yükleme noktası oluşturulamadı: {ex.Message}. JSON yedeği ile devam ediliyor.");
        }
    }
}
