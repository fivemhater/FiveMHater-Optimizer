using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>
/// Opsiyonel (varsayılan KAPALI): Windows bileşen deposu temizliği.
/// Resmi araç: Dism.exe /Online /Cleanup-Image /StartComponentCleanup (/ResetBase YOK → güncellemeler kaldırılabilir kalır).
/// Pencere açılmaz (CreateNoWindow), çıktı satır satır loglanır, ilerleme yüzdesi %10 adımlarla raporlanır.
/// </summary>
public sealed class DismCleanupModule : IOptimizationModule
{
    public string Stage => "Windows bileşen temizliği (DISM)";
    public string Section => "Windows bileşen temizliği (DISM /StartComponentCleanup)";

    private static readonly Regex Percent = new(@"(\d{1,3}(?:[.,]\d)?)\s*%");

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        if (ctx.IsNetworkOnly) { skipReason = "\"Sadece Ağ & Ping\" profili sisteme dokunmaz."; return false; }
        skipReason = "\"Windows bileşen temizliği (DISM)\" seçeneği kapalı (varsayılan).";
        return ctx.Options.RunDismCleanup;
    }

    public Task RunAsync(OptimizationContext ctx, CancellationToken ct) => Task.Run(() => Run(ctx, ct), ct);

    private static void Run(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        string dism = Path.Combine(AppPaths.WindowsDir, "System32", "Dism.exe");
        if (!File.Exists(dism)) { log.Warn("Dism.exe bulunamadı; bileşen temizliği atlandı."); return; }

        const string args = "/Online /Cleanup-Image /StartComponentCleanup";
        log.Info("Bileşen deposu temizleniyor (5-15 dk sürebilir; /ResetBase kullanılmaz, yüklü güncellemeler kaldırılabilir kalır).");
        log.Detail($"> Dism.exe {args}");

        var psi = new ProcessStartInfo(dism, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.Combine(AppPaths.WindowsDir, "System32")
        };

        int lastReported = -10;
        try
        {
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                string line = e.Data.Trim();
                var m = Percent.Match(line);
                if (m.Success && line.Contains('['))
                {
                    if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double pct))
                    {
                        int step = (int)pct / 10 * 10;
                        if (step > lastReported)
                        {
                            lastReported = step;
                            log.Detail($"DISM ilerleme: %{step}");
                            ctx.SubProgress?.Invoke(Math.Clamp(pct / 100.0, 0, 1));
                        }
                    }
                    return;
                }
                log.Detail("DISM: " + line);
            };
            p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log.Detail("DISM: " + e.Data.Trim()); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            var deadline = DateTime.UtcNow.AddMinutes(30);
            while (!p.WaitForExit(1000))
            {
                if (ct.IsCancellationRequested)
                {
                    log.Warn("İptal istendi: DISM güvenli şekilde kendi işini bitirene kadar çalışmaya devam eder (zorla sonlandırılmaz).");
                    ct.ThrowIfCancellationRequested();
                }
                if (DateTime.UtcNow > deadline)
                {
                    log.Warn("DISM 30 dakikada bitmedi; arka planda kendi işini tamamlaması için bırakıldı (zorla sonlandırılmadı).");
                    return;
                }
            }
            p.WaitForExit();
            if (p.ExitCode == 0) log.Ok("Windows bileşen temizliği tamamlandı.");
            else log.Warn($"DISM çıkış kodu {p.ExitCode} (0x{p.ExitCode:X8}). Ayrıntılar: %WINDIR%\\Logs\\DISM\\dism.log");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.Warn("DISM çalıştırılamadı: " + ex.Message);
        }
        ctx.SubProgress?.Invoke(1.0);
    }
}
