using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>MODÜL 3a: TCP/IP yığını, netsh global ayarları, NIC sürücü seviyesi ayarları ve FiveM/GTA V QoS DSCP 46.</summary>
public sealed class NetworkModule : IOptimizationModule
{
    public string Stage => "Ağ ayarları";
    public string Section => "Ağ ayarları";

    public static readonly string[] QosExecutables = { "FiveM.exe", "FiveM_GTAProcess.exe", "GTA5.exe" };
    public const string QosPolicyRoot = @"SOFTWARE\Policies\Microsoft\Windows\QoS";

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        skipReason = string.Empty;
        return true;
    }

    public Task RunAsync(OptimizationContext ctx, CancellationToken ct) => Task.Run(() => Run(ctx, ct), ct);

    private void Run(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        var reg = ctx.Reg;

        // ---- Arayüz bazlı Nagle kapatma ----
        log.Info("TCP arayüz ayarları (Nagle algoritması kapalı):");
        var interfaces = ctx.System.UpInterfaces.Where(i => !i.IsVirtual || i.HasGateway).ToList();
        foreach (var nic in interfaces)
        {
            string key = DnsService.InterfaceKey(nic.Id);
            if (!RegistryUtil.KeyExists(Hive.HKLM, key)) { log.Detail($"{nic.Name}: TCP arayüz anahtarı yok, atlandı."); continue; }
            log.Detail($"Arayüz: {nic.Name} {nic.Id}");
            reg.SetDword(Hive.HKLM, key, "TcpAckFrequency", 1);
            reg.SetDword(Hive.HKLM, key, "TCPNoDelay", 1);
            reg.SetDword(Hive.HKLM, key, "TcpDelAckTicks", 0);
        }
        ctx.SubProgress?.Invoke(0.15);

        // ---- Global TCP/IP ----
        log.Info("TCP/IP yığını:");
        const string tcp = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
        reg.SetDword(Hive.HKLM, tcp, "MaxUserPort", 65534);
        reg.SetDword(Hive.HKLM, tcp, "TcpTimedWaitDelay", 30);
        reg.SetDword(Hive.HKLM, tcp, "EnablePMTUDiscovery", 1);
        reg.SetDword(Hive.HKLM, tcp, "EnablePMTUBHDetect", 0);
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", 0xFFFFFFFF);
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit", 0);
        reg.SetDword(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0);
        ctx.NeedsReboot("TCP/IP yığını kayıt defteri ayarları");
        ctx.SubProgress?.Invoke(0.3);

        // ---- netsh global ----
        log.Info("netsh TCP/UDP ince ayarları (orijinal değerler yedekleniyor):");
        var current = TcpGlobalService.ReadCurrent();
        var desired = new List<(string key, string value)>
        {
            ("autotuninglevel", "normal"),
            ("ecncapability", "disabled"),
            ("rsc", "disabled"),
            ("timestamps", "disabled"),
            ("rss", "enabled"),
            ("initialRto", "2000"),
            ("heuristics", "disabled")
        };
        foreach (var (key, value) in desired)
        {
            ct.ThrowIfCancellationRequested();
            string original = current.TryGetValue(key, out var cv) ? cv : TcpGlobalService.WindowsDefaults[key];
            ctx.Backup.RecordTcpGlobal(key, original);
            log.Detail($"{key}: mevcut = {(current.ContainsKey(key) ? original : "okunamadı (varsayılan yedeklendi: " + original + ")")}");
            var r = TcpGlobalService.Apply(key, value, log);
            if (r.Success)
            {
                log.Ok($"netsh {key} = {value}");
                string k = key, v = value;
                ctx.Verify.Add("netsh:" + key, $"netsh {key} = {value}", () =>
                {
                    var now = TcpGlobalService.ReadCurrent();
                    // Değer okunamıyorsa (yerelleştirilmiş çıktı + WMI yok) komutun başarılı dönüşü esas alınır
                    return !now.TryGetValue(k, out var nv) || nv.Equals(v, StringComparison.OrdinalIgnoreCase);
                });
            }
            else
            {
                log.Warn($"netsh {key} uygulanamadı (bu Windows sürümünde desteklenmiyor olabilir).");
            }
        }
        ctx.SubProgress?.Invoke(0.5);

        // ---- NIC sürücü seviyesi ----
        log.Info("Ağ adaptörü (NIC) sürücü seviyesi optimizasyonu:");
        int cores = ctx.System.CpuCores;
        foreach (var nic in ctx.System.ActiveAdapters)
        {
            ct.ThrowIfCancellationRequested();
            string? classKey = HardwareDetector.FindNetClassKey(nic.Id);
            if (classKey == null) { log.Warn($"{nic.Description}: sürücü sınıf anahtarı bulunamadı."); continue; }
            log.Detail($"Adaptör: {nic.Description} → HKLM\\{classKey}");

            reg.SetDword(Hive.HKLM, classKey, "PnPCapabilities", 24);
            SetKeyword(ctx, classKey, "*EEE", "0");
            SetKeyword(ctx, classKey, "EEELinkAdvertisement", "0");
            SetKeyword(ctx, classKey, "EnableGreenEthernet", "0");
            SetKeyword(ctx, classKey, "GreenEthernet", "0");
            SetKeyword(ctx, classKey, "PowerSavingMode", "0");
            SetKeyword(ctx, classKey, "*WakeOnMagicPacket", "0");
            SetKeyword(ctx, classKey, "*WakeOnPattern", "0");
            SetKeyword(ctx, classKey, "*LsoV2IPv4", "0");
            SetKeyword(ctx, classKey, "*LsoV2IPv6", "0");
            SetKeyword(ctx, classKey, "*FlowControl", "0");
            string im = cores >= 6 ? "0" : "1";
            if (SetKeyword(ctx, classKey, "*InterruptModeration", im))
                log.Detail($"Interrupt Moderation = {(im == "0" ? "Kapalı (≥6 çekirdek, minimum gecikme)" : "Açık/Düşük (CPU yükünü dengelemek için)")}");
            SetBuffer(ctx, classKey, "*ReceiveBuffers", 4096);
            SetBuffer(ctx, classKey, "*TransmitBuffers", 4096);
        }
        if (ctx.System.ActiveAdapters.Count > 0) ctx.NeedsReboot("Ağ adaptörü sürücü ayarları (adaptör yeniden başlatıldığında etkin)");
        ctx.SubProgress?.Invoke(0.75);

        // ---- QoS DSCP 46 ----
        log.Info("FiveM & GTA V için QoS DSCP 46 (Expedited Forwarding) politikaları:");
        reg.SetString(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\QoS", "Do not use NLA", "1");
        foreach (string exe in QosExecutables)
        {
            string key = $@"{QosPolicyRoot}\FiveMHater_{exe.Replace(".exe", string.Empty)}";
            reg.SetString(Hive.HKLM, key, "Version", "1.0");
            reg.SetString(Hive.HKLM, key, "Application Name", exe);
            reg.SetString(Hive.HKLM, key, "Protocol", "*");
            reg.SetString(Hive.HKLM, key, "Local Port", "*");
            reg.SetString(Hive.HKLM, key, "Local IP", "*");
            reg.SetString(Hive.HKLM, key, "Local IP Prefix Length", "*");
            reg.SetString(Hive.HKLM, key, "Remote Port", "*");
            reg.SetString(Hive.HKLM, key, "Remote IP", "*");
            reg.SetString(Hive.HKLM, key, "Remote IP Prefix Length", "*");
            reg.SetString(Hive.HKLM, key, "DSCP Value", "46");
            reg.SetString(Hive.HKLM, key, "Throttle Rate", "-1");
            log.Ok($"QoS politikası: {exe} → DSCP 46");
        }
        log.Info("Yerel ilke yenileniyor (gpupdate /target:computer)...");
        ProcessRunner.Run("gpupdate.exe", "/target:computer /force", log, 180_000);
        ctx.SubProgress?.Invoke(1.0);
    }

    /// <summary>Gelişmiş NIC özelliğini yalnızca adaptör destekliyorsa (Ndi\Params altında tanımlı ya da mevcut) uygular.</summary>
    private static bool SetKeyword(OptimizationContext ctx, string classKey, string keyword, string value)
    {
        bool supported = RegistryUtil.KeyExists(Hive.HKLM, $@"{classKey}\Ndi\Params\{keyword}") ||
                         RegistryUtil.GetValue(Hive.HKLM, classKey, keyword) != null;
        if (!supported) return false;
        if (RegistryUtil.GetString(Hive.HKLM, classKey, keyword) == value)
        {
            ctx.Log.Detail($"{keyword} zaten {value}.");
            return true;
        }
        return ctx.Reg.SetString(Hive.HKLM, classKey, keyword, value);
    }

    private static void SetBuffer(OptimizationContext ctx, string classKey, string keyword, int cap)
    {
        string paramKey = $@"{classKey}\Ndi\Params\{keyword}";
        if (!RegistryUtil.KeyExists(Hive.HKLM, paramKey)) return;
        if (!int.TryParse(RegistryUtil.GetString(Hive.HKLM, paramKey, "max"), out int max) || max <= 0) return;
        int.TryParse(RegistryUtil.GetString(Hive.HKLM, paramKey, "step"), out int step);
        int target = Math.Min(max, cap);
        if (step > 1) target -= target % step;
        int.TryParse(RegistryUtil.GetString(Hive.HKLM, classKey, keyword), out int currentValue);
        if (currentValue >= target)
        {
            ctx.Log.Detail($"{keyword} zaten {currentValue} (hedef {target}).");
            return;
        }
        ctx.Reg.SetString(Hive.HKLM, classKey, keyword, target.ToString());
    }
}
