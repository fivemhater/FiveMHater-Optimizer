using System;
using System.Threading;
using System.Threading.Tasks;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>MODÜL 9: Son benchmark, tüm ayarların tek tek yeniden okunarak doğrulanması ve kalıcı log.</summary>
public sealed class VerificationModule : IOptimizationModule
{
    public string Stage => "Doğrulama ve sonuç";
    public string Section => "Doğrulama ve sonuç";

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        skipReason = string.Empty;
        return true;
    }

    public async Task RunAsync(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        log.Info("Sistemin durulması için 3 sn bekleniyor...");
        await Task.Delay(3000, ct).ConfigureAwait(false);

        ctx.After = await BenchmarkService.MeasureAsync(log, "Sonra", ct).ConfigureAwait(false);
        ctx.SubProgress?.Invoke(0.5);

        var b = ctx.Before;
        var a = ctx.After;
        if (b != null)
        {
            log.Info($"İşlem sayısı: {b.ProcessCount} → {a.ProcessCount} ({a.ProcessCount - b.ProcessCount:+0;-0;0})");
            log.Info($"Thread sayısı: {b.ThreadCount} → {a.ThreadCount}");
            log.Info($"Boş RAM: {b.FreeRamGB:0.00} GB → {a.FreeRamGB:0.00} GB");
            if (b.HasPing && a.HasPing)
                log.Info($"Ping: {b.PingMs:0.0} → {a.PingMs:0.0} ms, Jitter: {b.JitterMs:0.0} → {a.JitterMs:0.0} ms, Kayıp: %{b.PacketLossPercent:0.#} → %{a.PacketLossPercent:0.#}");
        }

        log.Info("Uygulanan tüm ayarlar tek tek yeniden okunuyor...");
        var (ok, total, failed) = await Task.Run(() => ctx.Verify.Run(), ct).ConfigureAwait(false);
        ctx.VerifiedOk = ok;
        ctx.VerifiedTotal = total;
        foreach (string f in failed) log.Warn("Doğrulanamadı: " + f);
        log.Ok($"Doğrulama: {ok} / {total} ayar doğrulandı ve aktif");
        log.Info($"Temizlenen alan: {ctx.CleanedMB:0.0} MB — Kapatılan arka plan süreci: {ctx.KilledProcesses}");
        if (ctx.ChosenDns != null) log.Info($"Seçilen DNS: {ctx.ChosenDns}");

        if (ctx.RebootRecommended)
        {
            log.Info("Yeniden başlatma ÖNERİLİR. Aşağıdaki ayarlar yeniden başlatmadan sonra tam etkinleşir:");
            foreach (string r in ctx.RebootReasons) log.Detail("• " + r);
            log.Detail("Not: svchost birleştirmesinin işlem sayısına asıl etkisi yeniden başlatmadan sonra görülür.");
        }
        log.Info($"Kalıcı log: {log.LogFilePath}");
        log.Info($"Yedek: {AppPaths.BackupFile} — \"Tam Geri Al (Restore)\" ile her şey eski haline döner.");
        ctx.SubProgress?.Invoke(1.0);
    }
}
