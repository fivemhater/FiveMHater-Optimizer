using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using FiveMHaterOptimizer.Models;
using Microsoft.Win32;

namespace FiveMHaterOptimizer.Services;

/// <summary>
/// JSON yedekleme motoru. Kural: bir öğenin orijinal değeri yedekte varsa ASLA üzerine yazılmaz.
/// Her kayıttan sonra yedek atomik olarak diske yazılır (çökme/elektrik kesintisinde bile kayıp olmaz).
/// </summary>
public sealed class BackupService
{
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public BackupData Data { get; private set; }
    public bool WasCreatedNow { get; private set; }

    private BackupService(BackupData data, bool created)
    {
        Data = data;
        WasCreatedNow = created;
    }

    public static bool BackupExists => File.Exists(AppPaths.BackupFile);

    public static BackupData? TryLoad(out string error)
    {
        error = string.Empty;
        try
        {
            if (!File.Exists(AppPaths.BackupFile)) { error = "backup.json bulunamadı"; return null; }
            string json = File.ReadAllText(AppPaths.BackupFile);
            var data = JsonSerializer.Deserialize<BackupData>(json, JsonOptions);
            if (data == null) { error = "backup.json boş"; return null; }
            Normalize(data);
            return data;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public static BackupService LoadOrCreate(Logger log)
    {
        if (File.Exists(AppPaths.BackupFile))
        {
            var existing = TryLoad(out string err);
            if (existing != null)
            {
                log.Ok($"Mevcut ilk orijinal yedek korunuyor (oluşturma: {existing.CreatedUtc.ToLocalTime():dd.MM.yyyy HH:mm}). Yeni değerler yalnızca eklenir, eskiler ezilmez.");
                return new BackupService(existing, false);
            }

            // Bozuk yedek silinmez; kenara alınır ki elle kurtarılabilsin.
            string corrupt = Path.Combine(AppPaths.DataDir, $"backup.corrupt_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            try { File.Copy(AppPaths.BackupFile, corrupt, true); } catch { }
            log.Warn($"backup.json okunamadı ({err}). Dosya {Path.GetFileName(corrupt)} olarak saklandı, yeni yedek başlatılıyor.");
        }

        var data = new BackupData
        {
            CreatedUtc = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            OsVersion = Environment.OSVersion.VersionString,
            AppVersion = "2.1.0.0"
        };
        var svc = new BackupService(data, true);
        svc.Save();
        log.Ok($"Yeni tam yedek oluşturuldu: {AppPaths.BackupFile}");
        return svc;
    }

    private static void Normalize(BackupData d)
    {
        d.Registry = d.Registry.ToDictionary(k => k.Key.ToLowerInvariant(), v => v.Value);
        d.Services = d.Services.ToDictionary(k => k.Key.ToLowerInvariant(), v => v.Value);
        d.ScheduledTasks = d.ScheduledTasks.ToDictionary(k => k.Key.ToLowerInvariant(), v => v.Value);
        d.Dns = d.Dns.ToDictionary(k => k.Key.ToLowerInvariant(), v => v.Value);
        d.TcpGlobals = d.TcpGlobals.ToDictionary(k => k.Key.ToLowerInvariant(), v => v.Value);
        d.Bcd = d.Bcd.ToDictionary(k => k.Key.ToLowerInvariant(), v => v.Value);
        d.Files = d.Files.ToDictionary(k => k.Key.ToLowerInvariant(), v => v.Value);
    }

    public void Save()
    {
        lock (_lock)
        {
            Data.LastUpdatedUtc = DateTime.UtcNow;
            string json = JsonSerializer.Serialize(Data, JsonOptions);
            string tmp = AppPaths.BackupFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, AppPaths.BackupFile, true);
        }
    }

    public static void SaveData(BackupData data)
    {
        data.LastUpdatedUtc = DateTime.UtcNow;
        string json = JsonSerializer.Serialize(data, JsonOptions);
        string tmp = AppPaths.BackupFile + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, AppPaths.BackupFile, true);
    }

    // ---------------- Registry ----------------

    public static RegistryBackupEntry Capture(Hive hive, string path, string name)
    {
        var entry = new RegistryBackupEntry
        {
            Hive = hive.ToString(),
            Path = path,
            Name = name,
            CapturedUtc = DateTime.UtcNow
        };
        using var b = RegistryUtil.OpenBase(hive);
        using var k = b.OpenSubKey(path, false);
        entry.KeyExisted = k != null;
        if (k == null) return entry;

        object? value = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value == null) return entry;

        entry.Existed = true;
        RegistryValueKind kind = k.GetValueKind(name);
        entry.Kind = kind.ToString();
        switch (kind)
        {
            case RegistryValueKind.DWord:
                entry.NumberData = unchecked((uint)(int)value);
                break;
            case RegistryValueKind.QWord:
                entry.NumberData = unchecked((ulong)(long)value);
                break;
            case RegistryValueKind.MultiString:
                entry.MultiData = (string[])value;
                break;
            case RegistryValueKind.Binary:
                entry.BinaryData = Convert.ToBase64String((byte[])value);
                break;
            case RegistryValueKind.String:
            case RegistryValueKind.ExpandString:
                entry.StringData = value.ToString();
                break;
            default:
                entry.BinaryData = value is byte[] raw ? Convert.ToBase64String(raw) : null;
                entry.StringData = value is byte[] ? null : value.ToString();
                break;
        }
        return entry;
    }

    /// <summary>Değerin orijinalini yedeğe ekler (yalnızca daha önce yedeklenmemişse).</summary>
    public void RecordRegistry(Hive hive, string path, string name)
    {
        string id = RegistryUtil.MakeId(hive, path, name);
        lock (_lock)
        {
            if (Data.Registry.ContainsKey(id)) return;
        }
        var entry = Capture(hive, path, name);
        lock (_lock)
        {
            if (Data.Registry.ContainsKey(id)) return;
            Data.Registry[id] = entry;
        }
        Save();
    }

    public void RecordCreatedKey(Hive hive, string path)
    {
        string id = $"{hive}\\{path.Trim('\\')}";
        lock (_lock)
        {
            if (Data.CreatedRegistryKeys.Any(k => k.Equals(id, StringComparison.OrdinalIgnoreCase))) return;
            Data.CreatedRegistryKeys.Add(id);
        }
        Save();
    }

    // ---------------- Diğer öğeler ----------------

    public bool HasService(string name) { lock (_lock) return Data.Services.ContainsKey(name.ToLowerInvariant()); }

    public void RecordService(ServiceBackupEntry e)
    {
        lock (_lock)
        {
            string id = e.Name.ToLowerInvariant();
            if (Data.Services.ContainsKey(id)) return;
            Data.Services[id] = e;
        }
        Save();
    }

    public void RecordTask(TaskBackupEntry e)
    {
        lock (_lock)
        {
            string id = e.Path.ToLowerInvariant();
            if (Data.ScheduledTasks.ContainsKey(id)) return;
            Data.ScheduledTasks[id] = e;
        }
        Save();
    }

    public void RecordPowerOriginal(string activeScheme)
    {
        lock (_lock)
        {
            if (Data.Power != null && !string.IsNullOrEmpty(Data.Power.OriginalActiveScheme)) return;
            Data.Power ??= new PowerBackupEntry();
            Data.Power.OriginalActiveScheme = activeScheme;
        }
        Save();
    }

    public void SetCreatedScheme(string guid)
    {
        lock (_lock)
        {
            Data.Power ??= new PowerBackupEntry();
            Data.Power.CreatedScheme = guid;
        }
        Save();
    }

    public void RecordDns(DnsBackupEntry e)
    {
        lock (_lock)
        {
            string id = e.SettingId.ToLowerInvariant();
            if (Data.Dns.ContainsKey(id)) return;
            Data.Dns[id] = e;
        }
        Save();
    }

    public void RecordTcpGlobal(string parameter, string originalValue)
    {
        lock (_lock)
        {
            string id = parameter.ToLowerInvariant();
            if (Data.TcpGlobals.ContainsKey(id)) return;
            Data.TcpGlobals[id] = originalValue;
        }
        Save();
    }

    public void RecordBcd(BcdBackupEntry e)
    {
        lock (_lock)
        {
            string id = e.Name.ToLowerInvariant();
            if (Data.Bcd.ContainsKey(id)) return;
            Data.Bcd[id] = e;
        }
        Save();
    }

    public void RecordFile(FileBackupEntry e)
    {
        lock (_lock)
        {
            string id = e.OriginalPath.ToLowerInvariant();
            if (Data.Files.ContainsKey(id)) return;
            Data.Files[id] = e;
        }
        Save();
    }

    public void AddNote(string note)
    {
        lock (_lock) Data.Notes.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {note}");
        Save();
    }
}
