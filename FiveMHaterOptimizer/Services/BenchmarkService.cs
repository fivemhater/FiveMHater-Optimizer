using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using FiveMHaterOptimizer.Models;

namespace FiveMHaterOptimizer.Services;

/// <summary>İşlem/Thread sayısı, boş RAM ve ping/jitter/paket kaybı ölçümü.</summary>
public static class BenchmarkService
{
    public static readonly string[] PingTargets = { "1.1.1.1", "8.8.8.8" };

    public static async Task<PingStats> PingSeriesAsync(string target, int count, CancellationToken ct, int timeoutMs = 1000, int intervalMs = 150)
    {
        var stats = new PingStats { Target = target, Sent = count };
        var rtts = new List<double>();
        using var ping = new Ping();
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                PingReply reply = await ping.SendPingAsync(target, timeoutMs).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success) rtts.Add(reply.RoundtripTime);
            }
            catch
            {
                // kayıp paket olarak sayılır
            }
            await Task.Delay(intervalMs, ct).ConfigureAwait(false);
        }

        stats.Received = rtts.Count;
        if (rtts.Count > 0)
        {
            stats.AverageMs = rtts.Average();
            stats.MinMs = rtts.Min();
            stats.MaxMs = rtts.Max();
            stats.JitterMs = rtts.Count > 1
                ? Enumerable.Range(1, rtts.Count - 1).Select(i => Math.Abs(rtts[i] - rtts[i - 1])).Average()
                : 0;
        }
        return stats;
    }

    public static async Task<BenchmarkResult> MeasureAsync(Logger log, string label, CancellationToken ct)
    {
        var r = new BenchmarkResult();
        Process[] procs = Process.GetProcesses();
        r.ProcessCount = procs.Length;
        int threads = 0;
        foreach (var p in procs)
        {
            try { threads += p.Threads.Count; } catch { }
            p.Dispose();
        }
        r.ThreadCount = threads;
        r.FreeRamBytes = HardwareDetector.GetFreeRamBytes();

        log.Info($"{label} ölçüm: İşlem = {r.ProcessCount}, Thread = {r.ThreadCount}, Boş RAM = {r.FreeRamGB:0.00} GB");

        var all = new List<PingStats>();
        foreach (string t in PingTargets)
        {
            var s = await PingSeriesAsync(t, 6, ct).ConfigureAwait(false);
            all.Add(s);
            if (s.Received > 0)
                log.Detail($"Ping {t}: ort {s.AverageMs:0.0} ms, min {s.MinMs:0} / max {s.MaxMs:0} ms, jitter {s.JitterMs:0.0} ms, kayıp %{s.LossPercent:0}");
            else
                log.Detail($"Ping {t}: yanıt yok (kayıp %100)");
        }

        var ok = all.Where(s => s.Received > 0).ToList();
        int sent = all.Sum(s => s.Sent);
        int recv = all.Sum(s => s.Received);
        r.PacketLossPercent = sent == 0 ? 100 : (sent - recv) * 100.0 / sent;
        if (ok.Count > 0)
        {
            r.PingMs = ok.Average(s => s.AverageMs);
            r.JitterMs = ok.Average(s => s.JitterMs);
        }
        log.Info(r.HasPing
            ? $"{label} ağ: Ping {r.PingMs:0.0} ms, Jitter {r.JitterMs:0.0} ms, Paket kaybı %{r.PacketLossPercent:0.#}"
            : $"{label} ağ: İnternet erişimi ölçülemedi (paket kaybı %100).");
        return r;
    }
}
