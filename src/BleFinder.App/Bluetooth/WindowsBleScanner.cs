using BleFinder.Core.Models;
using BleFinder.Core.Scanning;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;

namespace BleFinder.App.Bluetooth;

public sealed class WindowsBleScanner : IBleScanner
{
    private readonly object _gate = new();
    // State mutation and enqueue share _gate; the single drainer preserves that FIFO order.
    private readonly Queue<ScannerStateChanged> _stateNotifications = new();
    private BluetoothLEAdvertisementWatcher? _watcher;
    private ScannerState _state = ScannerState.Stopped;
    private int _generation;
    private bool _isDrainingStateNotifications;
    private bool _disposed;

    public event EventHandler<BleObservation>? ObservationReceived;

    public event EventHandler<ScannerStateChanged>? StateChanged;

    public ScannerState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        int generation;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state is ScannerState.Starting or ScannerState.Scanning)
            {
                return;
            }

            generation = ++_generation;
            QueueStateLocked(ScannerState.Starting);
        }

        DrainStateNotifications();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrent(generation))
            {
                return;
            }

            if (adapter is null)
            {
                CompleteStart(generation, ScannerState.AdapterMissing);
                return;
            }

            var radio = await adapter.GetRadioAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrent(generation))
            {
                return;
            }

            if (radio is null || radio.State != RadioState.On)
            {
                CompleteStart(generation, ScannerState.BluetoothOff);
                return;
            }

            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active,
                AllowExtendedAdvertisements = true,
            };
            watcher.Received += OnWatcherReceived;
            watcher.Stopped += OnWatcherStopped;

            lock (_gate)
            {
                if (_disposed || generation != _generation)
                {
                    Detach(watcher);
                    return;
                }

                _watcher = watcher;

                try
                {
                    watcher.Start();
                }
                catch
                {
                    _watcher = null;
                    Detach(watcher);
                    throw;
                }

                var state = MapWatcherStatus(watcher.Status);
                if (state != ScannerState.Scanning)
                {
                    _watcher = null;
                    Detach(watcher);
                }

                QueueStateLocked(state);
            }

            DrainStateNotifications();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CompleteStart(generation, ScannerState.Stopped);
        }
        catch (Exception exception)
        {
            CompleteStart(generation, ScannerState.Aborted, ErrorCode(exception));
        }
    }

    public void Stop()
    {
        BluetoothLEAdvertisementWatcher? watcher;
        int generation;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            generation = ++_generation;
            watcher = _watcher;
            _watcher = null;

            if (watcher is not null)
            {
                Detach(watcher);
            }

            QueueStateLocked(ScannerState.Stopped);
        }

        DrainStateNotifications();

        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.Stop();
        }
        catch (Exception exception)
        {
            CompleteStart(generation, ScannerState.Aborted, ErrorCode(exception));
        }
    }

    public void Dispose()
    {
        BluetoothLEAdvertisementWatcher? watcher;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ++_generation;
            watcher = _watcher;
            _watcher = null;
            QueueStateLocked(ScannerState.Stopped);

            if (watcher is not null)
            {
                Detach(watcher);
            }
        }

        if (watcher is not null)
        {
            try
            {
                watcher.Stop();
            }
            catch
            {
                // Disposal is best-effort and must remain safe during application shutdown.
            }
        }

        DrainStateNotifications();
    }

    private void OnWatcherReceived(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(sender, _watcher) || _state != ScannerState.Scanning)
            {
                return;
            }
        }

        var observation = new BleObservation(
            args.BluetoothAddress,
            args.Advertisement.LocalName,
            args.RawSignalStrengthInDBm,
            args.Timestamp,
            args.Advertisement.ManufacturerData.Select(data => data.CompanyId).ToArray(),
            args.Advertisement.ServiceUuids.ToArray());

        ObservationReceived?.Invoke(this, observation);
    }

    private void OnWatcherStopped(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(sender, _watcher))
            {
                return;
            }

            _watcher = null;
            Detach(sender);

            var state = MapStopError(args.Error, sender.Status);
            QueueStateLocked(state, args.Error == BluetoothError.Success ? null : args.Error.ToString());
        }

        DrainStateNotifications();
    }

    private bool IsCurrent(int generation)
    {
        lock (_gate)
        {
            return !_disposed && generation == _generation;
        }
    }

    private void CompleteStart(int generation, ScannerState state, string? errorCode = null)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation)
            {
                return;
            }

            QueueStateLocked(state, errorCode);
        }

        DrainStateNotifications();
    }

    private void QueueStateLocked(ScannerState state, string? errorCode = null)
    {
        if (_state == state && errorCode is null)
        {
            return;
        }

        _state = state;
        _stateNotifications.Enqueue(new ScannerStateChanged(state, errorCode));
    }

    private void DrainStateNotifications()
    {
        lock (_gate)
        {
            if (_isDrainingStateNotifications || _stateNotifications.Count == 0)
            {
                return;
            }

            _isDrainingStateNotifications = true;
        }

        Exception? firstException = null;

        while (true)
        {
            ScannerStateChanged change;

            lock (_gate)
            {
                if (_stateNotifications.Count == 0)
                {
                    _isDrainingStateNotifications = false;
                    break;
                }

                change = _stateNotifications.Dequeue();
            }

            try
            {
                StateChanged?.Invoke(this, change);
            }
            catch (Exception exception)
            {
                firstException ??= exception;
            }
        }

        if (firstException is not null)
        {
            throw firstException;
        }
    }

    private static ScannerState MapWatcherStatus(BluetoothLEAdvertisementWatcherStatus status) => status switch
    {
        BluetoothLEAdvertisementWatcherStatus.Started => ScannerState.Scanning,
        BluetoothLEAdvertisementWatcherStatus.Aborted => ScannerState.Aborted,
        _ => ScannerState.Stopped,
    };

    private static ScannerState MapStopError(
        BluetoothError error,
        BluetoothLEAdvertisementWatcherStatus status) => error switch
    {
        BluetoothError.Success => MapWatcherStatus(status),
        BluetoothError.RadioNotAvailable or
        BluetoothError.DisabledByPolicy or
        BluetoothError.DisabledByUser => ScannerState.BluetoothOff,
        BluetoothError.NotSupported or
        BluetoothError.TransportNotSupported => ScannerState.AdapterMissing,
        _ => ScannerState.Aborted,
    };

    private static string ErrorCode(Exception exception) => $"0x{exception.HResult:X8}";

    private void Detach(BluetoothLEAdvertisementWatcher watcher)
    {
        watcher.Received -= OnWatcherReceived;
        watcher.Stopped -= OnWatcherStopped;
    }
}
