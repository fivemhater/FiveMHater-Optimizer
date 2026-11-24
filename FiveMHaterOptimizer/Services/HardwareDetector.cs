using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using FiveMHaterOptimizer.Models;
using Microsoft.Win32;

namespace FiveMHaterOptimizer.Services;

/// <summary>CPU / RAM / GPU / Disk / Ağ / Sistem türü algılama (WMI + Win32 API).</summary>
public static class HardwareDetector
{
    public const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    public const string NetClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    private static readonly string[] VirtualMarkers =
    {
        "virtual", "hyper-v", "vmware", "virtualbox", "vpn", "tap-windows", "tap adapter", "wintun", "wireguard",
        "zerotier", "hamachi", "loopback", "bluetooth", "npcap", "teredo", "isatap", "radmin", "openvpn", "tailscale", "wan miniport"
    };

    public static SystemInfo Detect()
    {
        var info = new SystemInfo();
        Safe(() => DetectOs(info));
        Safe(() => DetectCpu(info));
        Safe(() => DetectHybrid(info));
        Safe(() => DetectRam(info));
        Safe(() => DetectGpu(info));
        Safe(() => DetectPower(info));
        Safe(() => DetectDisk(info));
        Safe(() => DetectNetwork(info));
        info.ProcessCount = CountProcesses();
        return info;
    }

    private static void Safe(Action a)
    {
        try { a(); } catch { /* tek bir algılama hatası diğerlerini durdurmaz */ }
    }

    public static int CountProcesses()
    {
        Process[] procs = Process.GetProcesses();
        int count = procs.Length;
        foreach (var p in procs) p.Dispose();
        return count;
    }

    private static void DetectOs(SystemInfo info)
    {
        var rows = WmiUtil.Query(@"root\cimv2", "SELECT Caption, BuildNumber FROM Win32_OperatingSystem", "Caption", "BuildNumber");
        if (rows.Count > 0)
        {
            info.OsCaption = WmiUtil.Str(rows[0], "Caption");
            int.TryParse(WmiUtil.Str(rows[0], "BuildNumber"), out int build);
            info.OsBuild = build;
        }
        if (info.OsBuild == 0) info.OsBuild = Environment.OSVersion.Version.Build;
    }

    private static void DetectCpu(SystemInfo info)
    {
        var rows = WmiUtil.Query(@"root\cimv2", "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor",
            "Name", "Manufacturer", "NumberOfCores", "NumberOfLogicalProcessors");
        if (rows.Count > 0)
        {
            info.CpuName = WmiUtil.Str(rows[0], "Name");
            info.CpuManufacturer = WmiUtil.Str(rows[0], "Manufacturer");
            info.CpuCores = (int)rows.Sum(r => WmiUtil.Num(r, "NumberOfCores"));
            info.CpuThreads = (int)rows.Sum(r => WmiUtil.Num(r, "NumberOfLogicalProcessors"));
        }
        if (info.CpuThreads == 0) info.CpuThreads = Environment.ProcessorCount;
        if (info.CpuCores == 0) info.CpuCores = Environment.ProcessorCount;
        info.IsAmdCpu = info.CpuManufacturer.Contains("AMD", StringComparison.OrdinalIgnoreCase) || info.CpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase);
        info.IsIntelCpu = info.CpuManufacturer.Contains("Intel", StringComparison.OrdinalIgnoreCase) || info.CpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase);
        info.IsX3D = info.CpuName.Contains("X3D", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>GetLogicalProcessorInformationEx ile çekirdek verimlilik sınıflarını okur (Intel P-Core/E-Core hibrit tespiti).</summary>
    private static void DetectHybrid(SystemInfo info)
    {
        uint len = 0;
        NativeMethods.GetLogicalProcessorInformationEx(NativeMethods.RelationProcessorCore, IntPtr.Zero, ref len);
        if (len == 0) return;
        IntPtr buffer = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!NativeMethods.GetLogicalProcessorInformationEx(NativeMethods.RelationProcessorCore, buffer, ref len)) return;
            var classes = new List<byte>();
            int offset = 0;
            while (offset < len)
            {
                int relationship = Marshal.ReadInt32(buffer, offset);
                int size = Marshal.ReadInt32(buffer, offset + 4);
                if (size <= 0) break;
                if (relationship == NativeMethods.RelationProcessorCore)
                {
                    // PROCESSOR_RELATIONSHIP: BYTE Flags (offset 8), BYTE EfficiencyClass (offset 9)
                    classes.Add(Marshal.ReadByte(buffer, offset + 9));
                }
                offset += size;
            }
            if (classes.Count == 0) return;
            byte max = classes.Max();
            info.IsHybrid = classes.Distinct().Count() > 1;
            info.PerformanceCores = classes.Count(c => c == max);
            info.EfficiencyCores = classes.Count - info.PerformanceCores;
            if (info.CpuCores == 0) info.CpuCores = classes.Count;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void DetectRam(SystemInfo info)
    {
        var mem = NativeMethods.GetMemoryStatus();
        info.TotalRamBytes = mem.ullTotalPhys;
        if (NativeMethods.GetPhysicallyInstalledSystemMemory(out ulong kb)) info.InstalledRamKB = kb;
        if (info.InstalledRamKB == 0) info.InstalledRamKB = info.TotalRamBytes / 1024;

        var rows = WmiUtil.Query(@"root\cimv2", "SELECT * FROM Win32_PhysicalMemory", "Speed", "ConfiguredClockSpeed");
        int speed = 0;
        foreach (var r in rows)
        {
            int s = (int)WmiUtil.Num(r, "ConfiguredClockSpeed");
            if (s <= 0) s = (int)WmiUtil.Num(r, "Speed");
            speed = Math.Max(speed, s);
        }
        info.RamSpeedMHz = speed;
    }

    private static GpuVendor VendorFrom(string pnp, string name, string compat)
    {
        string all = (pnp + " " + name + " " + compat).ToUpperInvariant();
        if (all.Contains("VEN_10DE") || all.Contains("NVIDIA")) return GpuVendor.Nvidia;
        if (all.Contains("VEN_1002") || all.Contains("VEN_1022") || all.Contains("AMD") || all.Contains("RADEON") || all.Contains("ATI ")) return GpuVendor.Amd;
        if (all.Contains("VEN_8086") || all.Contains("INTEL")) return GpuVendor.Intel;
        return GpuVendor.Unknown;
    }

    private static void DetectGpu(SystemInfo info)
    {
        var rows = WmiUtil.Query(@"root\cimv2", "SELECT Name, AdapterCompatibility, PNPDeviceID, AdapterRAM, DriverVersion FROM Win32_VideoController",
            "Name", "AdapterCompatibility", "PNPDeviceID", "AdapterRAM", "DriverVersion");

        // Display sınıf anahtarlarını oku: gerçek VRAM (qwMemorySize) ve sürücü anahtar yolu
        var classEntries = new List<(string path, string desc, ulong vram, string matching)>();
        foreach (string sub in RegistryUtil.GetSubKeyNames(Hive.HKLM, DisplayClassKey))
        {
            if (!sub.All(char.IsDigit)) continue;
            string p = DisplayClassKey + "\\" + sub;
            string desc = RegistryUtil.GetString(Hive.HKLM, p, "DriverDesc") ?? string.Empty;
            string matching = RegistryUtil.GetString(Hive.HKLM, p, "MatchingDeviceId") ?? string.Empty;
            ulong vram = 0;
            object? q = RegistryUtil.GetValue(Hive.HKLM, p, "HardwareInformation.qwMemorySize");
            if (q is long ql) vram = (ulong)ql;
            else if (q is byte[] qb && qb.Length >= 8) vram = BitConverter.ToUInt64(qb, 0);
            if (vram == 0)
            {
                object? m = RegistryUtil.GetValue(Hive.HKLM, p, "HardwareInformation.MemorySize");
                if (m is int mi) vram = unchecked((uint)mi);
                else if (m is byte[] mb && mb.Length >= 4) vram = BitConverter.ToUInt32(mb, 0);
            }
            classEntries.Add((p, desc, vram, matching));
        }

        foreach (var r in rows)
        {
            string name = WmiUtil.Str(r, "Name");
            string pnp = WmiUtil.Str(r, "PNPDeviceID");
            if (name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Parsec", StringComparison.OrdinalIgnoreCase))
                continue;

            var gpu = new GpuInfo
            {
                Name = name,
                PnpDeviceId = pnp,
                Vendor = VendorFrom(pnp, name, WmiUtil.Str(r, "AdapterCompatibility")),
                DriverVersion = WmiUtil.Str(r, "DriverVersion"),
                VramBytes = (ulong)Math.Max(0, WmiUtil.Num(r, "AdapterRAM"))
            };
            var match = classEntries.FirstOrDefault(c => c.desc.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match.path != null)
            {
                gpu.ClassKeyPath = match.path;
                if (match.vram > gpu.VramBytes) gpu.VramBytes = match.vram;
            }
            info.Gpus.Add(gpu);
        }

        info.HasNvidia = info.Gpus.Any(g => g.Vendor == GpuVendor.Nvidia);
        info.HasAmdGpu = info.Gpus.Any(g => g.Vendor == GpuVendor.Amd);

        info.PrimaryGpu = info.Gpus
            .OrderBy(g => g.Vendor switch { GpuVendor.Nvidia => 0, GpuVendor.Amd => 1, GpuVendor.Intel => 2, _ => 3 })
            .ThenByDescending(g => g.VramBytes)
            .FirstOrDefault() ?? new GpuInfo();
    }

    private static void DetectPower(SystemInfo info)
    {
        if (NativeMethods.GetSystemPowerStatus(out var ps))
        {
            // 128 = sistem bataryası yok, 255 = bilinmiyor
            info.HasBattery = ps.BatteryFlag != 128 && ps.BatteryFlag != 255;
        }

        var batteries = WmiUtil.Query(@"root\cimv2", "SELECT Name FROM Win32_Battery", "Name");
        if (batteries.Count > 0) info.HasBattery = true;

        var enclosure = WmiUtil.Query(@"root\cimv2", "SELECT ChassisTypes FROM Win32_SystemEnclosure", "ChassisTypes");
        int[] laptopTypes = { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };
        foreach (var e in enclosure)
        {
            if (e.TryGetValue("ChassisTypes", out object? ct) && ct is Array arr)
            {
                foreach (object o in arr)
                {
                    try
                    {
                        if (laptopTypes.Contains(Convert.ToInt32(o))) info.IsLaptopChassis = true;
                    }
                    catch { }
                }
            }
        }
    }

    private static void DetectDisk(SystemInfo info)
    {
        char sysLetter = char.ToUpperInvariant((Environment.GetEnvironmentVariable("SystemDrive") ?? "C:")[0]);
        const string storage = @"root\Microsoft\Windows\Storage";

        long diskNumber = -1;
        foreach (var p in WmiUtil.Query(storage, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition", "DiskNumber", "DriveLetter"))
        {
            if (!p.TryGetValue("DriveLetter", out object? dl) || dl == null) continue;
            char letter;
            try { letter = char.ToUpperInvariant(Convert.ToChar(dl)); }
            catch { continue; }
            if (letter == sysLetter) { diskNumber = WmiUtil.Num(p, "DiskNumber"); break; }
        }

        var disks = WmiUtil.Query(storage, "SELECT DeviceId, MediaType, BusType, FriendlyName FROM MSFT_PhysicalDisk", "DeviceId", "MediaType", "BusType", "FriendlyName");
        Dictionary<string, object?>? target = null;
        if (diskNumber >= 0) target = disks.FirstOrDefault(d => WmiUtil.Str(d, "DeviceId") == diskNumber.ToString());
        if (target == null && disks.Count == 1) target = disks[0];

        if (target != null)
        {
            long media = WmiUtil.Num(target, "MediaType");
            long bus = WmiUtil.Num(target, "BusType");
            string fname = WmiUtil.Str(target, "FriendlyName");
            if (bus == 17) { info.SystemDiskType = "NVMe SSD"; info.IsSystemDiskSsd = true; info.IsSystemDiskKnown = true; }
            else if (media == 4) { info.SystemDiskType = "SATA SSD"; info.IsSystemDiskSsd = true; info.IsSystemDiskKnown = true; }
            else if (media == 3) { info.SystemDiskType = "HDD"; info.IsSystemDiskSsd = false; info.IsSystemDiskKnown = true; }
            else if (fname.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || fname.Contains("SSD", StringComparison.OrdinalIgnoreCase))
            { info.SystemDiskType = "SSD"; info.IsSystemDiskSsd = true; info.IsSystemDiskKnown = true; }
        }

        if (!info.IsSystemDiskKnown)
        {
            var drives = WmiUtil.Query(@"root\cimv2", "SELECT Model FROM Win32_DiskDrive", "Model");
            if (drives.Count > 0 && drives.All(d => WmiUtil.Str(d, "Model").Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                                                    WmiUtil.Str(d, "Model").Contains("NVMe", StringComparison.OrdinalIgnoreCase)))
            {
                info.SystemDiskType = "SSD";
                info.IsSystemDiskSsd = true;
                info.IsSystemDiskKnown = true;
            }
        }
    }

    public static bool LooksVirtual(string description, string name) =>
        VirtualMarkers.Any(m => description.Contains(m, StringComparison.OrdinalIgnoreCase) || name.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static void DetectNetwork(SystemInfo info)
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            IPInterfaceProperties props;
            try { props = ni.GetIPProperties(); } catch { continue; }
            bool hasIpv4 = props.UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            if (!hasIpv4) continue;

            bool hasGw = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.ToString().Equals("0.0.0.0"));
            var a = new NetworkAdapterInfo
            {
                Id = ni.Id,
                Name = ni.Name,
                Description = ni.Description,
                IsWireless = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                IsVirtual = LooksVirtual(ni.Description, ni.Name),
                HasGateway = hasGw,
                SpeedMbps = ni.Speed > 0 ? ni.Speed / 1_000_000 : 0
            };
            info.UpInterfaces.Add(a);
            if (hasGw && !a.IsVirtual) info.ActiveAdapters.Add(a);
        }

        foreach (var a in info.ActiveAdapters)
        {
            var rows = WmiUtil.Query(@"root\cimv2", $"SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE GUID = '{a.Id}'", "PNPDeviceID");
            if (rows.Count > 0) a.PnpDeviceId = WmiUtil.Str(rows[0], "PNPDeviceID");
        }

        info.PrimaryAdapter = info.ActiveAdapters.OrderBy(a => a.IsWireless ? 1 : 0).ThenByDescending(a => a.SpeedMbps).FirstOrDefault();
    }

    /// <summary>Ağ adaptörünün sürücü sınıf anahtarını (Class\{4d36e972...}\00XX) NetCfgInstanceId ile bulur.</summary>
    public static string? FindNetClassKey(string adapterGuid)
    {
        foreach (string sub in RegistryUtil.GetSubKeyNames(Hive.HKLM, NetClassKey))
        {
            if (!sub.All(char.IsDigit)) continue;
            string p = NetClassKey + "\\" + sub;
            string? id = RegistryUtil.GetString(Hive.HKLM, p, "NetCfgInstanceId");
            if (id != null && id.Equals(adapterGuid, StringComparison.OrdinalIgnoreCase)) return p;
        }
        return null;
    }

    public static ulong GetFreeRamBytes() => NativeMethods.GetMemoryStatus().ullAvailPhys;
}
