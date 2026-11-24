using System;
using System.Text.RegularExpressions;

namespace FiveMHaterOptimizer.Services;

/// <summary>bcdedit.exe ile {current} girdisindeki öğeleri okur (öğe adları dil bağımsızdır).</summary>
public static class BcdService
{
    public static (bool exists, string? value) Query(string element)
    {
        var r = ProcessRunner.Run("bcdedit.exe", "/enum {current}", null, 30_000, false, false);
        if (!r.Success) return (false, null);
        var m = Regex.Match(r.Output, $@"^\s*{Regex.Escape(element)}\s+(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return m.Success ? (true, m.Groups[1].Value.Trim()) : (false, null);
    }

    public static bool IsYes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string v = value.Trim().ToLowerInvariant();
        return v is "yes" or "evet" or "true" or "on" or "1" or "ja" or "oui" or "sí" or "si" or "да";
    }
}
