using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FiveMHaterOptimizer.Models;
using FiveMHaterOptimizer.Services;
using Microsoft.Win32;

namespace FiveMHaterOptimizer.ViewModels;

public enum AppScreen
{
    Setup = 0,
    Live = 1
}

public sealed class MainViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _processTimer;
    private Logger? _logger;
    private CancellationTokenSource? _cts;
    private SystemInfo? _system;

    public MainViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;
        StartCommand = new RelayCommand(async () => await StartOptimizationAsync(), () => !IsBusy && !IsDetecting);
        RestoreCommand = new RelayCommand(async () => await StartRestoreAsync(), () => !IsBusy && HasBackup);
        ExportLogCommand = new RelayCommand(ExportLog, () => _logger != null && !IsBusy);
        RebootCommand = new RelayCommand(Reboot, () => !IsBusy);
        CloseCommand = new RelayCommand(() => Application.Current.MainWindow?.Close(), () => !IsBusy);
        BackCommand = new RelayCommand(BackToSetup, () => !IsBusy);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);

        _processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _processTimer.Tick += (_, _) =>
        {
            if (Screen == AppScreen.Setup && !IsBusy) ProcessValue = HardwareDetector.CountProcesses().ToString();
        };
        RefreshBackupState();
    }

    // ---------- Events ----------
    public event Action<string>? LogLineAppended;
    public event Action? LogCleared;

    // ---------- Commands ----------
    public RelayCommand StartCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand ExportLogCommand { get; }
    public RelayCommand RebootCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }

    // ---------- Screen state ----------
    private AppScreen _screen = AppScreen.Setup;
    public AppScreen Screen
    {
        get => _screen;
        set
        {
            if (SetField(ref _screen, value))
            {
                OnPropertyChanged(nameof(IsSetupScreen));
                OnPropertyChanged(nameof(IsLiveScreen));
            }
        }
    }
    public bool IsSetupScreen => Screen == AppScreen.Setup;
    public bool IsLiveScreen => Screen == AppScreen.Live;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowWaitWarning));
                RelayCommand.Refresh();
            }
        }
    }

    private bool _isDetecting = true;
    public bool IsDetecting { get => _isDetecting; set { SetField(ref _isDetecting, value); RelayCommand.Refresh(); } }

    private bool _isCompleted;
    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (SetField(ref _isCompleted, value))
            {
                OnPropertyChanged(nameof(ShowWaitWarning));
                OnPropertyChanged(nameof(ShowResultBadges));
                OnPropertyChanged(nameof(ShowRestoreSummary));
            }
        }
    }

    private bool _isRestoreMode;
    public bool IsRestoreMode
    {
        get => _isRestoreMode;
        set
        {
            if (SetField(ref _isRestoreMode, value))
            {
                OnPropertyChanged(nameof(ShowResultBadges));
                OnPropertyChanged(nameof(ShowRestoreSummary));
            }
        }
    }

    public bool ShowWaitWarning => IsBusy && !IsCompleted;
    public bool ShowResultBadges => IsCompleted && !IsRestoreMode;
    public bool ShowRestoreSummary => IsCompleted && IsRestoreMode;

    private bool _hasBackup;
    public bool HasBackup { get => _hasBackup; set { SetField(ref _hasBackup, value); RelayCommand.Refresh(); } }

    // ---------- System summary ----------
    private string _cpuValue = "Algılanıyor...";
    public string CpuValue { get => _cpuValue; set => SetField(ref _cpuValue, value); }
    private string _cpuDetail = string.Empty;
    public string CpuDetail { get => _cpuDetail; set => SetField(ref _cpuDetail, value); }
    private string _ramValue = "Algılanıyor...";
    public string RamValue { get => _ramValue; set => SetField(ref _ramValue, value); }
    private string _ramDetail = string.Empty;
    public string RamDetail { get => _ramDetail; set => SetField(ref _ramDetail, value); }
    private string _gpuValue = "Algılanıyor...";
    public string GpuValue { get => _gpuValue; set => SetField(ref _gpuValue, value); }
    private string _gpuDetail = string.Empty;
    public string GpuDetail { get => _gpuDetail; set => SetField(ref _gpuDetail, value); }
    private string _netValue = "Algılanıyor...";
    public string NetValue { get => _netValue; set => SetField(ref _netValue, value); }
    private string _netDetail = string.Empty;
    public string NetDetail { get => _netDetail; set => SetField(ref _netDetail, value); }
    private string _processValue = "...";
    public string ProcessValue { get => _processValue; set => SetField(ref _processValue, value); }
    private string _processDetail = "canlı";
    public string ProcessDetail { get => _processDetail; set => SetField(ref _processDetail, value); }

    // ---------- Profile & toggles ----------
    private OptimizationProfile _profile = OptimizationProfile.Ultra;
    public OptimizationProfile SelectedProfile
    {
        get => _profile;
        set
        {
            if (!SetField(ref _profile, value)) return;
            OnPropertyChanged(nameof(IsUltra));
            OnPropertyChanged(nameof(IsExtreme));
            OnPropertyChanged(nameof(IsBalanced));
            OnPropertyChanged(nameof(IsNetworkOnly));
            OnPropertyChanged(nameof(TogglesEnabled));
            if (value == OptimizationProfile.Balanced) DisablePrinter = false;
            if (value is OptimizationProfile.Extreme or OptimizationProfile.Ultra) DisablePrinter = true;
        }
    }

    public bool IsUltra { get => SelectedProfile == OptimizationProfile.Ultra; set { if (value) SelectedProfile = OptimizationProfile.Ultra; } }
    public bool IsExtreme { get => SelectedProfile == OptimizationProfile.Extreme; set { if (value) SelectedProfile = OptimizationProfile.Extreme; } }
    public bool IsBalanced { get => SelectedProfile == OptimizationProfile.Balanced; set { if (value) SelectedProfile = OptimizationProfile.Balanced; } }
    public bool IsNetworkOnly { get => SelectedProfile == OptimizationProfile.NetworkOnly; set { if (value) SelectedProfile = OptimizationProfile.NetworkOnly; } }
    public bool TogglesEnabled => SelectedProfile != OptimizationProfile.NetworkOnly;

    private bool _clearCache = true;
    public bool ClearFiveMCache { get => _clearCache; set => SetField(ref _clearCache, value); }
    private bool _fpsPreset = true;
    public bool ApplyFpsPreset { get => _fpsPreset; set => SetField(ref _fpsPreset, value); }
    private bool _disablePrinter = true;
    public bool DisablePrinter { get => _disablePrinter; set => SetField(ref _disablePrinter, value); }

    // ---- Ultra / Full+Full seçenekleri ----
    private bool _keepDiscordSteam = true;
    public bool KeepDiscordSteamStartup { get => _keepDiscordSteam; set => SetField(ref _keepDiscordSteam, value); }
    private bool _disableBluetooth;
    public bool DisableBluetooth { get => _disableBluetooth; set => SetField(ref _disableBluetooth, value); }
    private bool _disableOneDrive = true;
    public bool DisableOneDriveStartup { get => _disableOneDrive; set => SetField(ref _disableOneDrive, value); }
    private bool _removeBloatware;
    public bool RemoveBloatware { get => _removeBloatware; set => SetField(ref _removeBloatware, value); }
    private bool _runDism;
    public bool RunDismCleanup { get => _runDism; set => SetField(ref _runDism, value); }

    public RelayCommand OpenDiscordCommand { get; } = new RelayCommand(OpenDiscord);

    public const string DiscordUrl = "https://discord.gg/fivemhater";

    /// <summary>Yalnızca kullanıcı tıklayınca varsayılan tarayıcıda açılır; otomatik açılmaz.</summary>
    private static void OpenDiscord()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DiscordUrl) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Bağlantı açılamadı: " + ex.Message + "\n" + DiscordUrl, "FiveMHater Optimizer", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    // ---------- Live screen ----------
    private string _title = "Optimizasyon uygulanıyor";
    public string Title { get => _title; set => SetField(ref _title, value); }
    private string _subtitle = "Sistem hazırlığı";
    public string Subtitle { get => _subtitle; set => SetField(ref _subtitle, value); }
    private double _progress;
    public double Progress
    {
        get => _progress;
        set
        {
            if (SetField(ref _progress, value)) OnPropertyChanged(nameof(ProgressText));
        }
    }
    public string ProgressText => $"%{Math.Floor(Progress):0}";

    // ---------- Result badges ----------
    private string _badgeProcess = "-";
    public string BadgeProcess { get => _badgeProcess; set => SetField(ref _badgeProcess, value); }
    private string _badgeRam = "-";
    public string BadgeRam { get => _badgeRam; set => SetField(ref _badgeRam, value); }
    private string _badgePing = "-";
    public string BadgePing { get => _badgePing; set => SetField(ref _badgePing, value); }
    private string _badgeJitter = string.Empty;
    public string BadgeJitter { get => _badgeJitter; set => SetField(ref _badgeJitter, value); }
    private string _badgeCleaned = "-";
    public string BadgeCleaned { get => _badgeCleaned; set => SetField(ref _badgeCleaned, value); }
    private string _badgeVerify = string.Empty;
    public string BadgeVerify { get => _badgeVerify; set => SetField(ref _badgeVerify, value); }
    private string _restoreSummary = string.Empty;
    public string RestoreSummary { get => _restoreSummary; set => SetField(ref _restoreSummary, value); }
    private bool _rebootRecommended;
    public bool RebootRecommended { get => _rebootRecommended; set => SetField(ref _rebootRecommended, value); }

    // ---------- Lifecycle ----------
    public async Task InitializeAsync()
    {
        IsDetecting = true;
        try
        {
            _system = await Task.Run(HardwareDetector.Detect);
            ApplySummary(_system);
        }
        catch (Exception ex)
        {
            CpuValue = "Algılanamadı";
            CpuDetail = ex.Message;
        }
        finally
        {
            IsDetecting = false;
            _processTimer.Start();
        }
    }

    private void ApplySummary(SystemInfo s)
    {
        CpuValue = Shorten(s.CpuName, 34);
        string extra = s.IsHybrid ? $"Hibrit {s.PerformanceCores}P + {s.EfficiencyCores}E" : s.IsX3D ? "X3D V-Cache" : s.IsAmdCpu ? "AMD" : s.IsIntelCpu ? "Intel" : "";
        CpuDetail = $"{s.CpuCores} çekirdek / {s.CpuThreads} thread{(extra.Length > 0 ? " · " + extra : "")}";
        RamValue = $"{Math.Round(s.InstalledRamGB):0} GB";
        RamDetail = s.RamSpeedMHz > 0 ? $"{s.RamSpeedMHz} MHz · {(s.IsLaptop ? "Dizüstü" : "Masaüstü")}" : (s.IsLaptop ? "Dizüstü" : "Masaüstü");
        GpuValue = Shorten(s.PrimaryGpu.Name, 34);
        GpuDetail = s.PrimaryGpu.Vendor == GpuVendor.Unknown ? "Bilinmiyor" :
            $"{(s.PrimaryGpu.Vendor == GpuVendor.Nvidia ? "NVIDIA" : s.PrimaryGpu.Vendor == GpuVendor.Amd ? "AMD" : "Intel")}{(s.PrimaryGpu.VramBytes > 0 ? $" · {s.PrimaryGpu.VramGB:0.#} GB VRAM" : "")}";
        if (s.PrimaryAdapter != null)
        {
            NetValue = Shorten(s.PrimaryAdapter.Description, 34);
            NetDetail = $"{(s.PrimaryAdapter.IsWireless ? "Wi-Fi" : "Ethernet")}{(s.PrimaryAdapter.SpeedMbps > 0 ? $" · {s.PrimaryAdapter.SpeedMbps} Mbps" : "")}";
        }
        else
        {
            NetValue = "Aktif bağlantı yok";
            NetDetail = "-";
        }
        ProcessValue = s.ProcessCount.ToString();
        ProcessDetail = $"canlı · {s.SystemDiskType}";
    }

    private static string Shorten(string s, int max) =>
        string.IsNullOrWhiteSpace(s) ? "Bilinmiyor" : (s.Length <= max ? s.Trim() : s.Trim()[..(max - 1)] + "…");

    public void RefreshBackupState() => HasBackup = BackupService.BackupExists;

    private Logger NewLogger(string prefix)
    {
        _logger?.Dispose();
        var log = new Logger(prefix);
        log.LineLogged += line => _dispatcher.BeginInvoke(() => LogLineAppended?.Invoke(line));
        _logger = log;
        LogCleared?.Invoke();
        return log;
    }

    private async Task StartOptimizationAsync()
    {
        var options = new OptimizationOptions
        {
            Profile = SelectedProfile,
            ClearFiveMCache = TogglesEnabled && ClearFiveMCache,
            ApplyFiveMFpsPreset = TogglesEnabled && ApplyFpsPreset,
            DisablePrinterServices = TogglesEnabled && DisablePrinter,
            KeepDiscordSteamStartup = KeepDiscordSteamStartup,
            DisableBluetooth = IsUltra && DisableBluetooth,
            DisableOneDriveStartup = IsUltra && DisableOneDriveStartup,
            RemoveBloatware = TogglesEnabled && RemoveBloatware,
            RunDismCleanup = TogglesEnabled && RunDismCleanup
        };

        string msg = $"\"{options.ProfileDisplayName}\" profili uygulanacak.\n\n" +
                     "• Önce Sistem Geri Yükleme Noktası ve tam JSON yedeği alınır.\n" +
                     "• Tüm değişiklikler \"Tam Geri Al (Restore)\" ile geri alınabilir.\n" +
                     (options.ClearFiveMCache ? "• FiveM açıksa önbellek temizliği için kapatılacak.\n" : "") +
                     (options.RunDismCleanup ? "• Windows bileşen temizliği (DISM) çalışacak: 5-15 dk sürebilir.\n" : "") +
                     (options.RemoveBloatware ? "• DİKKAT: Hazır gelen gereksiz uygulamalar kaldırılacak. Bu adım GERİ ALINAMAZ (Microsoft Store'dan yeniden yüklenebilir).\n" : "") +
                     (options.Profile == OptimizationProfile.Ultra
                         ? "• Ultra: koruma listesi dışındaki başlangıç uygulamaları ve ek servisler kapatılır (hepsi geri alınabilir).\n" +
                           (options.DisableBluetooth ? "• Bluetooth servisleri KAPATILACAK (Bluetooth cihazlar çalışmaz).\n" : "") +
                           "• Sonuçları ölçmek için yeniden başlatıp 2-3 dakika bekleyin.\n"
                         : "") +
                     "\nDevam edilsin mi?";
        if (MessageBox.Show(msg, "FiveMHater Optimizer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var log = NewLogger("optimizer");
        PrepareLive(restore: false);
        _cts = new CancellationTokenSource();
        var progress = new Progress<EngineProgress>(p =>
        {
            Subtitle = p.Stage;
            if (p.Percent > Progress) Progress = Math.Min(100, p.Percent);
        });

        bool promptUltraReboot = false;
        try
        {
            _system ??= await Task.Run(HardwareDetector.Detect);
            var ctx = await new OptimizerEngine().RunAsync(options, _system, log, progress, _cts.Token);
            Progress = 100;
            Title = "Optimizasyon tamamlandı";
            Subtitle = ctx.RebootRecommended ? "Tüm ayarların etkinleşmesi için yeniden başlatma önerilir" : "Doğrulama ve sonuç";
            FillBadges(ctx);
            RebootRecommended = ctx.RebootRecommended;
            if (options.Profile == OptimizationProfile.Ultra)
            {
                log.Info("Ultra tamamlandı: değişikliklerin tamamı yeniden başlatma sonrası etkinleşir. İşlem sayısını açılıştan 2-3 dk sonra ölçün.");
                promptUltraReboot = true;
            }
        }
        catch (OperationCanceledException)
        {
            Title = "Optimizasyon durduruldu";
            Subtitle = "Uygulanan değişiklikler yedekte; Tam Geri Al ile geri alınabilir";
        }
        catch (Exception ex)
        {
            log.Error("Beklenmeyen hata: " + ex);
            Title = "Optimizasyon hata ile bitti";
            Subtitle = ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsCompleted = true;
            RefreshBackupState();
            log.Dispose();
        }

        if (promptUltraReboot)
        {
            await Task.Delay(400); // önce sonuç ekranı çizilsin
            if (MessageBox.Show("Ultra / Full+Full uygulandı.\n\nBaşlangıç uygulamaları, servisler ve svchost birleştirme ancak yeniden başlatma sonrası tam etkinleşir. " +
                                "En doğru işlem sayısı için açılıştan 2-3 dakika sonra kontrol edin.\n\nŞimdi yeniden başlatılsın mı? (10 saniye sonra; açık çalışmalarınızı kaydedin)",
                    "Yeniden Başlat", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                ProcessRunner.Run("shutdown.exe", "/r /t 10 /c \"FiveMHater Optimizer Ultra: ayarların etkinleşmesi için yeniden başlatılıyor\"", null, 15_000);
            }
        }
    }

    private void FillBadges(OptimizationContext ctx)
    {
        var b = ctx.Before;
        var a = ctx.After;
        BadgeProcess = b != null && a != null ? $"{b.ProcessCount} → {a.ProcessCount}" : "-";
        BadgeRam = b != null && a != null ? $"{b.FreeRamGB:0.0} → {a.FreeRamGB:0.0} GB" : "-";
        if (b != null && a != null && b.HasPing && a.HasPing)
        {
            BadgePing = $"{b.PingMs:0} ms → {a.PingMs:0} ms";
            BadgeJitter = $"Jitter {b.JitterMs:0.0} → {a.JitterMs:0.0} ms";
        }
        else
        {
            BadgePing = "Ölçülemedi";
            BadgeJitter = "İnternet erişimi yok";
        }
        BadgeCleaned = $"{ctx.CleanedMB:0} MB";
        BadgeVerify = $"Doğrulama: {ctx.VerifiedOk} / {ctx.VerifiedTotal} ayar yerinde";
    }

    private async Task StartRestoreAsync()
    {
        if (MessageBox.Show("Tüm FiveMHater değişiklikleri backup.json'daki orijinal değerlerine döndürülecek.\n\nDevam edilsin mi?",
                "Tam Geri Al (Restore)", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var log = NewLogger("restore");
        PrepareLive(restore: true);
        _cts = new CancellationTokenSource();
        var progress = new Progress<EngineProgress>(p =>
        {
            Subtitle = p.Stage;
            if (p.Percent > Progress) Progress = Math.Min(100, p.Percent);
        });
        try
        {
            var r = await new RestoreEngine().RunAsync(log, progress, _cts.Token);
            Progress = 100;
            Title = r.Failures == 0 ? "Geri alma tamamlandı" : "Geri alma kısmen tamamlandı";
            Subtitle = "Yeniden başlatma önerilir";
            RestoreSummary = $"{r.RegistryRestored} değer geri yüklendi · {r.RegistryDeleted} yeni değer silindi · Doğrulama: {r.VerifiedOk} / {r.VerifiedTotal}" +
                             (r.Failures > 0 ? $" · {r.Failures} hata (yedek korunuyor)" : "");
            RebootRecommended = true;
        }
        catch (Exception ex)
        {
            log.Error("Geri alma hatası: " + ex);
            Title = "Geri alma hata ile bitti";
            Subtitle = ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsCompleted = true;
            RefreshBackupState();
            log.Dispose();
        }
    }

    private void PrepareLive(bool restore)
    {
        IsRestoreMode = restore;
        IsCompleted = false;
        Progress = 0;
        Title = restore ? "Geri alma uygulanıyor" : "Optimizasyon uygulanıyor";
        Subtitle = restore ? "Yedek okunuyor" : "Sistem hazırlığı";
        RebootRecommended = false;
        IsBusy = true;
        Screen = AppScreen.Live;
    }

    private void BackToSetup()
    {
        Screen = AppScreen.Setup;
        IsCompleted = false;
        RefreshBackupState();
        _ = Task.Run(() =>
        {
            int c = HardwareDetector.CountProcesses();
            _dispatcher.BeginInvoke(() => ProcessValue = c.ToString());
        });
    }

    private void ExportLog()
    {
        if (_logger == null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Logu Dışa Aktar",
            FileName = Path.GetFileName(_logger.LogFilePath),
            Filter = "Log dosyası (*.log)|*.log|Metin dosyası (*.txt)|*.txt",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, _logger.GetAllText(), new System.Text.UTF8Encoding(true));
            MessageBox.Show($"Log kaydedildi:\n{dlg.FileName}", "FiveMHater Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Log kaydedilemedi: " + ex.Message, "FiveMHater Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(AppPaths.WindowsDir, "explorer.exe"), $"\"{AppPaths.LogsDir}\"")
            {
                UseShellExecute = false
            });
        }
        catch { }
    }

    private void Reboot()
    {
        if (MessageBox.Show("Bilgisayar 10 saniye içinde yeniden başlatılacak. Açık çalışmalarınızı kaydettiniz mi?",
                "Yeniden Başlat", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        ProcessRunner.Run("shutdown.exe", "/r /t 10 /c \"FiveMHater Optimizer: ayarların etkinleşmesi için yeniden başlatılıyor\"", null, 15_000);
    }

    public bool CanClose() => !IsBusy;
}
