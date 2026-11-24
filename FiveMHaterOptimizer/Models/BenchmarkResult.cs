using System;

namespace FiveMHaterOptimizer.Models;

public sealed class BenchmarkResult
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int ProcessCount { get; set; }
    public int ThreadCount { get; set; }
    public ulong FreeRamBytes { get; set; }
    public double PingMs { get; set; } = -1;
    public double JitterMs { get; set; } = -1;
    public double PacketLossPercent { get; set; } = 100;
    public double FreeRamGB => FreeRamBytes / 1024d / 1024d / 1024d;
    public bool HasPing => PingMs >= 0;
}

public sealed class PingStats
{
    public string Target { get; set; } = string.Empty;
    public int Sent { get; set; }
    public int Received { get; set; }
    public double AverageMs { get; set; } = -1;
    public double JitterMs { get; set; } = -1;
    public double MinMs { get; set; } = -1;
    public double MaxMs { get; set; } = -1;
    public double LossPercent => Sent == 0 ? 100 : (Sent - Received) * 100.0 / Sent;
}
