using System.Text.RegularExpressions;
using FiveMHaterOptimizer.Models;

namespace FiveMHaterOptimizer.Services;

/// <summary>schtasks.exe ile zamanlanmış görev durumunu (XML, dil bağımsız) okur ve yedekli olarak kapatır.</summary>
public sealed class ScheduledTaskTweaker
{
    private readonly BackupService _backup;
    private readonly Logger _log;
    private readonly VerificationList _verify;

    public ScheduledTaskTweaker(BackupService backup, Logger log, VerificationList verify)
    {
        _backup = backup;
        _log = log;
        _verify = verify;
    }

    /// <summary>null = görev yok; true/false = etkin mi.</summary>
    public static bool? QueryEnabled(string taskPath, Logger? log = null)
    {
        var r = ProcessRunner.Run("schtasks.exe", $"/Query /TN \"{taskPath}\" /XML", log, 30_000, logCommand: false, logOutput: false);
        if (!r.Success || !r.Output.Contains("<Task")) return null;
        var settings = Regex.Match(r.Output, @"<Settings>([\s\S]*?)</Settings>");
        string scope = settings.Success ? settings.Groups[1].Value : r.Output;
        var m = Regex.Match(scope, @"<Enabled>\s*(true|false)\s*</Enabled>", RegexOptions.IgnoreCase);
        if (!m.Success) return true; // Enabled öğesi yoksa varsayılan: etkin
        return m.Groups[1].Value.Equals("true", System.StringComparison.OrdinalIgnoreCase);
    }

    public bool Disable(string taskPath)
    {
        bool? state = QueryEnabled(taskPath);
        if (state == null)
        {
            _log.Detail($"Görev bulunamadı, atlandı: {taskPath}");
            return false;
        }

        _backup.RecordTask(new TaskBackupEntry { Path = taskPath, Existed = true, WasEnabled = state.Value });

        if (state == false)
        {
            _log.Detail($"Görev zaten kapalı: {taskPath}");
        }
        else
        {
            var r = ProcessRunner.Run("schtasks.exe", $"/Change /TN \"{taskPath}\" /DISABLE", _log, 30_000);
            if (!r.Success)
            {
                _log.Warn($"Görev kapatılamadı: {taskPath}");
                return false;
            }
            _log.Ok($"Görev kapatıldı: {taskPath}");
        }

        _verify.Add("task:" + taskPath, $"Zamanlanmış görev kapalı: {taskPath}", () => QueryEnabled(taskPath) == false);
        return true;
    }
}
