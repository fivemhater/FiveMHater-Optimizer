using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace FiveMHaterOptimizer.Services;

/// <summary>
/// Her yazımdan ÖNCE orijinal değeri yedekler, gerekirse yeni oluşturulan anahtarları kaydeder,
/// değeri yazar, loglar ve doğrulama listesine ekler.
/// </summary>
public sealed class RegistryTweaker
{
    private readonly BackupService _backup;
    private readonly Logger _log;
    private readonly VerificationList _verify;

    public int AppliedCount { get; private set; }
    public int FailedCount { get; private set; }

    public RegistryTweaker(BackupService backup, Logger log, VerificationList verify)
    {
        _backup = backup;
        _log = log;
        _verify = verify;
    }

    public bool SetDword(Hive hive, string path, string name, uint value, bool verify = true) =>
        Set(hive, path, name, unchecked((int)value), RegistryValueKind.DWord, verify, value > 9 ? $"0x{value:X} ({value})" : value.ToString());

    public bool SetQword(Hive hive, string path, string name, ulong value, bool verify = true) =>
        Set(hive, path, name, unchecked((long)value), RegistryValueKind.QWord, verify, value.ToString());

    public bool SetString(Hive hive, string path, string name, string value, bool verify = true) =>
        Set(hive, path, name, value, RegistryValueKind.String, verify, $"\"{value}\"");

    public bool SetBinary(Hive hive, string path, string name, byte[] value, bool verify = true) =>
        Set(hive, path, name, value, RegistryValueKind.Binary, verify, BitConverter.ToString(value).Replace("-", " "));

    /// <summary>Değeri değiştirmeden yalnızca yedeğe alır (ör. fsutil'in değiştireceği değerler).</summary>
    public void Snapshot(Hive hive, string path, string name)
    {
        try { _backup.RecordRegistry(hive, path, name); }
        catch (Exception ex) { _log.Warn($"Yedeklenemedi: {hive}\\{path}\\{name} ({ex.Message})"); }
    }

    public bool Set(Hive hive, string path, string name, object value, RegistryValueKind kind, bool verify, string display)
    {
        path = path.Trim('\\');
        try
        {
            // 1) Önce orijinal değeri yedekle (yalnızca ilk kez).
            _backup.RecordRegistry(hive, path, name);

            using var baseKey = RegistryUtil.OpenBase(hive);

            // 2) Eksik anahtarları tek tek oluştur ve "yeni oluşturuldu" olarak kaydet.
            EnsureKeyPath(baseKey, hive, path);

            using var key = baseKey.OpenSubKey(path, true) ?? throw new InvalidOperationException("Anahtar açılamadı");
            key.SetValue(name, value, kind);
            AppliedCount++;
            _log.Ok($"{hive}\\{path} → {name} = {display}");

            if (verify)
            {
                object expected = value;
                _verify.Add(RegistryUtil.MakeId(hive, path, name), $"{hive}\\{path}\\{name} = {display}",
                    () => RegistryUtil.ValueEquals(hive, path, name, expected, kind));
            }
            return true;
        }
        catch (Exception ex)
        {
            FailedCount++;
            _log.Warn($"Yazılamadı (atlandı): {hive}\\{path}\\{name} — {ex.Message}");
            return false;
        }
    }

    private void EnsureKeyPath(RegistryKey baseKey, Hive hive, string path)
    {
        string[] parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var current = new List<string>();
        foreach (string part in parts)
        {
            current.Add(part);
            string sub = string.Join("\\", current);
            using var existing = baseKey.OpenSubKey(sub, false);
            if (existing != null) continue;
            _backup.RecordCreatedKey(hive, sub);
            using var created = baseKey.CreateSubKey(sub, true);
            _log.Detail($"Yeni anahtar oluşturuldu: {hive}\\{sub}");
        }
    }
}
