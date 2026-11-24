using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using FiveMHaterOptimizer.Models;

namespace FiveMHaterOptimizer.Services;

public enum ServiceStart
{
    Automatic = 2,
    Manual = 3,
    Disabled = 4
}

/// <summary>
/// Servis başlangıç tiplerini kayıt defteri (Services\X\Start) üzerinden yedekli olarak değiştirir.
/// Koruma listesindeki servislere ve GPU sürücü servislerine ASLA dokunmaz.
/// </summary>
public sealed class ServiceTweaker
{
    public static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend", "mpssvc", "BFE", "Dhcp", "Dnscache", "NlaSvc", "nsi", "AudioSrv", "AudioEndpointBuilder",
        "RpcSs", "RpcEptMapper", "DcomLaunch", "Steam Client Service", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService",
        "WdNisSvc", "SecurityHealthService", "wscsvc", "Sense", "wuauserv", "UsoSvc", "WaaSMedicSvc", "TrustedInstaller",
        "CryptSvc", "Winmgmt", "EventLog", "Schedule", "ProfSvc", "LSM", "Power", "PlugPlay", "SamSs", "LanmanWorkstation",
        "Netman", "netprofm", "WlanSvc", "Wcmsvc", "Themes", "UserManager", "StateRepository", "AppXSvc", "ClipSVC",
        "Rockstar Service", "vgc", "vgk", "FACEIT", "BrokerInfrastructure", "SystemEventsBroker", "TimeBrokerSvc",
        "CoreMessagingRegistrar", "gpsvc", "iphlpsvc", "WinHttpAutoProxySvc", "KeyIso", "VaultSvc", "Audiosrv"
    };

    private static readonly string[] GpuDriverServicePrefixes =
    {
        "nvlddmkm", "NVDisplay", "NvContainer", "nvsvc", "amdkmdag", "amdfendr", "AMD External Events", "AMD Crash Defender",
        "amdlog", "AmdPPM", "igfx", "cphs", "IntelGraphics", "igccservice", "jhi_service", "esifsvc"
    };

    private readonly RegistryTweaker _reg;
    private readonly BackupService _backup;
    private readonly Logger _log;

    public int ChangedCount { get; private set; }
    public int StoppedCount { get; private set; }

    public ServiceTweaker(RegistryTweaker reg, BackupService backup, Logger log)
    {
        _reg = reg;
        _backup = backup;
        _log = log;
    }

    public static string ServiceKey(string name) => $@"SYSTEM\CurrentControlSet\Services\{name}";

    public static bool IsProtected(string name)
    {
        if (ProtectedServices.Contains(name)) return true;
        return GpuDriverServicePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    public bool Exists(string name) => RegistryUtil.KeyExists(Hive.HKLM, ServiceKey(name));

    public bool SetStart(string name, ServiceStart start, string reason)
    {
        if (IsProtected(name))
        {
            _log.Warn($"{name} koruma listesinde, dokunulmadı.");
            return false;
        }
        if (!Exists(name))
        {
            _log.Detail($"{name} bu sistemde yok, atlandı.");
            return false;
        }

        int? current = (int?)RegistryUtil.GetNumber(Hive.HKLM, ServiceKey(name), "Start");
        bool running = false;
        string display = name;
        try
        {
            using var sc = new ServiceController(name);
            display = sc.DisplayName;
            running = sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
        }
        catch { }

        _backup.RecordService(new ServiceBackupEntry
        {
            Name = name,
            DisplayName = display,
            OriginalStart = current,
            WasRunning = running
        });

        if (current == (int)start)
        {
            _log.Detail($"{name} ({display}) zaten {StartName(start)}.");
        }
        else if (!_reg.SetDword(Hive.HKLM, ServiceKey(name), "Start", (uint)start))
        {
            return false;
        }
        else
        {
            ChangedCount++;
            _log.Info($"Servis {name} ({display}): {StartName(current)} → {StartName(start)} — {reason}");
        }

        if (running) TryStop(name);
        return true;
    }

    private void TryStop(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) return;
            if (!sc.CanStop) { _log.Detail($"{name} şu an durdurulamıyor (yeniden başlatmada uygulanır)."); return; }
            if (sc.DependentServices.Any(d => d.Status == ServiceControllerStatus.Running))
            {
                _log.Detail($"{name} çalışan bağımlı servislere sahip; durdurulmadı (yeniden başlatmada uygulanır).");
                return;
            }
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            StoppedCount++;
            _log.Detail($"{name} durduruldu.");
        }
        catch (Exception ex)
        {
            _log.Detail($"{name} durdurulamadı ({ex.Message}); yeniden başlatmada devre dışı olacak.");
        }
    }

    public static string StartName(int? v) => v switch
    {
        0 => "Boot",
        1 => "System",
        2 => "Otomatik",
        3 => "El ile",
        4 => "Devre dışı",
        null => "Tanımsız",
        _ => v.ToString()!
    };

    public static string StartName(ServiceStart s) => StartName((int)s);
}
