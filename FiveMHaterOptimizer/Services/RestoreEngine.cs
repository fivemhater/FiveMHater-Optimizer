using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using FiveMHaterOptimizer.Models;
using FiveMHaterOptimizer.Services.Modules;
using Microsoft.Win32;

namespace FiveMHaterOptimizer.Services;

public sealed class RestoreResult
{
    public int RegistryRestored { get; set; }
    public int RegistryDeleted { get; set; }
    public int KeysDeleted { get; set; }
    public int Failures { get; set; }
    public int VerifiedOk { get; set; }
    public int VerifiedTotal { get; set; }
    public bool BackupArchived { get; set; }
}

/// <summary>MODÜL 10: Tam geri alma motoru — backup.json'daki her öğeyi orijinal veri tipi ve değeriyle geri yükler.</summary>
public sealed class RestoreEngine
{
    public async Task<RestoreResult> RunAsync(Logger log, IProgress<EngineProgress> progress, CancellationToken ct)
    {
        return await Task.Run(() => Run(log, progress, ct), ct).ConfigureAwait(false);
    }

    private RestoreResult Run(Logger log, IProgress<EngineProgress> progress, CancellationToken ct)
    {
        var result = new RestoreResult();
        log.Section("Tam Geri Alma (Restore)");
        progress.Report(new EngineProgress("Yedek okunuyor", 2));

        var data = BackupService.TryLoad(out string err);
        if (data == null)
        {
            log.Error($"Yedek okunamadı: {err}");
            result.Failures++;
            return result;
        }
        log.Ok($"Yedek okundu: {AppPaths.BackupFile} (oluşturma: {data.CreatedUtc.ToLocalTime():dd.MM.yyyy HH:mm}, {data.Registry.Count} kayıt defteri değeri)");

        // 1) Kayıt defteri
        log.Section("Kayıt defteri geri yükleniyor");
        var entries = data.Registry.Values.ToList();
        int i = 0;
        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            RestoreRegistryValue(e, log, result);
            progress.Report(new EngineProgress("Kayıt defteri geri yükleniyor", 5 + 45.0 * (++i) / Math.Max(1, entries.Count)));
        }

        // Yeni oluşturulan anahtarları (boşsa) en derinden başlayarak sil
        foreach (string id in data.CreatedRegistryKeys.OrderByDescending(k => k.Count(c => c == '\\')))
        {
            int sep = id.IndexOf('\\');
            if (sep <= 0) continue;
            Hive hive = RegistryUtil.ParseHive(id[..sep]);
            string path = id[(sep + 1)..];
            try
            {
                using var b = RegistryUtil.OpenBase(hive);
                using (var k = b.OpenSubKey(path, false))
                {
                    if (k == null) continue;
                    if (k.ValueCount > 0 || k.SubKeyCount > 0)
                    {
                        log.Detail($"Anahtar korunuyor (başka değerler içeriyor): {id}");
                        continue;
                    }
                }
                b.DeleteSubKey(path, false);
                result.KeysDeleted++;
                log.Ok($"Optimizer tarafından oluşturulan anahtar silindi: {id}");
            }
            catch (Exception ex)
            {
                log.Detail($"Anahtar silinemedi: {id} ({ex.Message})");
            }
        }
        log.Ok($"Kayıt defteri: {result.RegistryRestored} değer geri yüklendi, {result.RegistryDeleted} yeni değer silindi, {result.KeysDeleted} yeni anahtar kaldırıldı.");

        // 2) Servisler: Start tipleri kayıt defteriyle geri geldi; önceden çalışanları başlat
        log.Section("Servisler");
        progress.Report(new EngineProgress("Servisler", 55));
        foreach (var s in data.Services.Values)
        {
            ct.ThrowIfCancellationRequested();
            log.Detail($"{s.Name}: başlangıç tipi → {ServiceTweaker.StartName(s.OriginalStart)}");
            if (!s.WasRunning) continue;
            try
            {
                using var sc = new ServiceController(s.Name);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                    log.Ok($"{s.Name} yeniden başlatıldı.");
                }
            }
            catch (Exception ex)
            {
                log.Detail($"{s.Name} şimdi başlatılamadı ({ex.Message}); yeniden başlatmadan sonra orijinal tipine göre çalışır.");
            }
        }

        // 3) Zamanlanmış görevler
        log.Section("Zamanlanmış görevler");
        progress.Report(new EngineProgress("Zamanlanmış görevler", 62));
        foreach (var t in data.ScheduledTasks.Values.Where(t => t.Existed && t.WasEnabled))
        {
            ct.ThrowIfCancellationRequested();
            var r = ProcessRunner.Run("schtasks.exe", $"/Change /TN \"{t.Path}\" /ENABLE", log, 30_000);
            if (r.Success) log.Ok($"Görev yeniden etkin: {t.Path}");
            else { log.Warn($"Görev etkinleştirilemedi: {t.Path}"); result.Failures++; }
        }

        // 4) Güç planı
        log.Section("Güç planı");
        progress.Report(new EngineProgress("Güç planı", 68));
        foreach (var ps in data.PowerSettings.Values)
        {
            if (ps.OriginalAc == null || !PowerService.SchemeExists(ps.Scheme))
            {
                log.Detail($"{ps.Label}: plan kaldırılacak/yok veya orijinal değer bilinmiyor, atlandı.");
                continue;
            }
            if (PowerService.SetAc(ps.Scheme, ps.SubGroup, ps.Setting, (int)ps.OriginalAc.Value, log))
                log.Ok($"{ps.Label} → orijinal AC değeri {ps.OriginalAc} geri yüklendi.");
            else { log.Warn($"{ps.Label} geri yüklenemedi."); result.Failures++; }
        }
        if (data.HibernateWasEnabled == true)
        {
            var hr = ProcessRunner.Run("powercfg.exe", "/hibernate on", log, 30_000);
            if (hr.Success) log.Ok("Hazırda bekletme yeniden açıldı (powercfg /hibernate on).");
            else { log.Warn("Hazırda bekletme yeniden açılamadı."); result.Failures++; }
        }
        else if (data.HibernateWasEnabled == false)
        {
            log.Detail("Hazırda bekletme optimizasyondan önce de kapalıydı; dokunulmadı.");
        }
        if (data.RemovedAppx.Count > 0)
        {
            log.Info($"Kaldırılan hazır uygulamalar geri alınamaz ({data.RemovedAppx.Count} adet). İsterseniz Microsoft Store'dan yeniden yükleyebilirsiniz:");
            foreach (var a in data.RemovedAppx) log.Detail($"• {a.Name}");
        }
        if (data.Power != null)
        {
            if (!string.IsNullOrEmpty(data.Power.OriginalActiveScheme))
            {
                var r = ProcessRunner.Run("powercfg.exe", $"/setactive {data.Power.OriginalActiveScheme}", log, 30_000);
                if (r.Success) log.Ok($"Orijinal güç planı yeniden aktif: {data.Power.OriginalActiveScheme}");
                else
                {
                    log.Warn("Orijinal plan etkinleştirilemedi; Dengeli plana dönülüyor.");
                    ProcessRunner.Run("powercfg.exe", $"/setactive {PowerService.Balanced}", log, 30_000);
                }
            }
            if (!string.IsNullOrEmpty(data.Power.CreatedScheme) && PowerService.SchemeExists(data.Power.CreatedScheme))
            {
                var r = ProcessRunner.Run("powercfg.exe", $"/delete {data.Power.CreatedScheme}", log, 30_000);
                if (r.Success) log.Ok("FiveMHater Ultimate planı kaldırıldı.");
            }
        }
        else
        {
            log.Detail("Güç planı değiştirilmemiş.");
        }

        // 5) DNS
        log.Section("DNS ve ağ");
        progress.Report(new EngineProgress("DNS ve ağ", 74));
        foreach (var d in data.Dns.Values)
        {
            ct.ThrowIfCancellationRequested();
            string[]? servers = d.WasDhcp || d.OriginalServers.Length == 0 ? null : d.OriginalServers;
            uint rv = DnsService.SetServers(d.SettingId, servers, out string dnsErr);
            if (rv is 0 or 1) log.Ok($"{d.Description}: DNS → {(servers == null ? "Otomatik (DHCP)" : string.Join(", ", servers))}");
            else { log.Warn($"{d.Description}: DNS geri yüklenemedi (kod {rv}) {dnsErr}"); result.Failures++; }
        }

        // 6) netsh TCP global
        foreach (var kv in data.TcpGlobals)
        {
            ct.ThrowIfCancellationRequested();
            string key = kv.Key.Equals("initialrto", StringComparison.OrdinalIgnoreCase) ? "initialRto" : kv.Key;
            if (!TcpGlobalService.IsValid(key, kv.Value))
            {
                log.Detail($"netsh {key}: geçersiz yedek değer ({kv.Value}), Windows varsayılanı kullanılıyor.");
            }
            string value = TcpGlobalService.IsValid(key, kv.Value) ? kv.Value : TcpGlobalService.WindowsDefaults[key];
            var r = TcpGlobalService.Apply(key, value, log);
            if (r.Success) log.Ok($"netsh {key} → {value}");
            else log.Warn($"netsh {key} geri yüklenemedi.");
        }
        ProcessRunner.Run("gpupdate.exe", "/target:computer /force", log, 180_000);
        ProcessRunner.Run("ipconfig.exe", "/flushdns", log, 30_000);
        log.Detail("QoS DSCP 46 politikaları ve NIC sürücü ayarları kayıt defteri adımında orijinaline döndürüldü.");

        // 7) BCD
        log.Section("Önyükleme (BCD)");
        progress.Report(new EngineProgress("Önyükleme (BCD)", 82));
        foreach (var bcd in data.Bcd.Values)
        {
            ct.ThrowIfCancellationRequested();
            ProcessRunner.Result r;
            if (bcd.Existed)
            {
                string v = BcdService.IsYes(bcd.OriginalValue) ? "yes" : "no";
                r = ProcessRunner.Run("bcdedit.exe", $"/set {bcd.Name} {v}", log, 30_000);
            }
            else
            {
                if (!BcdService.Query(bcd.Name).exists) { log.Detail($"{bcd.Name} zaten tanımsız."); continue; }
                r = ProcessRunner.Run("bcdedit.exe", $"/deletevalue {bcd.Name}", log, 30_000);
            }
            if (r.Success) log.Ok($"BCD {bcd.Name} orijinal haline döndü.");
            else { log.Warn($"BCD {bcd.Name} geri yüklenemedi."); result.Failures++; }
        }

        // 8) Dosyalar (settings.xml.fivemhater.bak)
        log.Section("Oyun ayar dosyaları");
        progress.Report(new EngineProgress("Oyun ayar dosyaları", 88));
        foreach (var f in data.Files.Values)
        {
            try
            {
                if (!File.Exists(f.BackupPath)) { log.Warn($"Yedek dosya yok: {f.BackupPath}"); result.Failures++; continue; }
                File.Copy(f.BackupPath, f.OriginalPath, true);
                File.Delete(f.BackupPath);
                log.Ok($"Geri yüklendi: {f.OriginalPath}");
            }
            catch (Exception ex)
            {
                log.Warn($"{f.OriginalPath} geri yüklenemedi: {ex.Message} (oyun açıksa kapatıp tekrar deneyin)");
                result.Failures++;
            }
        }

        InputLatencyModule.ApplyMouseLive(log);
        NativeMethods.BroadcastSettingChange("WindowMetrics");

        // 9) Doğrulama
        log.Section("Doğrulama raporu");
        progress.Report(new EngineProgress("Doğrulama", 94));
        int ok = 0, total = 0;
        foreach (var e in entries)
        {
            total++;
            if (VerifyRestored(e)) ok++;
            else log.Warn($"Orijinal değerle eşleşmiyor: {e.Hive}\\{e.Path}\\{e.Name}");
        }
        foreach (var t in data.ScheduledTasks.Values.Where(t => t.Existed))
        {
            total++;
            if (ScheduledTaskTweaker.QueryEnabled(t.Path) == t.WasEnabled) ok++;
            else log.Warn($"Görev durumu orijinalle eşleşmiyor: {t.Path}");
        }
        if (data.Power != null && !string.IsNullOrEmpty(data.Power.OriginalActiveScheme))
        {
            total++;
            if (string.Equals(PowerService.GetActiveScheme(), data.Power.OriginalActiveScheme, StringComparison.OrdinalIgnoreCase)) ok++;
            else log.Warn("Aktif güç planı orijinal plan değil.");
        }
        result.VerifiedOk = ok;
        result.VerifiedTotal = total;
        log.Ok($"Geri alma doğrulaması: {ok} / {total} öğe orijinal durumunda.");

        if (result.Failures == 0)
        {
            try
            {
                string archive = Path.Combine(AppPaths.DataDir, $"backup_restored_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.Move(AppPaths.BackupFile, archive);
                result.BackupArchived = true;
                log.Ok($"Yedek arşivlendi: {archive}. Bir sonraki optimizasyon yeni bir orijinal yedek alacak.");
            }
            catch (Exception ex)
            {
                log.Warn($"Yedek arşivlenemedi: {ex.Message}");
            }
        }
        else
        {
            log.Warn($"{result.Failures} öğe geri yüklenemedi. backup.json korunuyor; sorunu giderip Tam Geri Al'ı tekrar çalıştırabilirsiniz.");
        }

        log.Info("Tüm ayarların tamamen eski haline dönmesi için bilgisayarı yeniden başlatın.");
        log.Detail("Not: Kapatılan arka plan süreçleri ve silinen geçici/önbellek dosyaları geri getirilmez (zararsızdır, gerektiğinde yeniden oluşur).");
        progress.Report(new EngineProgress("Geri alma tamamlandı", 100));
        return result;
    }

    private static void RestoreRegistryValue(RegistryBackupEntry e, Logger log, RestoreResult result)
    {
        Hive hive = RegistryUtil.ParseHive(e.Hive);
        string display = $"{hive}\\{e.Path}\\{e.Name}";
        try
        {
            using var b = RegistryUtil.OpenBase(hive);
            if (!e.Existed)
            {
                using var k = b.OpenSubKey(e.Path, true);
                if (k?.GetValue(e.Name) != null)
                {
                    k.DeleteValue(e.Name, false);
                    result.RegistryDeleted++;
                    log.Ok($"Silindi (önceden yoktu): {display}");
                }
                return;
            }

            using var key = b.CreateSubKey(e.Path, true);
            var kind = Enum.TryParse<RegistryValueKind>(e.Kind, out var parsed) ? parsed : RegistryValueKind.Binary;
            object value = kind switch
            {
                RegistryValueKind.DWord => unchecked((int)(uint)(e.NumberData ?? 0)),
                RegistryValueKind.QWord => unchecked((long)(e.NumberData ?? 0)),
                RegistryValueKind.MultiString => e.MultiData ?? Array.Empty<string>(),
                RegistryValueKind.Binary => e.BinaryData != null ? Convert.FromBase64String(e.BinaryData) : Array.Empty<byte>(),
                RegistryValueKind.String or RegistryValueKind.ExpandString => e.StringData ?? string.Empty,
                _ => e.BinaryData != null ? Convert.FromBase64String(e.BinaryData) : (object)(e.StringData ?? string.Empty)
            };
            if (kind is not (RegistryValueKind.DWord or RegistryValueKind.QWord or RegistryValueKind.MultiString
                or RegistryValueKind.Binary or RegistryValueKind.String or RegistryValueKind.ExpandString))
            {
                kind = value is byte[] ? RegistryValueKind.Binary : RegistryValueKind.String;
            }
            key.SetValue(e.Name, value, kind);
            result.RegistryRestored++;
            log.Ok($"Geri yüklendi: {display} = {Describe(e)} ({kind})");
        }
        catch (Exception ex)
        {
            result.Failures++;
            log.Warn($"Geri yüklenemedi: {display} — {ex.Message}");
        }
    }

    private static string Describe(RegistryBackupEntry e) =>
        e.Kind switch
        {
            "DWord" or "QWord" => e.NumberData?.ToString() ?? "0",
            "MultiString" => string.Join(" | ", e.MultiData ?? Array.Empty<string>()),
            "Binary" => e.BinaryData != null ? BitConverter.ToString(Convert.FromBase64String(e.BinaryData)).Replace("-", " ") : "",
            _ => $"\"{e.StringData}\""
        };

    private static bool VerifyRestored(RegistryBackupEntry e)
    {
        Hive hive = RegistryUtil.ParseHive(e.Hive);
        object? cur = RegistryUtil.GetValue(hive, e.Path, e.Name);
        if (!e.Existed) return cur == null;
        if (cur == null) return false;
        return e.Kind switch
        {
            "DWord" => cur is int i && unchecked((uint)i) == (uint)(e.NumberData ?? 0),
            "QWord" => cur is long l && unchecked((ulong)l) == (e.NumberData ?? 0),
            "MultiString" => cur is string[] m && m.SequenceEqual(e.MultiData ?? Array.Empty<string>()),
            "Binary" => cur is byte[] bb && e.BinaryData != null && bb.SequenceEqual(Convert.FromBase64String(e.BinaryData)),
            _ => string.Equals(cur.ToString(), e.StringData, StringComparison.Ordinal)
        };
    }
}
