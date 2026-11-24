using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FiveMHaterOptimizer.Models;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>MODÜL 3b: Canlı çoklu DNS benchmark'ı ve en hızlı DNS'in aktif adaptörlere uygulanması.</summary>
public sealed class DnsModule : IOptimizationModule
{
    public string Stage => "DNS seçimi";
    public string Section => "DNS seçimi";

    public static readonly (string Name, string Primary, string Secondary)[] Providers =
    {
        ("Cloudflare", "1.1.1.1", "1.0.0.1"),
        ("Google", "8.8.8.8", "8.8.4.4"),
        ("Quad9", "9.9.9.9", "149.112.112.112"),
        ("OpenDNS", "208.67.222.222", "208.67.220.220")
    };

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        skipReason = string.Empty;
        return true;
    }

    public async Task RunAsync(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        if (ctx.System.ActiveAdapters.Count == 0)
        {
            log.Warn("Aktif fiziksel ağ adaptörü yok; DNS değiştirilmedi.");
            return;
        }

        log.Info("DNS sunucuları test ediliyor (her IP için 5 paket ping + jitter):");
        var results = new List<(string name, string p, string s, double score, double avg, double jitter, double loss)>();
        int idx = 0;
        foreach (var (name, primary, secondary) in Providers)
        {
            ct.ThrowIfCancellationRequested();
            PingStats a = await BenchmarkService.PingSeriesAsync(primary, 5, ct).ConfigureAwait(false);
            PingStats b = await BenchmarkService.PingSeriesAsync(secondary, 5, ct).ConfigureAwait(false);
            var ok = new[] { a, b }.Where(x => x.Received > 0).ToList();
            double loss = (a.LossPercent + b.LossPercent) / 2;
            if (ok.Count == 0)
            {
                log.Detail($"{name,-10} {primary} / {secondary}: yanıt yok");
            }
            else
            {
                double avg = ok.Average(x => x.AverageMs);
                double jitter = ok.Average(x => x.JitterMs);
                // Skor: gecikme + 2×jitter + kayıp cezası
                double score = avg + 2 * jitter + loss * 10;
                results.Add((name, primary, secondary, score, avg, jitter, loss));
                log.Detail($"{name,-10} {primary} ({(a.Received > 0 ? a.AverageMs.ToString("0.0") : "-")} ms) / {secondary} ({(b.Received > 0 ? b.AverageMs.ToString("0.0") : "-")} ms) → ort {avg:0.0} ms, jitter {jitter:0.0} ms, kayıp %{loss:0}");
            }
            ctx.SubProgress?.Invoke(0.7 * (++idx) / Providers.Length);
        }

        if (results.Count == 0)
        {
            log.Warn("Hiçbir DNS sunucusuna ulaşılamadı; mevcut DNS korunuyor.");
            return;
        }

        var best = results.OrderBy(r => r.score).First();
        ctx.ChosenDns = $"{best.name} ({best.p}, {best.s})";
        log.Ok($"En hızlı DNS: {best.name} — {best.p} / {best.s} (ort {best.avg:0.0} ms, jitter {best.jitter:0.0} ms)");

        foreach (var nic in ctx.System.ActiveAdapters)
        {
            ct.ThrowIfCancellationRequested();
            string[] staticServers = DnsService.GetStaticServers(nic.Id);
            ctx.Backup.RecordDns(new DnsBackupEntry
            {
                SettingId = nic.Id,
                Description = nic.Description,
                WasDhcp = staticServers.Length == 0,
                OriginalServers = staticServers
            });
            log.Detail($"{nic.Name}: mevcut DNS = {(staticServers.Length == 0 ? "Otomatik (DHCP)" : string.Join(", ", staticServers))} (yedeklendi)");

            var servers = new[] { best.p, best.s };
            uint rv = DnsService.SetServers(nic.Id, servers, out string err);
            if (rv is 0 or 1)
            {
                log.Ok($"{nic.Name}: DNS = {best.p}, {best.s}");
                string id = nic.Id;
                ctx.Verify.Add("dns:" + id, $"{nic.Name} DNS = {best.p}, {best.s}", () =>
                {
                    var now = DnsService.GetCurrentServers(id);
                    return now.Length >= 2 && now[0] == servers[0] && now[1] == servers[1];
                });
            }
            else
            {
                log.Warn($"{nic.Name}: DNS uygulanamadı (WMI kod {rv}) {err}");
            }
        }

        ProcessRunner.Run("ipconfig.exe", "/flushdns", log, 30_000);
        ctx.SubProgress?.Invoke(1.0);
    }
}
