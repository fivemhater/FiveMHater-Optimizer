using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FiveMHaterOptimizer.Views;

public partial class LogoView : UserControl
{
    public LogoView()
    {
        InitializeComponent();
        LoadLogo();
    }

    private void LoadLogo()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/fivemhater_logo.png", UriKind.Absolute));
            if (info?.Stream == null) return;
            var bmp = new BitmapImage();
            using (info.Stream)
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = info.Stream;
                bmp.EndInit();
            }
            bmp.Freeze();
            LogoImage.Source = bmp;
            LogoImage.Visibility = Visibility.Visible;
            FallbackLogo.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // PNG yoksa/bozuksa vektörel yedek logo görünür kalır
            LogoImage.Visibility = Visibility.Collapsed;
            FallbackLogo.Visibility = Visibility.Visible;
        }
    }
}
