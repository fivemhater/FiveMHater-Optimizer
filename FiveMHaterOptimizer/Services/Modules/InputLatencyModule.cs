using System;
using System.Threading;
using System.Threading.Tasks;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>MODÜL 6: Girdi gecikmesi — 1:1 ham fare, giriş kuyrukları, klavye tekrar hızı, erişilebilirlik kısayolları.</summary>
public sealed class InputLatencyModule : IOptimizationModule
{
    public string Stage => "Girdi gecikmesi";
    public string Section => "Girdi gecikmesi (Input Lag)";

    /// <summary>Windows 10/11 %100 ölçek için 1:1 lineer eğri (ivmesiz).</summary>
    public static readonly byte[] SmoothMouseXCurve =
    {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xC0, 0xCC, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x80, 0x99, 0x19, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x40, 0x66, 0x26, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x33, 0x33, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    public static readonly byte[] SmoothMouseYCurve =
    {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x38, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0xA8, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0xE0, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        skipReason = "\"Sadece Ağ & Ping\" profili girdi ayarlarına dokunmaz.";
        return !ctx.IsNetworkOnly;
    }

    public Task RunAsync(OptimizationContext ctx, CancellationToken ct) => Task.Run(() => Run(ctx), ct);

    private void Run(OptimizationContext ctx)
    {
        var log = ctx.Log;
        var reg = ctx.Reg;
        const string mouse = @"Control Panel\Mouse";

        log.Info("1:1 ham fare eğrisi (İşaretçi hassasiyetini artır = Kapalı):");
        reg.SetString(Hive.HKCU, mouse, "MouseSpeed", "0");
        reg.SetString(Hive.HKCU, mouse, "MouseThreshold1", "0");
        reg.SetString(Hive.HKCU, mouse, "MouseThreshold2", "0");
        reg.SetString(Hive.HKCU, mouse, "MouseSensitivity", "10");
        reg.SetBinary(Hive.HKCU, mouse, "SmoothMouseXCurve", SmoothMouseXCurve);
        reg.SetBinary(Hive.HKCU, mouse, "SmoothMouseYCurve", SmoothMouseYCurve);
        ApplyMouseLive(log);
        ctx.SubProgress?.Invoke(0.4);

        if (ctx.IsExtreme)
        {
            log.Info("Giriş kuyruğu boyutları (0x20 = 32; 4000/8000 Hz farelerde de takılmayan en düşük güvenli değer):");
            if (RegistryUtil.KeyExists(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\mouclass"))
                reg.SetDword(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize", 0x20);
            if (RegistryUtil.KeyExists(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\kbdclass"))
                reg.SetDword(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize", 0x20);
            ctx.NeedsReboot("Fare/klavye kuyruk boyutu");
        }
        else
        {
            log.Detail("Balanced: Fare/klavye sürücü kuyruk boyutları varsayılan bırakıldı.");
        }

        log.Info("Klavye tekrar gecikmesi ve hızı:");
        reg.SetString(Hive.HKCU, @"Control Panel\Keyboard", "KeyboardDelay", "0");
        reg.SetString(Hive.HKCU, @"Control Panel\Keyboard", "KeyboardSpeed", "31");
        ctx.SubProgress?.Invoke(0.7);

        log.Info("Yapışkan Tuşlar / Filtre Tuşları / Geçiş Tuşları kısayolları kapatılıyor (Flags bit temizliği):");
        ClearHotkeyFlag(ctx, @"Control Panel\Accessibility\StickyKeys", 510);
        ClearHotkeyFlag(ctx, @"Control Panel\Accessibility\Keyboard Response", 126);
        ClearHotkeyFlag(ctx, @"Control Panel\Accessibility\ToggleKeys", 62);
        log.Detail("Tüm girdi ayarları oturum kapatıp açınca kesinleşir (fare ayarları şimdiden canlı uygulandı).");
        ctx.SubProgress?.Invoke(1.0);
    }

    /// <summary>SKF/FKF/TKF_HOTKEYACTIVE (0x4) ve CONFIRMHOTKEY (0x8) bitlerini temizler; özelliğin kendisini (ON bit) değiştirmez.</summary>
    private static void ClearHotkeyFlag(OptimizationContext ctx, string path, int defaultFlags)
    {
        string? cur = RegistryUtil.GetString(Hive.HKCU, path, "Flags");
        int flags = int.TryParse(cur, out int f) ? f : defaultFlags;
        int updated = flags & ~0x4 & ~0x8;
        if (updated == flags && cur != null)
        {
            ctx.Log.Detail($"{path}: kısayol zaten kapalı (Flags = {flags}).");
            return;
        }
        ctx.Reg.SetString(Hive.HKCU, path, "Flags", updated.ToString());
    }

    /// <summary>Fare ivmesini ve hızını SystemParametersInfo ile anında uygular (kayıt defteri zaten yazıldı).</summary>
    public static void ApplyMouseLive(Logger log)
    {
        try
        {
            int speed = int.TryParse(RegistryUtil.GetString(Hive.HKCU, @"Control Panel\Mouse", "MouseSpeed"), out int ms) ? ms : 0;
            int t1 = int.TryParse(RegistryUtil.GetString(Hive.HKCU, @"Control Panel\Mouse", "MouseThreshold1"), out int a) ? a : 0;
            int t2 = int.TryParse(RegistryUtil.GetString(Hive.HKCU, @"Control Panel\Mouse", "MouseThreshold2"), out int b) ? b : 0;
            int sens = int.TryParse(RegistryUtil.GetString(Hive.HKCU, @"Control Panel\Mouse", "MouseSensitivity"), out int c) ? c : 10;
            bool ok1 = NativeMethods.SystemParametersInfoArray(NativeMethods.SPI_SETMOUSE, 0, new[] { t1, t2, speed }, NativeMethods.SPIF_SENDCHANGE);
            bool ok2 = NativeMethods.SystemParametersInfoPtr(NativeMethods.SPI_SETMOUSESPEED, 0, new IntPtr(Math.Clamp(sens, 1, 20)), NativeMethods.SPIF_SENDCHANGE);
            if (ok1 && ok2) log.Detail("Fare ayarları canlı olarak uygulandı (SystemParametersInfo).");
        }
        catch (Exception ex)
        {
            log.Detail($"Fare ayarları canlı uygulanamadı ({ex.Message}); oturum açılışında etkin olur.");
        }
    }
}
