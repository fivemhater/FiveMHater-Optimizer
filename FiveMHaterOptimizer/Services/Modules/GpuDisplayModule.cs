using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FiveMHaterOptimizer.Models;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>MODÜL 5: Oyun Modu, GameDVR, HAGS, MPO, GPU üreticisine özel ayarlar ve görsel efektler.</summary>
public sealed class GpuDisplayModule : IOptimizationModule
{
    public string Stage => "GPU & Görüntü";
    public string Section => "GPU & Görüntü Optimizasyonu";

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        skipReason = "\"Sadece Ağ & Ping\" profili GPU/görüntü ayarlarına dokunmaz.";
        return !ctx.IsNetworkOnly;
    }

    public Task RunAsync(OptimizationContext ctx, CancellationToken ct) => Task.Run(() => Run(ctx, ct), ct);

    private void Run(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        var reg = ctx.Reg;
        var s = ctx.System;

        log.Info("Windows Oyun Modu açık, GameDVR / Xbox arka plan kaydı kapalı:");
        reg.SetDword(Hive.HKCU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
        reg.SetDword(Hive.HKCU, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1);
        reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
        reg.SetDword(Hive.HKCU, @"System\GameConfigStore", "GameDVR_Enabled", 0);
        reg.SetDword(Hive.HKCU, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2);
        reg.SetDword(Hive.HKCU, @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1);
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
        ctx.SubProgress?.Invoke(0.25);

        if (s.OsBuild >= 19041)
        {
            log.Info("Donanım Hızlandırmalı GPU Zamanlama (HAGS):");
            reg.SetDword(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2);
            ctx.NeedsReboot("HAGS (HwSchMode)");
        }
        else
        {
            log.Detail("HAGS bu Windows sürümünde desteklenmiyor (Build < 19041).");
        }

        log.Info("MPO (Multi-Plane Overlay) devre dışı — FiveM borderless/alt-tab takılma ve siyah ekran düzeltmesi:");
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5);
        ctx.NeedsReboot("MPO (OverlayTestMode)");
        ctx.SubProgress?.Invoke(0.45);

        // ---- Üreticiye özel ----
        if (s.HasNvidia)
        {
            var nv = s.Gpus.First(g => g.Vendor == GpuVendor.Nvidia);
            log.Info($"NVIDIA algılandı: {nv.Name} (sürücü {nv.DriverVersion}, VRAM {nv.VramGB:0.#} GB)");
            log.Detail("NVIDIA telemetri servisi Servis modülünde kapatıldı (varsa). NVIDIA Denetim Masası 3D tercihlerine dokunulmadı.");
            if (RegistryUtil.KeyExists(Hive.HKLM, @"SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client"))
                reg.SetDword(Hive.HKLM, @"SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client", "OptInOrOutPreference", 0);
            if (!string.IsNullOrEmpty(nv.ClassKeyPath))
            {
                foreach (string v in new[] { "PowerMizerEnable", "PowerMizerLevel", "PowerMizerLevelAC", "PerfLevelSrc", "EnableUlps" })
                {
                    object? val = RegistryUtil.GetValue(Hive.HKLM, nv.ClassKeyPath, v);
                    log.Detail($"NVIDIA {v} = {(val == null ? "tanımlı değil (sürücü varsayılanı)" : val.ToString())} — değiştirilmedi");
                }
                object? cache = RegistryUtil.GetValue(Hive.HKLM, @"SOFTWARE\NVIDIA Corporation\Global\NVTweak", "NvCplShaderCacheSize");
                log.Detail($"NVIDIA gölgelendirici önbelleği ayarı = {(cache == null ? "sürücü varsayılanı" : cache.ToString())} — değiştirilmedi");
            }
        }

        if (s.HasAmdGpu)
        {
            log.Info("AMD algılandı: ULPS (Ultra Low Power State) kapatılıyor — oyun içi frekans düşüşü/FPS drop önlenir.");
            int count = 0;
            foreach (string sub in RegistryUtil.GetSubKeyNames(Hive.HKLM, HardwareDetector.DisplayClassKey))
            {
                if (!sub.All(char.IsDigit)) continue;
                string p = HardwareDetector.DisplayClassKey + "\\" + sub;
                string provider = RegistryUtil.GetString(Hive.HKLM, p, "ProviderName") ?? string.Empty;
                string desc = RegistryUtil.GetString(Hive.HKLM, p, "DriverDesc") ?? string.Empty;
                bool isAmd = provider.Contains("AMD", StringComparison.OrdinalIgnoreCase) || provider.Contains("ATI", StringComparison.OrdinalIgnoreCase) ||
                             desc.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || desc.Contains("AMD", StringComparison.OrdinalIgnoreCase);
                if (!isAmd) continue;
                if (reg.SetDword(Hive.HKLM, p, "EnableUlps", 0)) count++;
            }
            if (count == 0) log.Detail("AMD sürücü anahtarı bulunamadı.");
            else ctx.NeedsReboot("AMD ULPS");
        }
        if (!s.HasNvidia && !s.HasAmdGpu) log.Detail($"GPU: {s.PrimaryGpu.Name} — üreticiye özel ek ayar yok.");
        ctx.SubProgress?.Invoke(0.7);

        // ---- Görsel efektler ----
        if (ctx.IsExtreme)
        {
            log.Info("Görsel efektler: en iyi performans (yazı tipi yumuşatma korunuyor):");
            reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0);
            reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2);
            reg.SetBinary(Hive.HKCU, @"Control Panel\Desktop", "UserPreferencesMask", new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 });
            reg.SetString(Hive.HKCU, @"Control Panel\Desktop", "FontSmoothing", "2");
            reg.SetString(Hive.HKCU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0");
            reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0);
            reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewAlphaSelect", 0);
            reg.SetDword(Hive.HKCU, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0);
            NativeMethods.BroadcastSettingChange("WindowMetrics");
            NativeMethods.BroadcastSettingChange("ImmersiveColorSet");
            log.Detail("Animasyon ayarlarının tamamı oturum kapatıp açınca etkinleşir.");
        }
        else
        {
            log.Detail("Balanced: Görsel efektler ve şeffaflık korunuyor.");
        }
        ctx.SubProgress?.Invoke(1.0);
    }
}
