using BleFinder.Core.Audio;
using BleFinder.Core.Devices;
using BleFinder.Core.Models;
using BleFinder.Core.Presentation;
using BleFinder.Core.Scanning;
using BleFinder.Core.Services;
using BleFinder.Core.Time;

namespace BleFinder.Core.Tests.Presentation;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task StartIsIdempotentAndAnObservationAppearsAfterRefresh()
    {
        var fixture = new ViewModelFixture();
        await fixture.ViewModel.StartAsync();
        await fixture.ViewModel.StartAsync();
        fixture.Scanner.Emit(fixture.Observation(1, -55, "Keyboard"));
        fixture.ViewModel.Refresh();

        Assert.Equal(1, fixture.Scanner.StartCount);
        Assert.True(fixture.ViewModel.IsScanning);
        Assert.Equal("Keyboard", Assert.Single(fixture.ViewModel.Devices).Name);
    }

    [Fact]
    public void SearchFiltersByNameAndAddressWithoutChangingRegistry()
    {
        var fixture = new ViewModelFixture();
        fixture.Scanner.Emit(fixture.Observation(0xAABBCCDDEEFF, -55, "Keyboard"));
        fixture.Scanner.Emit(fixture.Observation(2, -60, "Tag"));
        fixture.ViewModel.SearchText = "DD:EE";
        fixture.ViewModel.Refresh();

        Assert.Equal("Keyboard", Assert.Single(fixture.ViewModel.Devices).Name);
    }

    [Fact]
    public void SelectedDeviceBecomesOutOfRangeButRetainsLastSnapshot()
    {
        var fixture = new ViewModelFixture();
        fixture.Scanner.Emit(fixture.Observation(1, -55, "Tag"));
        fixture.ViewModel.Refresh();
        fixture.ViewModel.SelectCommand.Execute(1UL);
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(15);
        fixture.ViewModel.Refresh();

        Assert.NotNull(fixture.ViewModel.Tracking);
        Assert.True(fixture.ViewModel.Tracking!.IsOutOfRange);
        Assert.Equal("Tag", fixture.ViewModel.Tracking.Name);
    }

    [Fact]
    public void CopyAddressUsesTheFullUnmaskedAddress()
    {
        var fixture = new ViewModelFixture();
        fixture.ViewModel.CopyAddressCommand.Execute(0xAABBCCDDEEFFUL);

        Assert.Equal("AA:BB:CC:DD:EE:FF", fixture.Clipboard.LastText);
    }

    [Fact]
    public void AbortedStateKeepsRequiredStatusTextAndExposesErrorCodeSeparately()
    {
        var fixture = new ViewModelFixture();
        fixture.Scanner.Abort("E_PLATFORM");
        fixture.ViewModel.Refresh();

        Assert.Equal("توقف المسح", fixture.ViewModel.StatusText);
        Assert.Equal("E_PLATFORM", fixture.ViewModel.ScannerErrorCode);
    }

    [Fact]
    public void RefreshPreservesCollectionAndRowReferencesWhileUpdatingAndReordering()
    {
        var fixture = new ViewModelFixture();
        fixture.Scanner.Emit(fixture.Observation(1, -80, "Tag"));
        fixture.Scanner.Emit(fixture.Observation(2, -75, "Keyboard"));
        fixture.ViewModel.Refresh();

        var devices = fixture.ViewModel.Devices;
        var tag = Assert.Single(devices, device => device.Address == 1);
        Assert.Equal([2UL, 1UL], devices.Select(device => device.Address));

        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(1);
        fixture.Scanner.Emit(fixture.Observation(1, -50, "Tag"));
        fixture.ViewModel.Refresh();

        Assert.Same(devices, fixture.ViewModel.Devices);
        Assert.Same(tag, fixture.ViewModel.Devices[0]);
        Assert.Equal([1UL, 2UL], fixture.ViewModel.Devices.Select(device => device.Address));
        Assert.Equal("-71 dBm", tag.RssiText);
        Assert.Equal(45, tag.StrengthPercent);
    }

    [Fact]
    public async Task BackgroundScannerStateWaitsForRefreshBeforeMutatingBindableState()
    {
        var fixture = new ViewModelFixture();
        await fixture.ViewModel.StartAsync();
        fixture.ViewModel.Refresh();
        Assert.True(fixture.ViewModel.IsScanning);

        await Task.Run(
            () => fixture.Scanner.Abort("E_BACKGROUND"),
            TestContext.Current.CancellationToken);

        Assert.True(fixture.ViewModel.IsScanning);
        Assert.Equal("جارٍ البحث…", fixture.ViewModel.StatusText);
        Assert.Null(fixture.ViewModel.ScannerErrorCode);

        fixture.ViewModel.Refresh();

        Assert.False(fixture.ViewModel.IsScanning);
        Assert.Equal("توقف المسح", fixture.ViewModel.StatusText);
        Assert.Equal("E_BACKGROUND", fixture.ViewModel.ScannerErrorCode);
    }

    private sealed class ViewModelFixture
    {
        public ViewModelFixture()
        {
            Clock = new FakeClock
            {
                Now = DateTimeOffset.Parse("2026-08-05T12:00:00Z"),
            };
            Scanner = new FakeScanner();
            Sound = new FakeSound();
            Clipboard = new FakeClipboard();
            ViewModel = new MainViewModel(
                new DeviceRegistry(),
                Scanner,
                Sound,
                Clipboard,
                Clock);
        }

        public FakeClock Clock { get; }

        public FakeScanner Scanner { get; }

        public FakeSound Sound { get; }

        public FakeClipboard Clipboard { get; }

        public MainViewModel ViewModel { get; }

        public BleObservation Observation(ulong address, short rssi, string? name = null) =>
            new(address, name, rssi, Clock.Now, [], []);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; }
    }

    private sealed class FakeScanner : IBleScanner
    {
        public event EventHandler<BleObservation>? ObservationReceived;

        public event EventHandler<ScannerStateChanged>? StateChanged;

        public ScannerState State { get; private set; } = ScannerState.Stopped;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            ChangeState(ScannerState.Scanning);
            return Task.CompletedTask;
        }

        public void Stop()
        {
            StopCount++;
            ChangeState(ScannerState.Stopped);
        }

        public void Emit(BleObservation observation) =>
            ObservationReceived?.Invoke(this, observation);

        public void Abort(string errorCode) =>
            ChangeState(ScannerState.Aborted, errorCode);

        public void Dispose() => DisposeCount++;

        private void ChangeState(ScannerState state, string? errorCode = null)
        {
            State = state;
            StateChanged?.Invoke(this, new ScannerStateChanged(state, errorCode));
        }
    }

    private sealed class FakeSound : IProximitySound
    {
        public bool Enabled { get; set; }

        public List<SoundUpdate> Updates { get; } = [];

        public int DisposeCount { get; private set; }

        public void Update(double? rssi, bool selected, TimeSpan age) =>
            Updates.Add(new SoundUpdate(rssi, selected, age));

        public void Dispose() => DisposeCount++;
    }

    private sealed record SoundUpdate(double? Rssi, bool Selected, TimeSpan Age);

    private sealed class FakeClipboard : IClipboardService
    {
        public string? LastText { get; private set; }

        public void SetText(string text) => LastText = text;
    }
}
