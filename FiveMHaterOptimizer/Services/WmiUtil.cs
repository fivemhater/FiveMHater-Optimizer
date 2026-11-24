using System;
using System.Collections.Generic;
using System.Management;

namespace FiveMHaterOptimizer.Services;

/// <summary>System.Management (WMI/CIM) sorgu yardımcıları.</summary>
public static class WmiUtil
{
    public static List<Dictionary<string, object?>> Query(string scope, string wql, params string[] properties)
    {
        var result = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            searcher.Options.Timeout = TimeSpan.FromSeconds(20);
            using var collection = searcher.Get();
            foreach (ManagementBaseObject mo in collection)
            {
                using (mo)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (string p in properties)
                    {
                        try { row[p] = mo[p]; }
                        catch { row[p] = null; }
                    }
                    result.Add(row);
                }
            }
        }
        catch
        {
            // WMI erişilemezse boş liste döner; çağıran taraf varsayılanla devam eder.
        }
        return result;
    }

    public static string Str(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v != null ? v.ToString()!.Trim() : string.Empty;

    public static long Num(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v == null) return 0;
        try { return Convert.ToInt64(v); } catch { return 0; }
    }
}
