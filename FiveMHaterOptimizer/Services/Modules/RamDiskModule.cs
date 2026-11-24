using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>MODÜL 8: RAM dengesi (pagefile ASLA kapatılmaz), EmptyWorkingSet, NTFS ayarları ve güvenli disk temizliği.</summary>
public sealed class RamDiskModule : IOptimizationModule
{
    public string Stage => "RAM & Disk";
    public string Section => "RAM dengesi & Disk temizliği";

    public const string MemoryManagementKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    public const string FileSystemKey = @"SYSTEM\CurrentControlSet\Control\FileSystem";

    private static readonly HashSet<string> SkipTrim = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "csrss", "smss", "wininit", "winlogon", "lsass", "services", "dwm", "audiodg",
        "MsMpEng", "FiveM", "FiveM_GTAProcess", "GTA5", "GTA5_Enhanced", "Memory Compression"
    };

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        skipReason = "\"Sadece Ağ & Ping\" profili RAM ve disk ayarlarına dokunmaz.";
        return !ctx.IsNetworkOnly;
    }

    public Task RunAsync(OptimizationContext ctx, CancellationToken ct) => Task.Run(() => Run(ctx, ct), ct);

    private static void Run(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        var reg = ctx.Reg;
        var s = ctx.System;

        // ---- Bellek yönetimi ----
        if (s.InstalledRamGB >= 15.5)
        {
            log.Info($"RAM {s.InstalledRamGB:0} GB (≥16 GB): kernel sayfalanmaz, büyük sistem önbelleği kapalı:");
            reg.SetDword(Hive.HKLM, MemoryManagementKey, "DisablePagingExecutive", 1);
            reg.SetDword(Hive.HKLM, MemoryManagementKey, "LargeSystemCache", 0);
            ctx.NeedsReboot("DisablePagingExecutive");
        }
        else
        {
            log.Detail($"RAM {s.InstalledRamGB:0.#} GB (<16 GB): DisablePagingExecutive uygulanmadı.");
        }

        // ---- Pagefile: ASLA dokunma, sadece logla ----
        var pf = RegistryUtil.GetValue(Hive.HKLM, MemoryManagementKey, "PagingFiles") as string[];
        log.Info($"Sanal bellek (pagefile) KORUNUYOR: {(pf == null || pf.Length == 0 ? "sistem yönetimli" : string.Join("; ", pf))}");
        foreach (var row in WmiUtil.Query(@"root\cimv2", "SELECT Name, AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage", "Name", "AllocatedBaseSize", "CurrentUsage"))
            log.Detail($"{WmiUtil.Str(row, "Name")}: ayrılan {WmiUtil.Num(row, "AllocatedBaseSize")} MB, kullanılan {WmiUtil.Num(row, "CurrentUsage")} MB");
        log.Detail("Pagefile kapatılmaz/sıfırlanmaz: FiveM bellek taşması (out of memory) çökmesini önler.");
        ctx.SubProgress?.Invoke(0.15);

        // ---- EmptyWorkingSet ----
        log.Info("Çalışan süreçlerin şişmiş çalışma kümeleri temizleniyor (EmptyWorkingSet):");
        ulong before = HardwareDetector.GetFreeRamBytes();
        int trimmed = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (SkipTrim.Contains(p.ProcessName) || p.Id == 0 || p.Id == 4) continue;
                IntPtr h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION | NativeMethods.PROCESS_SET_QUOTA, false, p.Id);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (NativeMethods.EmptyWorkingSet(h)) trimmed++;
                }
                finally
                {
                    NativeMethods.CloseHandle(h);
                }
            }
            catch { }
            finally
            {
                p.Dispose();
            }
        }
        Thread.Sleep(1000);
        ulong after = HardwareDetector.GetFreeRamBytes();
        double diffMb = ((double)after - before) / 1024 / 1024;
        log.Ok($"{trimmed} süreç kırpıldı. Boş RAM: {before / 1024d / 1024 / 1024:0.00} GB → {after / 1024d / 1024 / 1024:0.00} GB ({(diffMb >= 0 ? "+" : "")}{diffMb:0} MB)");
        ctx.SubProgress?.Invoke(0.35);

        // ---- NTFS ----
        log.Info("NTFS hızlandırma (fsutil):");
        reg.Snapshot(Hive.HKLM, FileSystemKey, "NtfsDisableLastAccessUpdate");
        reg.Snapshot(Hive.HKLM, FileSystemKey, "NtfsDisable8dot3NameCreation");
        reg.Snapshot(Hive.HKLM, FileSystemKey, "DisableDeleteNotification");
        var dq = ProcessRunner.Run("fsutil.exe", "behavior query DisableDeleteNotify", log, 30_000);
        long? ddn = RegistryUtil.GetNumber(Hive.HKLM, FileSystemKey, "DisableDeleteNotification");
        log.Detail($"DisableDeleteNotify mevcut değer (kayıt defteri): {(ddn?.ToString() ?? "tanımsız")} — yedeklendi.");
        if (ProcessRunner.Run("fsutil.exe", "behavior set DisableDeleteNotify 0", log, 30_000).Success)
        {
            log.Ok("TRIM bildirimleri açık (DisableDeleteNotify = 0) — SSD performansı korunur.");
            ctx.Verify.Add("fsutil:disabledeletenotify", "NTFS DisableDeleteNotify = 0 (TRIM açık)",
                () => (RegistryUtil.GetNumber(Hive.HKLM, FileSystemKey, "DisableDeleteNotification") ?? 0) == 0);
        }
        else log.Warn("DisableDeleteNotify ayarlanamadı.");
        if (ProcessRunner.Run("fsutil.exe", "behavior set disablelastaccess 1", log, 30_000).Success)
        {
            log.Ok("Son erişim zamanı güncellemesi kapalı.");
            ctx.Verify.Add("fsutil:disablelastaccess", "NTFS disablelastaccess = 1",
                () => ((RegistryUtil.GetNumber(Hive.HKLM, FileSystemKey, "NtfsDisableLastAccessUpdate") ?? 0) & 1) == 1);
        }
        if (ProcessRunner.Run("fsutil.exe", "behavior set disable8dot3 1", log, 30_000).Success)
        {
            log.Ok("8.3 kısa ad oluşturma kapalı (mevcut kısa adlar silinmez).");
            ctx.Verify.Add("fsutil:disable8dot3", "NTFS disable8dot3 = 1",
                () => RegistryUtil.GetNumber(Hive.HKLM, FileSystemKey, "NtfsDisable8dot3NameCreation") == 1);
        }
        ctx.SubProgress?.Invoke(0.5);

        // ---- Güvenli temizlik ----
        log.Info("Güvenli disk temizliği (kilitli/kullanımdaki dosyalar atlanır, bağlantı noktaları takip edilmez):");
        string local = AppPaths.LocalAppData;
        string win = AppPaths.WindowsDir;
        var jobs = new List<(string label, string path, TimeSpan? age, Func<FileInfo, bool>? filter)>
        {
            ("Kullanıcı %TEMP% (24 saatten eski)", Path.GetTempPath(), TimeSpan.FromHours(24), null),
            ("C:\\Windows\\Temp (24 saatten eski)", Path.Combine(win, "Temp"), TimeSpan.FromHours(24), null),
            ("Prefetch (*.pf, son 7 gün hariç)", Path.Combine(win, "Prefetch"), TimeSpan.FromDays(7), f => f.Extension.Equals(".pf", StringComparison.OrdinalIgnoreCase)),
            ("DirectX Shader Cache (D3DSCache)", Path.Combine(local, "D3DSCache"), null, null),
            ("NVIDIA DXCache", Path.Combine(local, "NVIDIA", "DXCache"), null, null),
            ("NVIDIA GLCache", Path.Combine(local, "NVIDIA", "GLCache"), null, null),
            ("NVIDIA NV_Cache", Path.Combine(AppPaths.ProgramData, "NVIDIA Corporation", "NV_Cache"), null, null),
            ("AMD DxCache", Path.Combine(local, "AMD", "DxCache"), null, null),
            ("AMD DxcCache", Path.Combine(local, "AMD", "DxcCache"), null, null),
            ("AMD GLCache", Path.Combine(local, "AMD", "GLCache"), null, null),
            ("AMD VkCache", Path.Combine(local, "AMD", "VkCache"), null, null),
            ("Internet önbelleği (INetCache)", Path.Combine(local, "Microsoft", "Windows", "INetCache"), null, null),
            ("Çökme dökümleri (CrashDumps, 1 günden eski)", Path.Combine(local, "CrashDumps"), TimeSpan.FromDays(1), null),
            ("Windows Update indirme önbelleği (SoftwareDistribution\\Download, 1 günden eski)", Path.Combine(win, "SoftwareDistribution", "Download"), TimeSpan.FromDays(1), null)
        };

        double total = 0;
        int idx = 0;
        foreach (var (label, path, age, filter) in jobs)
        {
            ct.ThrowIfCancellationRequested();
            idx++;
            if (!Directory.Exists(path)) continue;
            // %TEMP%\.net = .NET tek dosya uygulamalarının (bu uygulama dahil) çıkarılmış yerel kütüphaneleri: ASLA silinmez
            var preserve = label.StartsWith("Kullanıcı") || label.StartsWith("C:\\Windows\\Temp")
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".net" }
                : null;
            var r = FileCleaner.CleanDirectory(path, age, filter, removeEmptyDirs: !label.StartsWith("Prefetch"), preserveTopLevelDirs: preserve);
            total += r.MB;
            log.Detail($"{label}: {r.MB:0.0} MB ({r.FilesDeleted} dosya{(r.FilesSkipped > 0 ? $", {r.FilesSkipped} kilitli dosya atlandı" : "")})");
            ctx.SubProgress?.Invoke(0.5 + 0.5 * idx / jobs.Count);
        }
        ctx.CleanedMB += total;
        log.Ok($"Disk temizliği: {total:0.0} MB kazanıldı. (Gölgelendirici önbellekleri ilk oyun açılışında yeniden oluşur.)");

        // ---- SSD TRIM (yalnızca Ultra) ----
        if (ctx.IsUltra)
        {
            string sysDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
            bool? ssd = HardwareDetector.SystemDiskIsSsdByMediaType();
            if (ssd == true)
            {
                log.Info($"SSD TRIM (retrim) çalıştırılıyor: defrag.exe {sysDrive} /L");
                var tr = ProcessRunner.Run("defrag.exe", $"{sysDrive} /L", log, 600_000);
                if (tr.Success) log.Ok("SSD TRIM tamamlandı.");
                else log.Warn($"SSD TRIM tamamlanamadı (çıkış kodu {tr.ExitCode}).");
            }
            else if (ssd == false) log.Detail("Sistem diski SSD değil (MediaType ≠ 4): TRIM atlandı.");
            else log.Detail("Sistem diski türü belirlenemedi (MSFT_PhysicalDisk): TRIM atlandı.");
        }
    }
}
