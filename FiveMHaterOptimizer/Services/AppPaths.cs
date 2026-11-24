using System;
using System.IO;

namespace FiveMHaterOptimizer.Services;

public static class AppPaths
{
    public static string DataDir
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FiveMHater");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string BackupFile => Path.Combine(DataDir, "backup.json");

    public static string LogsDir
    {
        get
        {
            string dir = Path.Combine(DataDir, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string System32 => Environment.SystemDirectory;
    public static string WindowsDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
}
