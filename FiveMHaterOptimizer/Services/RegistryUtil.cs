using System;
using System.Linq;
using Microsoft.Win32;

namespace FiveMHaterOptimizer.Services;

public enum Hive { HKLM, HKCU }

/// <summary>Microsoft.Win32.Registry tabanlı yardımcılar (64-bit görünüm).</summary>
public static class RegistryUtil
{
    public static RegistryKey OpenBase(Hive hive) =>
        RegistryKey.OpenBaseKey(hive == Hive.HKLM ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);

    public static Hive ParseHive(string s) => s.Equals("HKCU", StringComparison.OrdinalIgnoreCase) ? Hive.HKCU : Hive.HKLM;

    public static string MakeId(Hive hive, string path, string name) => $"{hive}\\{path.Trim('\\')}\\{name}".ToLowerInvariant();

    public static bool KeyExists(Hive hive, string path)
    {
        try
        {
            using var b = OpenBase(hive);
            using var k = b.OpenSubKey(path, false);
            return k != null;
        }
        catch
        {
            return false;
        }
    }

    public static object? GetValue(Hive hive, string path, string name)
    {
        try
        {
            using var b = OpenBase(hive);
            using var k = b.OpenSubKey(path, false);
            return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch
        {
            return null;
        }
    }

    public static string? GetString(Hive hive, string path, string name) => GetValue(hive, path, name)?.ToString();

    public static long? GetNumber(Hive hive, string path, string name)
    {
        object? v = GetValue(hive, path, name);
        return v switch
        {
            int i => unchecked((uint)i),
            long l => l,
            string s when long.TryParse(s, out long p) => p,
            byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
            byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
            _ => null
        };
    }

    public static string[] GetSubKeyNames(Hive hive, string path)
    {
        try
        {
            using var b = OpenBase(hive);
            using var k = b.OpenSubKey(path, false);
            return k?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static bool ValueEquals(Hive hive, string path, string name, object expected, RegistryValueKind kind)
    {
        object? current = GetValue(hive, path, name);
        if (current == null) return false;
        try
        {
            switch (kind)
            {
                case RegistryValueKind.DWord:
                    return current is int ci && ci == Convert.ToInt32(expected);
                case RegistryValueKind.QWord:
                    return current is long cl && cl == Convert.ToInt64(expected);
                case RegistryValueKind.Binary:
                    return current is byte[] cb && expected is byte[] eb && cb.SequenceEqual(eb);
                case RegistryValueKind.MultiString:
                    return current is string[] cm && expected is string[] em && cm.SequenceEqual(em);
                default:
                    return string.Equals(current.ToString(), expected.ToString(), StringComparison.Ordinal);
            }
        }
        catch
        {
            return false;
        }
    }
}
