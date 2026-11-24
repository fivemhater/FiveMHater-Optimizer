using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FiveMHaterOptimizer.Models;
using Windows.Management.Deployment;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>
/// Opsiyonel (varsayılan KAPALI): hazır gelen gereksiz Store uygulamalarını yalnızca MEVCUT KULLANICI için kaldırır.
/// Resmi WinRT PackageManager API (FindPackagesForUser + RemovePackageAsync) kullanılır; PowerShell yok.
/// Hazırlanmış (provisioned) paketlere dokunulmaz. GERİ ALINAMAZ; uygulamalar Microsoft Store'dan yeniden yüklenebilir.
/// </summary>
public sealed class BloatwareModule : IOptimizationModule
{
    public string Stage => "Hazır uygulamaları kaldırma";
    public string Section => "Hazır gelen gereksiz uygulamalar (yalnızca mevcut kullanıcı)";

    public static readonly string[] Targets =
    {
        "Microsoft.3DBuilder", "Microsoft.BingWeather", "Microsoft.BingNews", "Microsoft.BingSports", "Microsoft.BingFinance",
        "Microsoft.GetHelp", "Microsoft.Getstarted", "Microsoft.Messaging", "Microsoft.MicrosoftOfficeHub",
        "Microsoft.MicrosoftSolitaireCollection", "Microsoft.MixedReality.Portal", "Microsoft.OneConnect", "Microsoft.People",
        "Microsoft.Print3D", "Microsoft.SkypeApp", "Microsoft.Wallet", "Microsoft.WindowsFeedbackHub", "Microsoft.WindowsMaps",
        "Microsoft.ZuneVideo", "Microsoft.549981C3F5F10", "Microsoft.PowerAutomateDesktop", "MicrosoftTeams",
        "Clipchamp.Clipchamp", "king.com.CandyCrushSaga", "king.com.CandyCrushSodaSaga", "Disney.37853FC22B2CE",
        "Facebook.Facebook", "BytedancePte.Ltd.TikTok", "SpotifyAB.SpotifyMusic"
    };

    /// <summary>Güvenlik ağı: hedef listede olmasa da asla kaldırılmaz (oyunlar ve temel uygulamalar).</summary>
    private static readonly string[] NeverRemove =
    {
        "Microsoft.Xbox", "Microsoft.GamingApp", "Microsoft.XboxIdentityProvider", "Microsoft.XboxGamingOverlay", "Microsoft.GamingServices",
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.WindowsCamera", "Microsoft.WindowsCalculator",
        "Microsoft.Windows.Photos", "Microsoft.MicrosoftStickyNotes", "Microsoft.ScreenSketch", "Microsoft.WindowsAlarms",
        "Microsoft.WindowsSoundRecorder", "microsoft.windowscommunicationsapps", "Microsoft.DesktopAppInstaller", "Microsoft.SecHealthUI"
    };

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        if (ctx.IsNetworkOnly) { skipReason = "\"Sadece Ağ & Ping\" profili uygulamalara dokunmaz."; return false; }
        skipReason = "\"Hazır gelen gereksiz uygulamaları kaldır\" seçeneği kapalı (varsayılan). Hiçbir uygulama kaldırılmadı.";
        return ctx.Options.RemoveBloatware;
    }

    public async Task RunAsync(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        log.Warn("Kullanıcı seçimi: hazır uygulamalar mevcut kullanıcı için kaldırılıyor. Bu işlem Tam Geri Al ile GERİ ALINAMAZ; uygulamalar Microsoft Store'dan yeniden yüklenebilir.");
        log.Detail("Korunanlar: Xbox / GamingApp / XboxIdentityProvider / GamingOverlay / Store / Kamera / Hesap Makinesi / Fotoğraflar / Yapışkan Notlar / Ekran Alıntısı / Alarmlar / Ses Kaydedici / Posta.");

        PackageManager pm;
        Windows.ApplicationModel.Package[] packages;
        try
        {
            pm = new PackageManager();
            packages = await Task.Run(() => pm.FindPackagesForUser(string.Empty).ToArray(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Warn($"Paket listesi okunamadı (PackageManager): {ex.Message}. Uygulama kaldırma atlandı.");
            return;
        }

        int removed = 0, notFound = 0, failed = 0, i = 0;
        foreach (string target in Targets)
        {
            ct.ThrowIfCancellationRequested();
            ctx.SubProgress?.Invoke(++i / (double)Targets.Length);
            if (NeverRemove.Any(n => target.StartsWith(n, StringComparison.OrdinalIgnoreCase))) continue;

            var matches = packages.Where(p =>
            {
                try { return p.Id.Name.Equals(target, StringComparison.OrdinalIgnoreCase) && !p.IsFramework; }
                catch { return false; }
            }).ToList();

            if (matches.Count == 0) { notFound++; log.Detail($"Yüklü değil: {target}"); continue; }

            foreach (var p in matches)
            {
                string full = p.Id.FullName;
                try
                {
                    var result = await pm.RemovePackageAsync(full).AsTask(ct).WaitAsync(TimeSpan.FromMinutes(3), ct).ConfigureAwait(false);
                    if (result.ExtendedErrorCode == null || result.ExtendedErrorCode.HResult == 0)
                    {
                        removed++;
                        ctx.Backup.RecordRemovedAppx(new RemovedAppxEntry { Name = target, FullName = full, RemovedUtc = DateTime.UtcNow });
                        log.Ok($"Kaldırıldı: {target} ({full}) — Microsoft Store'dan yeniden yüklenebilir.");
                    }
                    else
                    {
                        failed++;
                        log.Warn($"Kaldırılamadı: {target} — {result.ErrorText} (0x{result.ExtendedErrorCode.HResult:X8})");
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    log.Warn($"Kaldırılamadı: {target} — {ex.Message}");
                }
            }
        }

        ctx.Backup.AddNote($"Hazır uygulama kaldırma: {removed} kaldırıldı (geri alınamaz, Microsoft Store'dan yeniden yüklenebilir).");
        log.Ok($"Hazır uygulamalar: {removed} kaldırıldı, {notFound} zaten yüklü değil, {failed} kaldırılamadı. Hazırlanmış (provisioned) paketlere dokunulmadı.");
    }
}
