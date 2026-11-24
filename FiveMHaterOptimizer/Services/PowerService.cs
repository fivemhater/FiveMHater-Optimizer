using System;
using System.Text.RegularExpressions;

namespace FiveMHaterOptimizer.Services;

/// <summary>powercfg.exe sarmalayıcı.</summary>
public static class PowerService
{
    public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    public const string CpMinCores = "0cc5b647-c1df-4637-891a-dec35c318583";
    public const string CpMaxCores = "ea062031-0e34-4ff1-9b6d-eb1059334028";
    public const string ProcThrottleMin = "893dee8e-2bef-41e0-89c6-b55d0929964c";
    public const string ProcThrottleMax = "bc5038f7-23e0-4960-96da-33abaf5935ec";
    public const string PerfBoostMode = "be337238-0d82-4146-a960-4f3749d470c7";
    public const string HeteroSchedPolicy = "93b8b6dc-0698-4d1c-9ee4-0644e900c85d";
    public const string HeteroShortSchedPolicy = "bae08b81-2d5e-4688-ad6a-13243356654b";

    public const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3";
    public const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
    public const string SubPciExpress = "501a4d13-42af-4429-9fd1-a8218c268e20";
    public const string PciAspm = "ee12f906-d277-404b-b6da-e5fa1a576df5";

    private static readonly Regex GuidRegex = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    public static string? GetActiveScheme(Logger? log = null)
    {
        var r = ProcessRunner.Run("powercfg.exe", "/getactivescheme", log, 30_000, log != null, log != null);
        var m = GuidRegex.Match(r.Output);
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    public static bool SchemeExists(string guid)
    {
        var r = ProcessRunner.Run("powercfg.exe", "/list", null, 30_000, false, false);
        return r.Output.Contains(guid, StringComparison.OrdinalIgnoreCase);
    }

    public static string? Duplicate(string sourceGuid, Logger log)
    {
        var r = ProcessRunner.Run("powercfg.exe", $"-duplicatescheme {sourceGuid}", log, 30_000);
        if (!r.Success) return null;
        var m = GuidRegex.Matches(r.Output);
        foreach (Match x in m)
        {
            if (!x.Value.Equals(sourceGuid, StringComparison.OrdinalIgnoreCase)) return x.Value.ToLowerInvariant();
        }
        return null;
    }

    public static bool SetAc(string scheme, string sub, string setting, int value, Logger log)
    {
        var r = ProcessRunner.Run("powercfg.exe", $"/setacvalueindex {scheme} {sub} {setting} {value}", log, 30_000);
        return r.Success;
    }

    /// <summary>Doğrulama: User\PowerSchemes altındaki ACSettingIndex değeri.</summary>
    public static long? ReadAc(string scheme, string sub, string setting) =>
        RegistryUtil.GetNumber(Hive.HKLM, $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{scheme}\{sub}\{setting}", "ACSettingIndex");
}
