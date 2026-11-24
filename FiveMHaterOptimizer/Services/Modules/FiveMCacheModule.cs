using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>MODÜL 7b: FiveM önbelleğinin güvenli temizliği (game-storage, citizen, mods ve kullanıcı yapılandırmaları KORUNUR).</summary>
public sealed class FiveMCacheModule : IOptimizationModule
{
    public string Stage => "FiveM önbelleği";
    public string Section => "FiveM önbelleği";

    public static readonly string[] CacheFolders = { "cache", "server-cache", "server-cache-priv", "nui-storage" };

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        if (ctx.IsNetworkOnly) { skipReason = "\"Sadece Ağ & Ping\" profili FiveM dosyalarına dokunmaz."; return false; }
        if (!ctx.Options.ClearFiveMCache) { skipReason = "FiveM Cache Temizliği seçeneği kapalı."; return false; }
        skipReason = string.Empty;
        return true;
    }

    public Task RunAsync(OptimizationContext ctx, CancellationToken ct) => Task.Run(() => Run(ctx, ct), ct);

    private static void Run(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        var appDirs = new List<string> { Path.Combine(AppPaths.LocalAppData, "FiveM", "FiveM.app") };
        foreach (string exe in ctx.GameExecutables.Where(e => Path.GetFileName(e).Equals("FiveM.exe", StringComparison.OrdinalIgnoreCase)))
        {
            string? dir = Path.GetDirectoryName(exe);
            if (dir != null) appDirs.Add(Path.Combine(dir, "FiveM.app"));
        }
        var dataDirs = appDirs.Select(d => Path.Combine(d, "data")).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (dataDirs.Count == 0)
        {
            log.Detail("FiveM.app\\data klasörü bulunamadı; temizlenecek önbellek yok.");
            return;
        }

        CloseFiveM(log);
        ctx.SubProgress?.Invoke(0.3);

        double totalMb = 0;
        foreach (string data in dataDirs)
        {
            log.Info($"FiveM veri klasörü: {data}");
            log.Detail("Korunanlar: game-storage, cache\\game, citizen, mods, plugins, CitizenFX.ini ve kullanıcı yapılandırmaları.");
            string fullData = Path.GetFullPath(data).TrimEnd('\\') + "\\";
            foreach (string folder in CacheFolders)
            {
                ct.ThrowIfCancellationRequested();
                string target = Path.GetFullPath(Path.Combine(data, folder));
                if (!target.StartsWith(fullData, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(target)) continue;
                var preserve = folder == "cache"
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "game" }
                    : null;
                var r = FileCleaner.CleanDirectory(target, null, null, true, preserve);
                totalMb += r.MB;
                log.Ok($"{folder}: {r.MB:0.0} MB silindi ({r.FilesDeleted} dosya{(r.FilesSkipped > 0 ? $", {r.FilesSkipped} kilitli dosya atlandı" : "")})");
            }
        }
        ctx.CleanedMB += totalMb;
        log.Ok($"FiveM önbelleği: toplam {totalMb:0.0} MB temizlendi.");
        ctx.SubProgress?.Invoke(1.0);
    }

    private static void CloseFiveM(Logger log)
    {
        var procs = Process.GetProcesses()
            .Where(p =>
            {
                try { return p.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            })
            .ToList();
        if (procs.Count == 0)
        {
            log.Detail("Çalışan FiveM süreci yok.");
            return;
        }
        log.Info($"Önbellek temizliği için çalışan {procs.Count} FiveM süreci güvenli şekilde kapatılıyor...");
        foreach (var p in procs)
        {
            try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { }
        }
        Thread.Sleep(4000);
        foreach (var p in procs)
        {
            try
            {
                if (!p.HasExited)
                {
                    string name = p.ProcessName;
                    p.Kill(true);
                    p.WaitForExit(5000);
                    log.Detail($"Kapatıldı: {name}.exe");
                }
            }
            catch (Exception ex)
            {
                log.Detail($"Süreç kapatılamadı: {ex.Message}");
            }
            finally
            {
                p.Dispose();
            }
        }
    }
}
