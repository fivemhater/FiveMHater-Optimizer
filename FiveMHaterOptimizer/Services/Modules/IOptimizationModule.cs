using System.Threading;
using System.Threading.Tasks;

namespace FiveMHaterOptimizer.Services.Modules;

public interface IOptimizationModule
{
    /// <summary>Arayüz alt başlığı (aktif modül adı).</summary>
    string Stage { get; }

    /// <summary>Log bölüm başlığı (== Bölüm ==).</summary>
    string Section { get; }

    /// <summary>Profil bu modülü çalıştırıyor mu? false ise neden loglanır.</summary>
    bool ShouldRun(OptimizationContext ctx, out string skipReason);

    Task RunAsync(OptimizationContext ctx, CancellationToken ct);
}
