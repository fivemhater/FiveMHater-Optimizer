using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using FiveMHaterOptimizer.ViewModels;

namespace FiveMHaterOptimizer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        TrySetIcon();

        _vm = new MainViewModel();
        DataContext = _vm;
        _vm.LogLineAppended += OnLogLine;
        _vm.LogCleared += () => LogBox.Clear();
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += async (_, _) => await _vm.InitializeAsync();
        Closing += OnClosing;
    }

    private void TrySetIcon()
    {
        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/fivemhater.ico", UriKind.Absolute));
        }
        catch
        {
            // İkon kaynağı yoksa pencere varsayılan ikonla açılır (exe ikonu ApplicationIcon ile gömülüdür)
        }
    }

    private void OnLogLine(string line)
    {
        bool atBottom = LogBox.VerticalOffset + LogBox.ViewportHeight >= LogBox.ExtentHeight - 24;
        LogBox.AppendText(line + Environment.NewLine);
        if (atBottom || LogBox.SelectionLength == 0) LogBox.ScrollToEnd();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Progress):
                AnimateProgress(_vm.Progress);
                break;
            case nameof(MainViewModel.Screen):
                FadeIn(_vm.Screen == AppScreen.Setup ? SetupScreen : LiveScreen);
                break;
        }
    }

    private void AnimateProgress(double to)
    {
        if (to <= 0)
        {
            MainProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
            MainProgress.Value = 0;
            return;
        }
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        MainProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    private static void FadeIn(UIElement element)
    {
        element.Opacity = 0;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_vm.CanClose()) return;
        MessageBox.Show(this, "Lütfen bekle, işlem bitene kadar pencereyi kapatma.\nYarım kalan bir işlem sistem ayarlarını tutarsız bırakabilir.",
            "FiveMHater Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
        e.Cancel = true;
    }
}
