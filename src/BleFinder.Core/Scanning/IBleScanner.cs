using BleFinder.Core.Models;

namespace BleFinder.Core.Scanning;

public interface IBleScanner : IDisposable
{
    event EventHandler<BleObservation>? ObservationReceived;

    event EventHandler<ScannerStateChanged>? StateChanged;

    ScannerState State { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    void Stop();
}
