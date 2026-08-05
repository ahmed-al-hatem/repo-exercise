using System.Collections.ObjectModel;
using BleFinder.Core.Audio;
using BleFinder.Core.Devices;
using BleFinder.Core.Models;
using BleFinder.Core.Scanning;
using BleFinder.Core.Services;
using BleFinder.Core.Time;

namespace BleFinder.Core.Presentation;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly DeviceRegistry _registry;
    private readonly IBleScanner _scanner;
    private readonly IProximitySound _sound;
    private readonly IClipboardService _clipboard;
    private readonly IClock _clock;
    private readonly object _pendingScannerStateGate = new();
    private readonly Dictionary<ulong, DeviceRowViewModel> _deviceRows = [];
    private IReadOnlyList<DeviceSnapshot> _currentSnapshots = [];
    private ScannerStateChanged? _pendingScannerState;
    private TrackingViewModel? _tracking;
    private DeviceSnapshot? _lastSelectedSnapshot;
    private ulong? _selectedAddress;
    private string _searchText = string.Empty;
    private string _statusText;
    private string _statusGuidanceText;
    private string _deviceCountText = "0 أجهزة";
    private string? _scannerErrorCode;
    private bool _isScanning;
    private bool _hideAddresses;
    private bool _startInProgress;
    private bool _disposed;

    public MainViewModel(
        DeviceRegistry registry,
        IBleScanner scanner,
        IProximitySound sound,
        IClipboardService clipboard,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(clock);

        _registry = registry;
        _scanner = scanner;
        _sound = sound;
        _clipboard = clipboard;
        _clock = clock;
        _statusText = StatusFor(scanner.State);
        _statusGuidanceText = GuidanceFor(scanner.State);
        _isScanning = scanner.State is ScannerState.Starting or ScannerState.Scanning;

        StartCommand = new AsyncRelayCommand(StartAsync);
        StopCommand = new RelayCommand(Stop);
        SelectCommand = new RelayCommand(Select);
        CopyAddressCommand = new RelayCommand(CopyAddress);
        BackCommand = new RelayCommand(Back);

        Devices = new ObservableCollection<DeviceRowViewModel>();

        _scanner.ObservationReceived += OnObservationReceived;
        _scanner.StateChanged += OnScannerStateChanged;
    }

    public AsyncRelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand SelectCommand { get; }

    public RelayCommand CopyAddressCommand { get; }

    public RelayCommand BackCommand { get; }

    public ObservableCollection<DeviceRowViewModel> Devices { get; }

    public TrackingViewModel? Tracking
    {
        get => _tracking;
        private set => SetProperty(ref _tracking, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value ?? string.Empty);
    }

    public bool HideAddresses
    {
        get => _hideAddresses;
        set
        {
            if (!SetProperty(ref _hideAddresses, value))
            {
                return;
            }

            foreach (var device in Devices)
            {
                device.SetAddressHidden(value);
            }

            Tracking?.SetAddressHidden(value);
        }
    }

    public bool SoundEnabled
    {
        get => _sound.Enabled;
        set
        {
            if (_sound.Enabled == value)
            {
                return;
            }

            _sound.Enabled = value;
            OnPropertyChanged();

            if (Tracking is not null && Tracking.SoundEnabled != value)
            {
                Tracking.SoundEnabled = value;
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusGuidanceText
    {
        get => _statusGuidanceText;
        private set => SetProperty(ref _statusGuidanceText, value);
    }

    public string DeviceCountText
    {
        get => _deviceCountText;
        private set => SetProperty(ref _deviceCountText, value);
    }

    public string? ScannerErrorCode
    {
        get => _scannerErrorCode;
        private set => SetProperty(ref _scannerErrorCode, value);
    }

    public async Task StartAsync()
    {
        ThrowIfDisposed();

        if (_startInProgress || _scanner.State is ScannerState.Starting or ScannerState.Scanning)
        {
            return;
        }

        _startInProgress = true;
        StartCommand.RaiseCanExecuteChanged();

        try
        {
            await _scanner.StartAsync();
        }
        finally
        {
            _startInProgress = false;
            StartCommand.RaiseCanExecuteChanged();
        }
    }

    public void Refresh()
    {
        ThrowIfDisposed();
        ApplyPendingScannerState();

        var now = _clock.Now;
        _currentSnapshots = _registry.Snapshot(now);

        SynchronizeDeviceRows(_currentSnapshots, now);

        IReadOnlyList<DeviceSnapshot> visibleSnapshots = string.IsNullOrWhiteSpace(SearchText)
            ? _currentSnapshots
            : _currentSnapshots
                .Where(snapshot => AddressFormatter.Matches(snapshot.Address, snapshot.LocalName, SearchText))
                .ToArray();

        SynchronizeVisibleDevices(visibleSnapshots);
        DeviceCountText = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Devices.Count} أجهزة");

        UpdateTracking(now);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scanner.ObservationReceived -= OnObservationReceived;
        _scanner.StateChanged -= OnScannerStateChanged;
        _scanner.Stop();
        _scanner.Dispose();
        _sound.Dispose();
    }

    private void OnObservationReceived(object? sender, BleObservation observation) =>
        _registry.Record(observation);

    private void OnScannerStateChanged(object? sender, ScannerStateChanged change)
    {
        lock (_pendingScannerStateGate)
        {
            _pendingScannerState = change;
        }
    }

    private void ApplyScannerState(ScannerState state, string? errorCode)
    {
        IsScanning = state is ScannerState.Starting or ScannerState.Scanning;
        ScannerErrorCode = errorCode;
        StatusText = StatusFor(state);
        StatusGuidanceText = GuidanceFor(state);
    }

    private void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _scanner.Stop();
    }

    private void ApplyPendingScannerState()
    {
        ScannerStateChanged? change;

        lock (_pendingScannerStateGate)
        {
            change = _pendingScannerState;
            _pendingScannerState = null;
        }

        if (change is not null)
        {
            ApplyScannerState(change.State, change.ErrorCode);
        }
    }

    private void SynchronizeDeviceRows(IReadOnlyList<DeviceSnapshot> snapshots, DateTimeOffset now)
    {
        var currentAddresses = snapshots.Select(snapshot => snapshot.Address).ToHashSet();

        foreach (var removedAddress in _deviceRows.Keys.Where(address => !currentAddresses.Contains(address)).ToArray())
        {
            _deviceRows.Remove(removedAddress);
        }

        foreach (var snapshot in snapshots)
        {
            if (_deviceRows.TryGetValue(snapshot.Address, out var row))
            {
                row.Update(snapshot, now, HideAddresses);
            }
            else
            {
                _deviceRows.Add(snapshot.Address, new DeviceRowViewModel(snapshot, now, HideAddresses));
            }
        }
    }

    private void SynchronizeVisibleDevices(IReadOnlyList<DeviceSnapshot> visibleSnapshots)
    {
        for (var targetIndex = 0; targetIndex < visibleSnapshots.Count; targetIndex++)
        {
            var row = _deviceRows[visibleSnapshots[targetIndex].Address];
            if (targetIndex < Devices.Count && ReferenceEquals(Devices[targetIndex], row))
            {
                continue;
            }

            var currentIndex = Devices.IndexOf(row);
            if (currentIndex >= 0)
            {
                Devices.Move(currentIndex, targetIndex);
            }
            else
            {
                Devices.Insert(targetIndex, row);
            }
        }

        while (Devices.Count > visibleSnapshots.Count)
        {
            Devices.RemoveAt(Devices.Count - 1);
        }
    }

    private void Select(object? parameter)
    {
        if (parameter is not ulong address)
        {
            return;
        }

        _selectedAddress = address;
        _lastSelectedSnapshot = _currentSnapshots.FirstOrDefault(snapshot => snapshot.Address == address);
        UpdateTracking(_clock.Now);
    }

    private void CopyAddress(object? parameter)
    {
        if (parameter is ulong address)
        {
            _clipboard.SetText(AddressFormatter.Full(address));
        }
    }

    private void Back()
    {
        _selectedAddress = null;
        _lastSelectedSnapshot = null;
        Tracking = null;
        SoundEnabled = false;
        _sound.Update(null, false, TimeSpan.Zero);
    }

    private void UpdateTracking(DateTimeOffset now)
    {
        if (_selectedAddress is not ulong address)
        {
            Tracking = null;
            _sound.Update(null, false, TimeSpan.Zero);
            return;
        }

        var liveSnapshot = _currentSnapshots.FirstOrDefault(snapshot => snapshot.Address == address);
        if (liveSnapshot is not null)
        {
            _lastSelectedSnapshot = liveSnapshot;
        }

        if (_lastSelectedSnapshot is null)
        {
            Tracking = null;
            _sound.Update(null, false, TimeSpan.Zero);
            return;
        }

        var age = NonNegativeAge(now, _lastSelectedSnapshot.LastSeen);
        var isOutOfRange = liveSnapshot is null;
        Tracking = new TrackingViewModel(
            _lastSelectedSnapshot,
            now,
            HideAddresses,
            isOutOfRange,
            SoundEnabled,
            value => SoundEnabled = value);
        _sound.Update(_lastSelectedSnapshot.SmoothedRssi, true, age);
    }

    private static TimeSpan NonNegativeAge(DateTimeOffset now, DateTimeOffset lastSeen) =>
        now <= lastSeen ? TimeSpan.Zero : now - lastSeen;

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MainViewModel));
        }
    }

    private static string StatusFor(ScannerState state) => state switch
    {
        ScannerState.Starting or ScannerState.Scanning => "جارٍ البحث…",
        ScannerState.BluetoothOff => "Bluetooth متوقف",
        ScannerState.AdapterMissing => "لا يوجد محول BLE",
        _ => "توقف المسح",
    };

    private static string GuidanceFor(ScannerState state) => state switch
    {
        ScannerState.BluetoothOff => "شغّل Bluetooth من إعدادات Windows ثم أعد المحاولة.",
        ScannerState.AdapterMissing => "يلزم محول Bluetooth LE لاكتشاف الأجهزة.",
        ScannerState.Aborted => "فشل المسح. أعد المحاولة من زر بدء المسح.",
        _ => string.Empty,
    };
}
