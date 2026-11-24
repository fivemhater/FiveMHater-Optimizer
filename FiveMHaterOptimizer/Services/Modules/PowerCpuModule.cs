using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FiveMHaterOptimizer.Models;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>MODÜL 4: Güç planı (yalnızca AC), CPU zamanlayıcı, kernel/BCD, MMCSS ve PCIe MSI modu.</summary>
public sealed class PowerCpuModule : IOptimizationModule
{
    public string Stage => "Güç planı & CPU";
    public string Section => "Güç planı & CPU Zamanlayıcı";

    public const string MmcssKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    public const string GamesTaskKey = MmcssKey + @"\Tasks\Games";

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        skipReason = "\"Sadece Ağ & Ping\" profili güç planı ve CPU ayarlarına dokunmaz.";
        return !ctx.IsNetworkOnly;
    }

    public Task RunAsync(OptimizationContext ctx, CancellationToken ct) => Task.Run(() => Run(ctx, ct), ct);

    private void Run(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        var reg = ctx.Reg;
        var s = ctx.System;

        // ---- Güç planı ----
        log.Info("Güç planı: Ultimate Performance çoğaltılıyor (yalnızca AC/priz değerleri değişir; DC/pil değerlerine dokunulmaz).");
        string? active = PowerService.GetActiveScheme(log);
        if (active != null) ctx.Backup.RecordPowerOriginal(active);
        log.Detail($"Mevcut aktif plan: {active ?? "okunamadı"} (yedeklendi)");

        string? scheme = ctx.Backup.Data.Power?.CreatedScheme;
        if (!string.IsNullOrEmpty(scheme) && PowerService.SchemeExists(scheme))
        {
            log.Detail($"Önceki çalıştırmada oluşturulan FiveMHater planı yeniden kullanılıyor: {scheme}");
        }
        else
        {
            scheme = PowerService.Duplicate(PowerService.UltimatePerformance, log);
            if (scheme == null)
            {
                log.Warn("Ultimate Performance bu sürümde çoğaltılamadı; Yüksek Performans planı temel alınıyor.");
                scheme = PowerService.Duplicate(PowerService.HighPerformance, log);
            }
            if (scheme != null)
            {
                ctx.Backup.SetCreatedScheme(scheme);
                ProcessRunner.Run("powercfg.exe", $"-changename {scheme} \"FiveMHater Ultimate\" \"FiveMHater Optimizer tarafından oluşturuldu (AC odaklı)\"", log, 30_000);
            }
        }

        if (scheme != null)
        {
            bool laptop = s.IsLaptop;
            int boost = laptop ? 4 : 2;
            var settings = new List<(string sub, string setting, int value, string label)>
            {
                (PowerService.SubProcessor, PowerService.CpMinCores, 100, "Core Parking Minimum %100 (CPMINCORES)"),
                (PowerService.SubProcessor, PowerService.CpMaxCores, 100, "Core Parking Maksimum %100 (CPMAXCORES)"),
                (PowerService.SubProcessor, PowerService.ProcThrottleMin, 100, "Minimum İşlemci Durumu %100"),
                (PowerService.SubProcessor, PowerService.ProcThrottleMax, 100, "Maksimum İşlemci Durumu %100"),
                (PowerService.SubProcessor, PowerService.PerfBoostMode, boost, laptop ? "Boost Modu 4 (Efficient Aggressive — dizüstü ısı koruması)" : "Boost Modu 2 (Aggressive — masaüstü)"),
                (PowerService.SubUsb, PowerService.UsbSelectiveSuspend, 0, "USB Seçici Askıya Alma Kapalı"),
                (PowerService.SubPciExpress, PowerService.PciAspm, 0, "PCIe ASPM Kapalı")
            };
            if (s.IsHybrid)
            {
                settings.Add((PowerService.SubProcessor, PowerService.HeteroSchedPolicy, 2, "Hibrit CPU: uzun iş parçacıkları P-Core'ları tercih etsin"));
                settings.Add((PowerService.SubProcessor, PowerService.HeteroShortSchedPolicy, 2, "Hibrit CPU: kısa iş parçacıkları P-Core'ları tercih etsin"));
            }

            foreach (var (sub, setting, value, label) in settings)
            {
                ct.ThrowIfCancellationRequested();
                if (PowerService.SetAc(scheme, sub, setting, value, log))
                {
                    log.Ok(label);
                    string sch = scheme, sb = sub, st = setting;
                    int v = value;
                    ctx.Verify.Add($"power:{setting}", $"Güç: {label}", () => PowerService.ReadAc(sch, sb, st) == v);
                }
                else
                {
                    log.Warn($"{label} uygulanamadı (bu donanımda ayar mevcut olmayabilir).");
                }
            }

            var r = ProcessRunner.Run("powercfg.exe", $"/setactive {scheme}", log, 30_000);
            if (r.Success)
            {
                log.Ok($"Aktif güç planı: FiveMHater Ultimate ({scheme})");
                string sch = scheme;
                ctx.Verify.Add("power:active", "Aktif güç planı = FiveMHater Ultimate", () => PowerService.GetActiveScheme() == sch);
            }
        }
        else
        {
            log.Warn("Güç planı oluşturulamadı; mevcut plan korunuyor.");
        }
        ctx.SubProgress?.Invoke(0.35);

        // ---- Zamanlayıcı & Kernel ----
        log.Info("Zamanlayıcı & kernel ayarları:");
        reg.SetDword(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 0x26);
        reg.SetDword(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1);
        ctx.NeedsReboot("GlobalTimerResolutionRequests (timer resolution)");

        if (ctx.IsExtreme)
        {
            var (upcExists, upcValue) = BcdService.Query("useplatformclock");
            if (upcExists)
            {
                ctx.Backup.RecordBcd(new BcdBackupEntry { Name = "useplatformclock", Existed = true, OriginalValue = upcValue });
                var r = ProcessRunner.Run("bcdedit.exe", "/deletevalue useplatformclock", log, 30_000);
                if (r.Success)
                {
                    log.Ok($"bcdedit useplatformclock ({upcValue}) kaldırıldı — HPET zorlaması yok, düşük gecikmeli TSC kullanılacak.");
                    ctx.Verify.Add("bcd:useplatformclock", "BCD useplatformclock kaldırıldı", () => !BcdService.Query("useplatformclock").exists);
                    ctx.NeedsReboot("BCD useplatformclock");
                }
            }
            else
            {
                log.Detail("useplatformclock tanımlı değil (HPET zorlaması yok) — dokunulmadı.");
            }

            if (s.IsDesktop)
            {
                var (dtExists, dtValue) = BcdService.Query("disabledynamictick");
                ctx.Backup.RecordBcd(new BcdBackupEntry { Name = "disabledynamictick", Existed = dtExists, OriginalValue = dtValue });
                if (dtExists && BcdService.IsYes(dtValue))
                {
                    log.Detail("disabledynamictick zaten yes.");
                }
                else
                {
                    var r = ProcessRunner.Run("bcdedit.exe", "/set disabledynamictick yes", log, 30_000);
                    if (r.Success)
                    {
                        log.Ok("bcdedit disabledynamictick = yes (masaüstü: sabit tick, mikro-stutter azaltma)");
                        ctx.NeedsReboot("BCD disabledynamictick");
                    }
                }
                ctx.Verify.Add("bcd:disabledynamictick", "BCD disabledynamictick = yes", () => BcdService.IsYes(BcdService.Query("disabledynamictick").value));
            }
            else
            {
                log.Detail("Bataryalı/dizüstü sistem: disabledynamictick uygulanmadı (pil ömrünü korumak için).");
            }
        }
        else
        {
            log.Detail("Balanced: BCD (önyükleme) ayarlarına dokunulmadı.");
        }
        ctx.SubProgress?.Invoke(0.55);

        // ---- MMCSS ----
        log.Info("MMCSS oyun önceliği:");
        reg.SetDword(Hive.HKLM, MmcssKey, "SystemResponsiveness", 10);
        reg.SetDword(Hive.HKLM, GamesTaskKey, "GPU Priority", 8);
        reg.SetDword(Hive.HKLM, GamesTaskKey, "Priority", 6);
        reg.SetString(Hive.HKLM, GamesTaskKey, "Scheduling Category", "High");
        reg.SetString(Hive.HKLM, GamesTaskKey, "SFIO Priority", "High");
        reg.SetString(Hive.HKLM, GamesTaskKey, "Background Only", "False");
        reg.SetDword(Hive.HKLM, GamesTaskKey, "Clock Rate", 10000);
        ctx.SubProgress?.Invoke(0.7);

        // ---- PCIe MSI ----
        if (ctx.IsExtreme)
        {
            log.Info("PCIe MSI (Message Signaled Interrupts) modu:");
            var devices = new List<(string pnp, string label)>();
            if (!string.IsNullOrEmpty(s.PrimaryGpu.PnpDeviceId)) devices.Add((s.PrimaryGpu.PnpDeviceId, "GPU: " + s.PrimaryGpu.Name));
            foreach (var nic in s.ActiveAdapters.Where(a => !string.IsNullOrEmpty(a.PnpDeviceId)))
                devices.Add((nic.PnpDeviceId, "Ağ: " + nic.Description));

            foreach (var (pnp, label) in devices)
            {
                if (!pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
                {
                    log.Detail($"{label}: PCI aygıtı değil ({pnp.Split('\\')[0]}), atlandı.");
                    continue;
                }
                string devParams = $@"SYSTEM\CurrentControlSet\Enum\{pnp}\Device Parameters";
                if (!RegistryUtil.KeyExists(Hive.HKLM, devParams))
                {
                    log.Detail($"{label}: Device Parameters anahtarı yok, atlandı.");
                    continue;
                }
                string msiKey = devParams + @"\Interrupt Management\MessageSignaledInterruptProperties";
                long? cur = RegistryUtil.GetNumber(Hive.HKLM, msiKey, "MSISupported");
                if (cur == 1)
                {
                    log.Detail($"{label}: MSI zaten etkin.");
                    continue;
                }
                if (ctx.Reg.SetDword(Hive.HKLM, msiKey, "MSISupported", 1))
                {
                    log.Ok($"{label}: MSISupported = 1 (eski değer yedeklendi)");
                    ctx.NeedsReboot("PCIe MSI modu");
                }
            }
        }
        else
        {
            log.Detail("Balanced: PCIe MSI moduna dokunulmadı.");
        }
        ctx.SubProgress?.Invoke(1.0);
    }
}
