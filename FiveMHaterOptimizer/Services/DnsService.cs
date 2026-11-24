using System;
using System.Linq;
using System.Management;

namespace FiveMHaterOptimizer.Services;

/// <summary>DNS okuma/yazma: Win32_NetworkAdapterConfiguration.SetDNSServerSearchOrder (WMI). PowerShell kullanılmaz.</summary>
public static class DnsService
{
    public static string InterfaceKey(string guid) => $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{guid}";

    /// <summary>Elle girilmiş (statik) DNS sunucuları. Boşsa DNS DHCP'den gelir.</summary>
    public static string[] GetStaticServers(string guid)
    {
        string? ns = RegistryUtil.GetString(Hive.HKLM, InterfaceKey(guid), "NameServer");
        if (string.IsNullOrWhiteSpace(ns)) return Array.Empty<string>();
        return ns.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string[] GetCurrentServers(string guid)
    {
        var rows = WmiUtil.Query(@"root\cimv2", $"SELECT SettingID, DNSServerSearchOrder FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE",
            "SettingID", "DNSServerSearchOrder");
        var row = rows.FirstOrDefault(r => WmiUtil.Str(r, "SettingID").Equals(guid, StringComparison.OrdinalIgnoreCase));
        if (row != null && row["DNSServerSearchOrder"] is string[] arr) return arr;
        return Array.Empty<string>();
    }

    /// <summary>servers == null ise DNS tekrar DHCP'ye (otomatik) döndürülür.</summary>
    public static uint SetServers(string guid, string[]? servers, out string error)
    {
        error = string.Empty;
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
            using var col = searcher.Get();
            foreach (ManagementObject mo in col)
            {
                using (mo)
                {
                    string id = mo["SettingID"]?.ToString() ?? string.Empty;
                    if (!id.Equals(guid, StringComparison.OrdinalIgnoreCase)) continue;
                    using ManagementBaseObject inParams = mo.GetMethodParameters("SetDNSServerSearchOrder");
                    inParams["DNSServerSearchOrder"] = servers;
                    using ManagementBaseObject outParams = mo.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
                    return Convert.ToUInt32(outParams["ReturnValue"]);
                }
            }
            error = "Adaptör yapılandırması bulunamadı";
            return 999;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return 998;
        }
    }
}
