using System;
using System.Collections.Generic;
using FiveMHaterOptimizer.Models;

namespace FiveMHaterOptimizer.Services;

public sealed class OptimizationContext
{
    public OptimizationContext(OptimizationOptions options, SystemInfo system, Logger log, BackupService backup)
    {
        Options = options;
        System = system;
        Log = log;
        Backup = backup;
        Verify = new VerificationList();
        Reg = new RegistryTweaker(backup, log, Verify);
        Services = new ServiceTweaker(Reg, backup, log);
        Tasks = new ScheduledTaskTweaker(backup, log, Verify);
    }

    public OptimizationOptions Options { get; }
    public SystemInfo System { get; set; }
    public Logger Log { get; }
    public BackupService Backup { get; }
    public VerificationList Verify { get; }
    public RegistryTweaker Reg { get; }
    public ServiceTweaker Services { get; }
    public ScheduledTaskTweaker Tasks { get; }

    public BenchmarkResult? Before { get; set; }
    public BenchmarkResult? After { get; set; }
    public double CleanedMB { get; set; }
    public int KilledProcesses { get; set; }
    public int VerifiedOk { get; set; }
    public int VerifiedTotal { get; set; }
    public bool RebootRecommended { get; set; }
    public string? ChosenDns { get; set; }
    public List<string> GameExecutables { get; } = new();
    public List<string> RebootReasons { get; } = new();

    /// <summary>Modül içi alt ilerleme (0..1).</summary>
    public Action<double>? SubProgress { get; set; }

    public bool IsExtreme => Options.Profile == OptimizationProfile.Extreme;
    public bool IsBalanced => Options.Profile == OptimizationProfile.Balanced;
    public bool IsNetworkOnly => Options.Profile == OptimizationProfile.NetworkOnly;

    public void NeedsReboot(string reason)
    {
        RebootRecommended = true;
        if (!RebootReasons.Contains(reason)) RebootReasons.Add(reason);
    }
}
