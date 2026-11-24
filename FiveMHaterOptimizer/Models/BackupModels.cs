using System;
using System.Collections.Generic;

namespace FiveMHaterOptimizer.Models;

/// <summary>
/// %ProgramData%\FiveMHater\backup.json içeriği. Bir değer yedeğe bir kez yazılır ve
/// sonraki çalıştırmalarda ASLA ezilmez (ilk orijinal değer korunur).
/// </summary>
public sealed class BackupData
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>Anahtar: "hklm\yol\değer" (küçük harf).</summary>
    public Dictionary<string, RegistryBackupEntry> Registry { get; set; } = new();

    /// <summary>Optimizer tarafından sıfırdan oluşturulan kayıt defteri anahtarları ("HKLM\yol").</summary>
    public List<string> CreatedRegistryKeys { get; set; } = new();

    public Dictionary<string, ServiceBackupEntry> Services { get; set; } = new();
    public Dictionary<string, TaskBackupEntry> ScheduledTasks { get; set; } = new();
    public PowerBackupEntry? Power { get; set; }
    public Dictionary<string, DnsBackupEntry> Dns { get; set; } = new();

    /// <summary>netsh parametresi -> orijinal değer (ör. "rsc" -> "enabled").</summary>
    public Dictionary<string, string> TcpGlobals { get; set; } = new();

    public Dictionary<string, BcdBackupEntry> Bcd { get; set; } = new();
    public Dictionary<string, FileBackupEntry> Files { get; set; } = new();
    public List<string> Notes { get; set; } = new();

    /// <summary>v2.2: güç planı AC ayarlarının orijinal değerleri. Anahtar: "şema|alt grup|ayar".</summary>
    public Dictionary<string, PowerSettingBackupEntry> PowerSettings { get; set; } = new();

    /// <summary>v2.2: powercfg /hibernate off öncesi hazırda bekletme durumu (null = dokunulmadı).</summary>
    public bool? HibernateWasEnabled { get; set; }

    /// <summary>v2.2: kaldırılan hazır uygulamalar (geri alınamaz; Microsoft Store'dan yeniden yüklenebilir).</summary>
    public List<RemovedAppxEntry> RemovedAppx { get; set; } = new();
}

public sealed class PowerSettingBackupEntry
{
    public string Scheme { get; set; } = string.Empty;
    public string SubGroup { get; set; } = string.Empty;
    public string Setting { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public long? OriginalAc { get; set; }
}

public sealed class RemovedAppxEntry
{
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime RemovedUtc { get; set; }
    public string Note { get; set; } = "Geri alınamaz; Microsoft Store'dan yeniden yüklenebilir.";
}

public sealed class RegistryBackupEntry
{
    public string Hive { get; set; } = "HKLM";
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool KeyExisted { get; set; }
    public bool Existed { get; set; }
    /// <summary>Microsoft.Win32.RegistryValueKind adı: DWord, QWord, String, ExpandString, MultiString, Binary.</summary>
    public string Kind { get; set; } = "Unknown";
    public string? StringData { get; set; }
    public ulong? NumberData { get; set; }
    public string[]? MultiData { get; set; }
    public string? BinaryData { get; set; }
    public DateTime CapturedUtc { get; set; }
}

public sealed class ServiceBackupEntry
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int? OriginalStart { get; set; }
    public bool WasRunning { get; set; }
}

public sealed class TaskBackupEntry
{
    public string Path { get; set; } = string.Empty;
    public bool Existed { get; set; }
    public bool WasEnabled { get; set; }
}

public sealed class PowerBackupEntry
{
    public string OriginalActiveScheme { get; set; } = string.Empty;
    public string? CreatedScheme { get; set; }
}

public sealed class DnsBackupEntry
{
    public string SettingId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool WasDhcp { get; set; }
    public string[] OriginalServers { get; set; } = Array.Empty<string>();
}

public sealed class BcdBackupEntry
{
    public string Name { get; set; } = string.Empty;
    public bool Existed { get; set; }
    public string? OriginalValue { get; set; }
}

public sealed class FileBackupEntry
{
    public string OriginalPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
}
