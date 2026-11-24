using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FiveMHaterOptimizer.Services;

/// <summary>
/// "netsh int tcp" global ayarlarını okur (önce İngilizce netsh çıktısı, sonra WMI MSFT_NetTCPSetting /
/// MSFT_NetOffloadGlobalSetting) ve uygular. Okunamayan değerler için Windows varsayılanı yedeklenir.
/// </summary>
public static class TcpGlobalService
{
    public static readonly Dictionary<string, string> WindowsDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["autotuninglevel"] = "normal",
        ["ecncapability"] = "default",
        ["rsc"] = "enabled",
        ["timestamps"] = "default",
        ["rss"] = "enabled",
        ["initialRto"] = "1000",
        ["heuristics"] = "default"
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["autotuninglevel"] = new(StringComparer.OrdinalIgnoreCase) { "disabled", "highlyrestricted", "restricted", "normal", "experimental" },
        ["ecncapability"] = new(StringComparer.OrdinalIgnoreCase) { "enabled", "disabled", "default" },
        ["rsc"] = new(StringComparer.OrdinalIgnoreCase) { "enabled", "disabled" },
        ["timestamps"] = new(StringComparer.OrdinalIgnoreCase) { "enabled", "disabled", "allowed", "default" },
        ["rss"] = new(StringComparer.OrdinalIgnoreCase) { "enabled", "disabled" },
        ["heuristics"] = new(StringComparer.OrdinalIgnoreCase) { "enabled", "disabled", "default" }
    };

    public static bool IsValid(string key, string value)
    {
        if (key.Equals("initialRto", StringComparison.OrdinalIgnoreCase)) return int.TryParse(value, out int v) && v >= 300;
        return AllowedValues.TryGetValue(key, out var set) && set.Contains(value);
    }

    public static Dictionary<string, string> ReadCurrent()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1) netsh (İngilizce sistemlerde doğrudan etiketlerden)
        var g = ProcessRunner.Run("netsh.exe", "int tcp show global", null, 30_000, false, false);
        if (g.Success)
        {
            TryLabel(g.Output, @"Receive-Side Scaling State\s*:\s*(\S+)", "rss", result);
            TryLabel(g.Output, @"Receive Window Auto-Tuning Level\s*:\s*(\S+)", "autotuninglevel", result);
            TryLabel(g.Output, @"ECN Capability\s*:\s*(\S+)", "ecncapability", result);
            TryLabel(g.Output, @"RFC 1323 Timestamps\s*:\s*(\S+)", "timestamps", result);
            TryLabel(g.Output, @"Initial RTO\s*:\s*(\d+)", "initialRto", result);
            TryLabel(g.Output, @"Receive Segment Coalescing State\s*:\s*(\S+)", "rsc", result);
        }
        var h = ProcessRunner.Run("netsh.exe", "int tcp show heuristics", null, 30_000, false, false);
        if (h.Success) TryLabel(h.Output, @"Window Scaling heuristics\s*:\s*(\S+)", "heuristics", result);

        // 2) WMI (dil bağımsız) — netsh etiketleri yerelleştirilmişse
        if (result.Count < 7)
        {
            var tcp = WmiUtil.Query(@"root\StandardCimv2", "SELECT * FROM MSFT_NetTCPSetting WHERE SettingName = 'Internet'",
                "AutoTuningLevelLocal", "EcnCapability", "Timestamps", "InitialRtoMs", "ScalingHeuristics");
            if (tcp.Count > 0)
            {
                var row = tcp[0];
                if (!result.ContainsKey("autotuninglevel") && row["AutoTuningLevelLocal"] != null)
                {
                    string v = WmiUtil.Num(row, "AutoTuningLevelLocal") switch
                    {
                        0 => "disabled", 1 => "highlyrestricted", 2 => "restricted", 3 => "normal", 4 => "experimental", _ => "normal"
                    };
                    result["autotuninglevel"] = v;
                }
                if (!result.ContainsKey("ecncapability") && row["EcnCapability"] != null)
                    result["ecncapability"] = WmiUtil.Num(row, "EcnCapability") == 1 ? "enabled" : "disabled";
                if (!result.ContainsKey("timestamps") && row["Timestamps"] != null)
                    result["timestamps"] = WmiUtil.Num(row, "Timestamps") switch { 0 => "disabled", 1 => "enabled", 2 => "allowed", _ => "default" };
                if (!result.ContainsKey("initialRto") && row["InitialRtoMs"] != null && WmiUtil.Num(row, "InitialRtoMs") >= 300)
                    result["initialRto"] = WmiUtil.Num(row, "InitialRtoMs").ToString();
                if (!result.ContainsKey("heuristics") && row["ScalingHeuristics"] != null)
                    result["heuristics"] = WmiUtil.Num(row, "ScalingHeuristics") == 1 ? "enabled" : "disabled";
            }

            var off = WmiUtil.Query(@"root\StandardCimv2", "SELECT * FROM MSFT_NetOffloadGlobalSetting", "ReceiveSideScaling", "ReceiveSegmentCoalescing");
            if (off.Count > 0)
            {
                if (!result.ContainsKey("rss") && off[0]["ReceiveSideScaling"] != null)
                    result["rss"] = WmiUtil.Num(off[0], "ReceiveSideScaling") == 1 ? "enabled" : "disabled";
                if (!result.ContainsKey("rsc") && off[0]["ReceiveSegmentCoalescing"] != null)
                    result["rsc"] = WmiUtil.Num(off[0], "ReceiveSegmentCoalescing") == 1 ? "enabled" : "disabled";
            }
        }
        return result;
    }

    private static void TryLabel(string text, string pattern, string key, Dictionary<string, string> into)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (!m.Success) return;
        string v = m.Groups[1].Value.Trim().ToLowerInvariant();
        if (IsValid(key, v)) into[key] = key == "initialRto" ? m.Groups[1].Value.Trim() : v;
    }

    public static ProcessRunner.Result Apply(string key, string value, Logger log)
    {
        string args = key.Equals("heuristics", StringComparison.OrdinalIgnoreCase)
            ? $"int tcp set heuristics {value}"
            : $"int tcp set global {key}={value}";
        return ProcessRunner.Run("netsh.exe", args, log, 30_000);
    }
}
