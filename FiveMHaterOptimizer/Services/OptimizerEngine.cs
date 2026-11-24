using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FiveMHaterOptimizer.Models;
using FiveMHaterOptimizer.Services.Modules;

namespace FiveMHaterOptimizer.Services;

public readonly record struct EngineProgress(string Stage, double Percent);

/// <summary>Modülleri sırayla çalıştırır; bir modüldeki hata diğerlerini durdurmaz.</summary>
public sealed class OptimizerEngine
{
    public static IReadOnlyList<IOptimizationModule> CreateModules() => new IOptimizationModule[]
    {
        new SystemPrepModule(),
        new ServiceProcessModule(),
        new NetworkModule(),
        new DnsModule(),
        new PowerCpuModule(),
        new GpuDisplayModule(),
        new InputLatencyModule(),
        new GameSettingsModule(),
        new FiveMCacheModule(),
        new RamDiskModule(),
        new VerificationModule()
    };

    public async Task<OptimizationContext> RunAsync(OptimizationOptions options, SystemInfo system, Logger log,
        IProgress<EngineProgress> progress, CancellationToken ct)
    {
        var backup = BackupService.LoadOrCreate(log);
        var ctx = new OptimizationContext(options, system, log, backup);
        var modules = CreateModules();
        int n = modules.Count;

        for (int i = 0; i < n; i++)
        {
            var m = modules[i];
            int index = i;
            ctx.SubProgress = f => progress.Report(new EngineProgress(m.Stage, (index + Math.Clamp(f, 0, 1)) * 100.0 / n));
            progress.Report(new EngineProgress(m.Stage, index * 100.0 / n));
            log.Section(m.Section);

            if (!m.ShouldRun(ctx, out string reason))
            {
                log.Info("Atlandı: " + reason);
                progress.Report(new EngineProgress(m.Stage, (index + 1) * 100.0 / n));
                continue;
            }

            try
            {
                await m.RunAsync(ctx, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                log.Warn("İşlem iptal edildi. Uygulanan değişiklikler yedekte kayıtlı; Tam Geri Al ile geri alınabilir.");
                throw;
            }
            catch (Exception ex)
            {
                log.Error($"{m.Section} modülünde beklenmeyen hata: {ex.Message}. Diğer modüllere devam ediliyor.");
            }
            progress.Report(new EngineProgress(m.Stage, (index + 1) * 100.0 / n));
        }

        backup.AddNote($"Optimizasyon tamamlandı: profil {options.Profile}, doğrulama {ctx.VerifiedOk}/{ctx.VerifiedTotal}");
        progress.Report(new EngineProgress("Doğrulama ve sonuç", 100));
        return ctx;
    }
}
