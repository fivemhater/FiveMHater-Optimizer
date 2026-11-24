using System;
using System.IO;
using System.Text;

namespace FiveMHaterOptimizer.Services;

/// <summary>
/// Zaman damgalı canlı log. Her satır anında %ProgramData%\FiveMHater\logs altındaki kalıcı dosyaya yazılır.
/// </summary>
public sealed class Logger : IDisposable
{
    private readonly object _lock = new();
    private readonly StringBuilder _all = new();
    private StreamWriter? _writer;

    public string LogFilePath { get; }
    public int WarningCount { get; private set; }
    public int ErrorCount { get; private set; }

    public event Action<string>? LineLogged;

    public Logger(string prefix = "optimizer")
    {
        string file = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        string path;
        try
        {
            path = Path.Combine(AppPaths.LogsDir, file);
            _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(true)) { AutoFlush = true };
        }
        catch
        {
            path = Path.Combine(Path.GetTempPath(), file);
            try
            {
                _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(true)) { AutoFlush = true };
            }
            catch
            {
                _writer = null;
            }
        }
        LogFilePath = path;
    }

    public void Section(string title)
    {
        Write(string.Empty, raw: true);
        Write($"== {title} ==");
    }

    public void Info(string message) => Write(message);
    public void Ok(string message) => Write("✔ " + message);
    public void Detail(string message) => Write("   " + message);

    public void Warn(string message)
    {
        lock (_lock) WarningCount++;
        Write("! " + message);
    }

    public void Error(string message)
    {
        lock (_lock) ErrorCount++;
        Write("✖ " + message);
    }

    private void Write(string message, bool raw = false)
    {
        string line = raw ? message : $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_lock)
        {
            _all.AppendLine(line);
            try { _writer?.WriteLine(line); } catch { /* disk hatası logu durdurmasın */ }
        }
        try { LineLogged?.Invoke(line); } catch { }
    }

    public string GetAllText()
    {
        lock (_lock) return _all.ToString();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try { _writer?.Flush(); _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }
}
