using System.Windows;
using BleFinder.App.Audio;
using BleFinder.App.Bluetooth;
using BleFinder.App.Services;

namespace BleFinder.App;

public partial class App : Application
{
    internal WindowsBleScanner Scanner { get; private set; } = null!;

    internal WindowsProximitySound ProximitySound { get; private set; } = null!;

    internal WindowsClipboardService ClipboardService { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Scanner = new WindowsBleScanner();
        ProximitySound = new WindowsProximitySound();
        ClipboardService = new WindowsClipboardService();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Scanner?.Dispose();
        ProximitySound?.Dispose();

        base.OnExit(e);
    }
}
