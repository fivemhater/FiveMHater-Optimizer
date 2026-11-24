using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using FiveMHaterOptimizer.Services;
using FiveMHaterOptimizer.Views;

namespace FiveMHaterOptimizer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // OEM kod sayfaları (netsh/bcdedit/schtasks Türkçe çıktıları için)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrash(args.ExceptionObject as Exception);

        base.OnStartup(e);

        if (!PrivilegeHelper.IsAdministrator())
        {
            MessageBox.Show("FiveMHater Optimizer yönetici yetkisi gerektirir. Lütfen sağ tıklayıp \"Yönetici olarak çalıştır\" seçin.",
                "FiveMHater Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception);
        MessageBox.Show("Beklenmeyen bir hata oluştu ve kaydedildi:\n" + e.Exception.Message,
            "FiveMHater Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.LogsDir, "crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }
    }
}
