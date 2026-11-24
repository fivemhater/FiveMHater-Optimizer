using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace FiveMHaterOptimizer.Services;

/// <summary>
/// Yalnızca resmi Windows araçlarını (System32) açık argümanlarla, pencere açmadan (CreateNoWindow) çalıştırır
/// ve çıktıyı loglar. Şifreli/gizli PowerShell kullanılmaz.
/// </summary>
public static class ProcessRunner
{
    public sealed record Result(int ExitCode, string Output, string Error)
    {
        public bool Success => ExitCode == 0;
    }

    private static Encoding? _oem;

    private static Encoding OemEncoding
    {
        get
        {
            if (_oem != null) return _oem;
            try { _oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { _oem = Encoding.UTF8; }
            return _oem;
        }
    }

    public static string ResolveSystemTool(string exe)
    {
        if (Path.IsPathRooted(exe)) return exe;
        string candidate = Path.Combine(AppPaths.System32, exe);
        return File.Exists(candidate) ? candidate : exe;
    }

    public static Result Run(string exe, string arguments, Logger? log, int timeoutMs = 120_000, bool logCommand = true, bool logOutput = true)
    {
        string path = ResolveSystemTool(exe);
        if (logCommand) log?.Detail($"> {Path.GetFileName(path)} {arguments}");

        var psi = new ProcessStartInfo(path, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Normal,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = OemEncoding,
            StandardErrorEncoding = OemEncoding,
            WorkingDirectory = AppPaths.System32
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        try
        {
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (stdout) stdout.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                log?.Warn($"{Path.GetFileName(path)} zaman aşımına uğradı ({timeoutMs / 1000} sn).");
                return new Result(-1, stdout.ToString(), stderr.ToString());
            }
            p.WaitForExit();

            string o, e2;
            lock (stdout) o = stdout.ToString();
            lock (stderr) e2 = stderr.ToString();

            if (logOutput && log != null)
            {
                foreach (string line in (o + e2).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = line.Trim();
                    if (t.Length > 0) log.Detail("  " + t);
                }
            }
            if (p.ExitCode != 0 && logCommand) log?.Detail($"  (çıkış kodu {p.ExitCode})");
            return new Result(p.ExitCode, o, e2);
        }
        catch (Exception ex)
        {
            log?.Warn($"{Path.GetFileName(path)} çalıştırılamadı: {ex.Message}");
            return new Result(-1, string.Empty, ex.Message);
        }
    }
}
