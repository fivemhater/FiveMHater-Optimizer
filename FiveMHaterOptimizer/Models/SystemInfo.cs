using System.Collections.Generic;

namespace FiveMHaterOptimizer.Models;

public enum GpuVendor { Unknown, Nvidia, Amd, Intel }

public sealed class GpuInfo
{
    public string Name { get; set; } = "Bilinmiyor";
    public GpuVendor Vendor { get; set; }
    public string PnpDeviceId { get; set; } = string.Empty;
    public ulong VramBytes { get; set; }
    public string DriverVersion { get; set; } = string.Empty;
    public string ClassKeyPath { get; set; } = string.Empty;
    public double VramGB => VramBytes / 1024d / 1024d / 1024d;
}

public sealed class NetworkAdapterInfo
{
    /// <summary>Arayüz GUID'i ("{...}").</summary>
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsWireless { get; set; }
    public bool IsVirtual { get; set; }
    public bool HasGateway { get; set; }
    public long SpeedMbps { get; set; }
    public string PnpDeviceId { get; set; } = string.Empty;
}

public sealed class SystemInfo
{
    public string OsCaption { get; set; } = "Windows";
    public int OsBuild { get; set; }
    public bool IsWindows11 => OsBuild >= 22000;

    public string CpuName { get; set; } = "Bilinmiyor";
    public string CpuManufacturer { get; set; } = string.Empty;
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public bool IsAmdCpu { get; set; }
    public bool IsIntelCpu { get; set; }
    public bool IsHybrid { get; set; }
    public int PerformanceCores { get; set; }
    public int EfficiencyCores { get; set; }
    public bool IsX3D { get; set; }

    public ulong TotalRamBytes { get; set; }
    public ulong InstalledRamKB { get; set; }
    public int RamSpeedMHz { get; set; }
    public double TotalRamGB => TotalRamBytes / 1024d / 1024d / 1024d;
    public double InstalledRamGB => InstalledRamKB > 0 ? InstalledRamKB / 1024d / 1024d : TotalRamGB;

    public GpuInfo PrimaryGpu { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = new();
    public bool HasNvidia { get; set; }
    public bool HasAmdGpu { get; set; }

    public bool HasBattery { get; set; }
    public bool IsLaptopChassis { get; set; }
    public bool IsLaptop => IsLaptopChassis || HasBattery;
    public bool IsDesktop => !IsLaptopChassis && !HasBattery;

    public string SystemDiskType { get; set; } = "Bilinmiyor";
    public bool IsSystemDiskSsd { get; set; }
    public bool IsSystemDiskKnown { get; set; }

    /// <summary>Ağ geçidi olan, çalışan fiziksel adaptörler.</summary>
    public List<NetworkAdapterInfo> ActiveAdapters { get; set; } = new();
    /// <summary>IPv4 etkin, çalışır durumdaki tüm arayüzler (TCP arayüz ayarları için).</summary>
    public List<NetworkAdapterInfo> UpInterfaces { get; set; } = new();
    public NetworkAdapterInfo? PrimaryAdapter { get; set; }

    public int ProcessCount { get; set; }
}
