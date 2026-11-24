using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using FiveMHaterOptimizer.Models;

namespace FiveMHaterOptimizer.Services.Modules;

/// <summary>MODÜL 7a: FiveM & GTA V tarama, exe bazlı GPU/uyumluluk/IFEO öncelik ayarları ve Competitive FPS preset.</summary>
public sealed class GameSettingsModule : IOptimizationModule
{
    public string Stage => "Oyun ayarları";
    public string Section => "FiveM & GTA V Oyun ayarları";

    private static readonly Regex GameExeRegex = new(
        @"^(FiveM\.exe|FiveM_GTAProcess\.exe|FiveM_b\d+_GTAProcess\.exe|FiveM_ChromeBrowser\.exe|GTA5\.exe|GTA5_Enhanced\.exe)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "$Recycle.Bin", "System Volume Information", "$WinREAgent", "Recovery", "PerfLogs", "Config.Msi",
        "Windows.old", "$SysReset", "$Windows.~BT", "$Windows.~WS", "node_modules", ".git", "WinSxS", "Microsoft", "Package Cache"
    };

    public const string GpuPrefKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    public const string IfeoKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    public bool ShouldRun(OptimizationContext ctx, out string skipReason)
    {
        skipReason = "\"Sadece Ağ & Ping\" profili oyun ayarlarına dokunmaz.";
        return !ctx.IsNetworkOnly;
    }

    public Task RunAsync(OptimizationContext ctx, CancellationToken ct) => Task.Run(() => Run(ctx, ct), ct);

    private void Run(OptimizationContext ctx, CancellationToken ct)
    {
        var log = ctx.Log;
        log.Info("FiveM ve GTA V dosyaları aranıyor (bilinen konumlar + tüm sabit diskler, en fazla 90 sn)...");
        var found = ScanForGames(log, ct);
        ctx.GameExecutables.Clear();
        ctx.GameExecutables.AddRange(found);
        if (found.Count == 0) log.Warn("FiveM/GTA V çalıştırılabilir dosyası bulunamadı. Exe bazlı ayarlar atlandı.");
        foreach (string f in found) log.Detail("Bulundu: " + f);
        ctx.SubProgress?.Invoke(0.5);

        // ---- Exe bazlı ayarlar ----
        var ifeoNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string exe in found)
        {
            ct.ThrowIfCancellationRequested();
            string fileName = Path.GetFileName(exe);
            log.Info($"{fileName}:");

            // Grafik tercihi: Yüksek Performans GPU (diğer DirectX anahtarları korunur)
            string? gpuPref = RegistryUtil.GetString(Hive.HKCU, GpuPrefKey, exe);
            ctx.Reg.SetString(Hive.HKCU, GpuPrefKey, exe, MergeGpuPreference(gpuPref));

            // Uyumluluk: tam ekran iyileştirmeleri kapalı + yüksek DPI (mevcut bayraklar korunur)
            if (!fileName.Equals("FiveM_ChromeBrowser.exe", StringComparison.OrdinalIgnoreCase))
            {
                string? layers = RegistryUtil.GetString(Hive.HKCU, LayersKey, exe);
                ctx.Reg.SetString(Hive.HKCU, LayersKey, exe, MergeLayers(layers));
            }

            if (!fileName.Equals("FiveM_ChromeBrowser.exe", StringComparison.OrdinalIgnoreCase)) ifeoNames.Add(fileName);
        }
        if (found.Any(f => Path.GetFileName(f).StartsWith("FiveM", StringComparison.OrdinalIgnoreCase)))
            ifeoNames.Add("FiveM_GTAProcess.exe");

        foreach (string name in ifeoNames)
        {
            string key = $@"{IfeoKey}\{name}\PerfOptions";
            ctx.Reg.SetDword(Hive.HKLM, key, "CpuPriorityClass", 3);
            ctx.Reg.SetDword(Hive.HKLM, key, "IoPriority", 3);
            log.Detail($"{name}: IFEO PerfOptions → CPU önceliği Yüksek, I/O önceliği Yüksek");
        }
        ctx.SubProgress?.Invoke(0.7);

        // ---- Competitive FPS preset ----
        if (!ctx.Options.ApplyFiveMFpsPreset)
        {
            log.Detail("FiveM Competitive FPS ayarları seçeneği kapalı; settings.xml dosyalarına dokunulmadı.");
            ctx.SubProgress?.Invoke(1.0);
            return;
        }

        log.Info("FiveM / GTA V Competitive FPS preset (settings.xml):");
        var files = new List<string>
        {
            Path.Combine(AppPaths.RoamingAppData, "CitizenFX", "gta5_settings.xml"),
            Path.Combine(AppPaths.Documents, "Rockstar Games", "GTA V", "settings.xml"),
            Path.Combine(AppPaths.UserProfile, "Documents", "Rockstar Games", "GTA V", "settings.xml"),
            Path.Combine(AppPaths.UserProfile, "OneDrive", "Documents", "Rockstar Games", "GTA V", "settings.xml"),
            Path.Combine(AppPaths.UserProfile, "OneDrive", "Belgeler", "Rockstar Games", "GTA V", "settings.xml")
        };
        double vramGb = ctx.System.PrimaryGpu.VramGB;
        int textureQuality = vramGb >= 6 ? 1 : 0;
        log.Detail($"VRAM {vramGb:0.#} GB → TextureQuality = {(textureQuality == 1 ? "High" : "Normal")} (oyunun çamur gibi görünmemesi için korunur)");

        foreach (string file in files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(file)) continue;
            ApplyPreset(ctx, file, textureQuality);
        }
        ctx.SubProgress?.Invoke(1.0);
    }

    public static string MergeGpuPreference(string? existing)
    {
        var parts = new List<(string k, string v)>();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            foreach (string seg in existing.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = seg.IndexOf('=');
                if (eq <= 0) continue;
                string k = seg[..eq].Trim();
                if (k.Equals("GpuPreference", StringComparison.OrdinalIgnoreCase)) continue;
                parts.Add((k, seg[(eq + 1)..].Trim()));
            }
        }
        var sb = new StringBuilder("GpuPreference=2;");
        foreach (var (k, v) in parts) sb.Append(k).Append('=').Append(v).Append(';');
        return sb.ToString();
    }

    public static string MergeLayers(string? existing)
    {
        var tokens = new List<string>();
        if (!string.IsNullOrWhiteSpace(existing))
            tokens.AddRange(existing.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t != "~"));
        foreach (string t in new[] { "DISABLEDXMAXIMIZEDWINDOWEDMODE", "HIGHDPIAWARE" })
            if (!tokens.Contains(t, StringComparer.OrdinalIgnoreCase)) tokens.Add(t);
        return "~ " + string.Join(" ", tokens);
    }

    private static List<string> ScanForGames(Logger log, CancellationToken ct)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sw = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(90);

        void ScanDir(string dir, int depth)
        {
            if (!Directory.Exists(dir)) return;
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline
            };
            var stack = new Stack<(string path, int d)>();
            stack.Push((dir, 0));
            while (stack.Count > 0)
            {
                if (sw.Elapsed > budget || ct.IsCancellationRequested) return;
                var (path, d) = stack.Pop();
                try
                {
                    foreach (string f in Directory.EnumerateFiles(path, "*.exe", opts))
                        if (GameExeRegex.IsMatch(Path.GetFileName(f))) results.Add(f);
                    if (d >= depth) continue;
                    foreach (string sub in Directory.EnumerateDirectories(path, "*", opts))
                    {
                        string name = Path.GetFileName(sub);
                        if (d == 0 && SkipDirs.Contains(name)) continue;
                        if (name.StartsWith("$")) continue;
                        stack.Push((sub, d + 1));
                    }
                }
                catch
                {
                    // erişilemeyen klasör: atla
                }
            }
        }

        // 1) Bilinen konumlar (derin)
        string fivem = Path.Combine(AppPaths.LocalAppData, "FiveM");
        ScanDir(fivem, 6);
        foreach (string pf in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (string.IsNullOrEmpty(pf)) continue;
            ScanDir(Path.Combine(pf, "Rockstar Games"), 3);
            ScanDir(Path.Combine(pf, "Epic Games"), 3);
            ScanDir(Path.Combine(pf, "Steam", "steamapps", "common"), 3);
        }
        foreach (string lib in SteamLibraries())
            ScanDir(Path.Combine(lib, "steamapps", "common"), 3);

        // 2) Tüm sabit diskler (sınırlı derinlik)
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (sw.Elapsed > budget) { log.Detail("Tarama süre sınırına ulaştı."); break; }
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            }
            catch { continue; }
            ScanDir(drive.RootDirectory.FullName, 5);
        }
        log.Detail($"Tarama {sw.Elapsed.TotalSeconds:0.0} sn sürdü.");
        return results.OrderBy(r => r).ToList();
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var list = new List<string>();
        try
        {
            string? steam = RegistryUtil.GetString(Hive.HKCU, @"Software\Valve\Steam", "SteamPath");
            if (string.IsNullOrEmpty(steam)) return list;
            steam = steam.Replace('/', '\\');
            list.Add(steam);
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    list.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            }
        }
        catch { }
        return list.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void ApplyPreset(OptimizationContext ctx, string file, int textureQuality)
    {
        var log = ctx.Log;
        try
        {
            string bak = file + ".fivemhater.bak";
            if (!File.Exists(bak))
            {
                File.Copy(file, bak, false);
                log.Ok($"Yedek alındı: {bak}");
            }
            else
            {
                log.Detail($"İlk orijinal yedek korunuyor: {bak}");
            }
            ctx.Backup.RecordFile(new FileBackupEntry { OriginalPath = file, BackupPath = bak });

            XDocument doc;
            using (var fs = File.OpenRead(file))
            {
                doc = XDocument.Load(fs, LoadOptions.PreserveWhitespace);
            }
            var graphics = doc.Root?.Element("graphics");
            if (graphics == null)
            {
                log.Warn($"{Path.GetFileName(file)}: <graphics> bölümü bulunamadı, dosyaya dokunulmadı.");
                return;
            }

            var changes = new List<(string el, string val)>
            {
                ("GrassQuality", "0"),
                ("ShadowQuality", "1"),
                ("ParticleQuality", "0"),
                ("ReflectionQuality", "0"),
                ("ReflectionMSAA", "0"),
                ("MSAA", "0"),
                ("PostFX", "0"),
                ("SSAO", "0"),
                ("Tessellation", "0"),
                ("Lighting_FogVolumes", "false"),
                ("Shadow_SoftShadows", "0"),
                ("UltraShadows_Enabled", "false"),
                ("Shadow_LongShadows", "false"),
                ("Shadow_ParticleShadows", "false"),
                ("LodScale", "0.000000"),
                ("MaxLodScale", "0.000000"),
                ("MotionBlurStrength", "0.000000"),
                ("DoF", "false"),
                ("TextureQuality", textureQuality.ToString())
            };

            int applied = 0;
            foreach (var (el, val) in changes)
            {
                var e = graphics.Element(el);
                var attr = e?.Attribute("value");
                if (attr == null) continue;
                if (attr.Value != val)
                {
                    log.Detail($"{el}: {attr.Value} → {val}");
                    attr.Value = val;
                }
                applied++;
            }

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = false,
                OmitXmlDeclaration = doc.Declaration == null
            };
            string tmp = file + ".fivemhater.tmp";
            using (var w = XmlWriter.Create(tmp, settings))
            {
                doc.Save(w);
            }
            File.Copy(tmp, file, true);
            File.Delete(tmp);
            log.Ok($"{Path.GetFileName(file)}: {applied} grafik ayarı Competitive preset'e alındı ({file})");

            string path = file;
            ctx.Verify.Add("xml:" + file, $"{Path.GetFileName(file)} Competitive preset", () =>
            {
                try
                {
                    var d = XDocument.Load(path);
                    return d.Root?.Element("graphics")?.Element("PostFX")?.Attribute("value")?.Value is null or "0";
                }
                catch { return false; }
            });
        }
        catch (Exception ex)
        {
            log.Warn($"{Path.GetFileName(file)} düzenlenemedi: {ex.Message} (dosya oyun açıkken kilitli olabilir).");
        }
    }
}
