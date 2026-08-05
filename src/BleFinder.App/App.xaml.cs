using System.Windows;
using BleFinder.App.Audio;
using BleFinder.App.Bluetooth;
using BleFinder.App.Services;
using BleFinder.Core.Devices;
using BleFinder.Core.Presentation;
using BleFinder.Core.Time;

namespace BleFinder.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var registry = new DeviceRegistry();
        var scanner = new WindowsBleScanner();
        var sound = new WindowsProximitySound();
        var clipboard = new WindowsClipboardService();
        var clock = new SystemClock();
        var viewModel = new MainViewModel(registry, scanner, sound, clipboard, clock);
        var window = new MainWindow
        {
            DataContext = viewModel,
        };

        MainWindow = window;
        window.Show();

        try
        {
            await viewModel.StartAsync();
        }
        catch (Exception exception)
        {
            if (window.IsLoaded)
            {
                MessageBox.Show(
                    window,
                    $"تعذر بدء المسح. يمكنك المحاولة مرة أخرى من زر بدء المسح.\n0x{exception.HResult:X8}",
                    "تعذر بدء المسح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
