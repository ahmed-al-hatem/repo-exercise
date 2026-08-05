# BLE Finder for Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an Arabic Windows 11 x64 desktop application that scans any nearby BLE advertiser, ranks devices by smoothed RSSI, tracks one selected device with trend/history/sound feedback, and ships as source plus a self-contained single-file EXE.

**Architecture:** A platform-neutral core owns observations, signal analysis, the in-memory registry, presentation state, and testable scanner/sound contracts. A thin WPF application implements the Windows Runtime advertisement watcher, dispatcher refresh loop, sound playback, and one-window Arabic UI. GitHub Actions builds and tests on Windows, then publishes the `win-x64` single-file artifact.

**Tech Stack:** C# 14, .NET 10 LTS, WPF, Windows Runtime BLE APIs, xUnit v3 3.2.2, GitHub Actions, PowerShell.

## Global Constraints

- Target Windows 11 x64 only with `net10.0-windows10.0.22621.0` and `RuntimeIdentifier=win-x64`.
- Use `BluetoothLEAdvertisementWatcher` in active mode; do not pair, connect to GATT, or make devices ring.
- Treat RSSI as relative proximity only; never display metres or a compass bearing.
- Keep device names, addresses, observations, and selections in memory only; make no network or telemetry calls.
- Use a single Arabic right-to-left window while rendering addresses, UUIDs, RSSI, and timestamps left-to-right.
- Publish with `SelfContained=true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `PublishTrimmed=false`, and no PDB in the distribution folder.
- Use no third-party runtime packages. xUnit packages are test-only.
- Keep scanner callbacks off WPF-bound collections; update presentation state on the dispatcher every 250 ms.
- Mark devices stale after 5 seconds and remove registry entries after 15 seconds.
- Implement independently; do not copy or translate source from `ben-z/findphone`.

---

## File Structure

```text
BleFinder.sln
Directory.Build.props
.editorconfig
.gitignore
src/
  BleFinder.Core/
    BleFinder.Core.csproj
    Models/BleObservation.cs
    Models/DeviceSnapshot.cs
    Models/SignalSample.cs
    Signals/SignalAnalyzer.cs
    Signals/SignalTrend.cs
    Signals/ProximityBand.cs
    Devices/DeviceRegistry.cs
    Scanning/IBleScanner.cs
    Scanning/ScannerState.cs
    Audio/IProximitySound.cs
    Audio/ProximityCadence.cs
    Services/IClipboardService.cs
    Presentation/AddressFormatter.cs
    Presentation/DeviceRowViewModel.cs
    Presentation/TrackingViewModel.cs
    Presentation/MainViewModel.cs
    Presentation/ObservableObject.cs
    Presentation/RelayCommand.cs
    Presentation/AsyncRelayCommand.cs
    Time/IClock.cs
    Time/SystemClock.cs
  BleFinder.App/
    BleFinder.App.csproj
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Bluetooth/WindowsBleScanner.cs
    Audio/WindowsProximitySound.cs
    Services/WindowsClipboardService.cs
    Controls/SignalHistoryControl.cs
    Resources/click.wav
tests/
  BleFinder.Core.Tests/
    BleFinder.Core.Tests.csproj
    Signals/SignalAnalyzerTests.cs
    Devices/DeviceRegistryTests.cs
    Audio/ProximityCadenceTests.cs
    Presentation/AddressFormatterTests.cs
    Presentation/MainViewModelTests.cs
scripts/publish.ps1
.github/workflows/build.yml
README.md
LICENSE
THIRD-PARTY-NOTICES.md
```

The core project contains every deterministic decision. The app project is limited to Windows APIs, rendering, timing, and audio. This lets the core tests run without Bluetooth hardware and keeps the WinRT boundary small.

### Task 1: Establish the solution and signal model

**Files:**
- Create: `BleFinder.sln`
- Create: `Directory.Build.props`
- Create: `.editorconfig`
- Create: `.gitignore`
- Create: `src/BleFinder.Core/BleFinder.Core.csproj`
- Create: `tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj`
- Create: `src/BleFinder.Core/Models/BleObservation.cs`
- Create: `src/BleFinder.Core/Models/SignalSample.cs`
- Create: `src/BleFinder.Core/Signals/SignalTrend.cs`
- Create: `src/BleFinder.Core/Signals/ProximityBand.cs`
- Create: `src/BleFinder.Core/Signals/SignalAnalyzer.cs`
- Test: `tests/BleFinder.Core.Tests/Signals/SignalAnalyzerTests.cs`

**Interfaces:**
- Produces: `BleObservation.IsValidRssi(short)`, `SignalAnalyzer.StrengthPercent(double)`, `SignalAnalyzer.Band(double)`, `SignalAnalyzer.Trend(IReadOnlyList<SignalSample>, DateTimeOffset)`, and localized `SignalAnalyzer.Label(ProximityBand)`.

- [ ] **Step 1: Create the solution and project shells**

Run on a machine with .NET 10 SDK:

```powershell
dotnet new sln --format sln -n BleFinder
dotnet new classlib -n BleFinder.Core -o src/BleFinder.Core -f net10.0
dotnet new xunit -n BleFinder.Core.Tests -o tests/BleFinder.Core.Tests -f net10.0
dotnet sln BleFinder.sln add src/BleFinder.Core/BleFinder.Core.csproj tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj
dotnet add tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj reference src/BleFinder.Core/BleFinder.Core.csproj
```

Pin the test package to `<PackageReference Include="xunit.v3" Version="3.2.2" />`; keep it only in `BleFinder.Core.Tests.csproj`.

Set `Directory.Build.props` to:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>14</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

Set `.gitignore` to ignore only generated state:

```gitignore
**/bin/
**/obj/
.vs/
artifacts/
*.user
```

- [ ] **Step 2: Write failing signal tests**

Create `SignalAnalyzerTests.cs` with these cases:

```csharp
using BleFinder.Core.Models;
using BleFinder.Core.Signals;

namespace BleFinder.Core.Tests.Signals;

public sealed class SignalAnalyzerTests
{
    [Theory]
    [InlineData(-127, false)]
    [InlineData(0, false)]
    [InlineData(-126, true)]
    [InlineData(-1, true)]
    public void ValidatesRssi(short value, bool expected) =>
        Assert.Equal(expected, BleObservation.IsValidRssi(value));

    [Theory]
    [InlineData(-120, 0)]
    [InlineData(-100, 0)]
    [InlineData(-67.5, 50)]
    [InlineData(-35, 100)]
    [InlineData(-10, 100)]
    public void MapsStrengthToClampedPercent(double rssi, int expected) =>
        Assert.Equal(expected, SignalAnalyzer.StrengthPercent(rssi));

    [Theory]
    [InlineData(-49, ProximityBand.VeryClose)]
    [InlineData(-60, ProximityBand.Close)]
    [InlineData(-70, ProximityBand.Nearby)]
    [InlineData(-80, ProximityBand.Far)]
    [InlineData(-95, ProximityBand.VeryFar)]
    public void MapsBands(double rssi, ProximityBand expected) =>
        Assert.Equal(expected, SignalAnalyzer.Band(rssi));

    [Fact]
    public void FindsCloserTrendFromTwoPopulatedWindows()
    {
        var now = DateTimeOffset.Parse("2026-08-05T12:00:12Z");
        var samples = new[]
        {
            new SignalSample(-78, now.AddSeconds(-10)),
            new SignalSample(-76, now.AddSeconds(-8)),
            new SignalSample(-69, now.AddSeconds(-3)),
            new SignalSample(-68, now.AddSeconds(-1)),
        };

        Assert.Equal(SignalTrend.Closer, SignalAnalyzer.Trend(samples, now));
    }

    [Fact]
    public void ReturnsUnknownUntilBothTrendWindowsHaveTwoSamples() =>
        Assert.Equal(
            SignalTrend.Unknown,
            SignalAnalyzer.Trend(
                [new SignalSample(-60, DateTimeOffset.UnixEpoch)],
                DateTimeOffset.UnixEpoch));
}
```

- [ ] **Step 3: Run the signal tests and confirm the expected failure**

Run:

```powershell
dotnet test tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj --filter SignalAnalyzerTests
```

Expected: compilation fails because `BleObservation`, `SignalSample`, `SignalAnalyzer`, `SignalTrend`, and `ProximityBand` do not exist.

- [ ] **Step 4: Add the minimal signal implementation**

Use these public shapes:

```csharp
public sealed record BleObservation(
    ulong Address,
    string? LocalName,
    short Rssi,
    DateTimeOffset Timestamp,
    IReadOnlyList<ushort> ManufacturerIds,
    IReadOnlyList<Guid> ServiceUuids)
{
    public static bool IsValidRssi(short value) => value is >= -126 and <= -1;
}

public readonly record struct SignalSample(double Rssi, DateTimeOffset Timestamp);

public enum SignalTrend { Unknown, Closer, Farther, Steady }
public enum ProximityBand { VeryClose, Close, Nearby, Far, VeryFar }
```

Implement `SignalAnalyzer` with `-100...-35` percent mapping, band floors `-50/-65/-75/-88`, Arabic labels, median trend windows `0...4` and `4...12` seconds old, and a 3 dBm threshold. Sort each window before choosing `values[values.Count / 2]`.

- [ ] **Step 5: Run all core tests**

Run: `dotnet test tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj`

Expected: all tests pass with zero warnings.

- [ ] **Step 6: Commit the signal foundation**

```bash
git add BleFinder.sln Directory.Build.props .editorconfig .gitignore src/BleFinder.Core tests/BleFinder.Core.Tests
git commit -m "feat: add BLE signal model and analysis"
```

### Task 2: Build the in-memory device registry

**Files:**
- Create: `src/BleFinder.Core/Models/DeviceSnapshot.cs`
- Create: `src/BleFinder.Core/Devices/DeviceRegistry.cs`
- Test: `tests/BleFinder.Core.Tests/Devices/DeviceRegistryTests.cs`

**Interfaces:**
- Consumes: `BleObservation`, `SignalSample`.
- Produces: `DeviceRegistry.Record(BleObservation)`, `DeviceRegistry.Snapshot(DateTimeOffset)`, `DeviceRegistry.Clear()`, and immutable `DeviceSnapshot` values.

- [ ] **Step 1: Write failing registry tests**

Add tests using a helper `Observation(ulong address, short rssi, DateTimeOffset at, string? name = null)` and assert:

```csharp
[Fact]
public void MergesAndSmoothsRepeatedObservations()
{
    var registry = new DeviceRegistry();
    var now = DateTimeOffset.Parse("2026-08-05T12:00:00Z");
    registry.Record(Observation(0xAABBCCDDEEFF, -80, now, "Tag"));
    registry.Record(Observation(0xAABBCCDDEEFF, -60, now.AddSeconds(1)));

    var item = Assert.Single(registry.Snapshot(now.AddSeconds(1)));
    Assert.Equal(-74, item.SmoothedRssi, precision: 6);
    Assert.Equal(-60, item.LatestRssi);
    Assert.Equal(-60, item.PeakRssi);
    Assert.Equal("Tag", item.LocalName);
    Assert.Equal(2, item.History.Count);
}

[Fact]
public void MarksAtFiveSecondsAndRemovesAtFifteenSeconds()
{
    var registry = new DeviceRegistry();
    var now = DateTimeOffset.Parse("2026-08-05T12:00:00Z");
    registry.Record(Observation(1, -70, now));

    Assert.False(Assert.Single(registry.Snapshot(now.AddSeconds(4.999))).IsStale);
    Assert.True(Assert.Single(registry.Snapshot(now.AddSeconds(5))).IsStale);
    Assert.Empty(registry.Snapshot(now.AddSeconds(15)));
}

[Fact]
public void RejectsInvalidRssiAndPrunesHistoryOlderThanSixtySeconds()
{
    var registry = new DeviceRegistry();
    var now = DateTimeOffset.Parse("2026-08-05T12:00:00Z");
    registry.Record(Observation(1, -60, now.AddSeconds(-61)));
    registry.Record(Observation(1, -127, now));
    registry.Record(Observation(1, -55, now));

    var item = Assert.Single(registry.Snapshot(now));
    Assert.Single(item.History);
    Assert.Equal(-55, item.LatestRssi);
}
```

- [ ] **Step 2: Run the registry tests and confirm failure**

Run: `dotnet test tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj --filter DeviceRegistryTests`

Expected: compilation fails because the registry types do not exist.

- [ ] **Step 3: Implement a locked registry with immutable snapshots**

Define:

```csharp
public sealed record DeviceSnapshot(
    ulong Address,
    string? LocalName,
    short LatestRssi,
    double SmoothedRssi,
    short PeakRssi,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    bool IsStale,
    IReadOnlyList<SignalSample> History,
    IReadOnlyList<ushort> ManufacturerIds,
    IReadOnlyList<Guid> ServiceUuids);
```

Use a private mutable entry per address. Ignore invalid observations before locking. Merge names only when non-whitespace, use `0.7 * old + 0.3 * new`, replace manufacturer/service metadata when the incoming corresponding list is non-empty, prune history with `sample.Timestamp >= now - 60s`, remove entries when `now - LastSeen >= 15s`, and return snapshots ordered by `SmoothedRssi` descending.

- [ ] **Step 4: Run the registry and full core tests**

Run: `dotnet test tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj`

Expected: all tests pass.

- [ ] **Step 5: Commit the registry**

```bash
git add src/BleFinder.Core/Models src/BleFinder.Core/Devices tests/BleFinder.Core.Tests/Devices
git commit -m "feat: track nearby BLE devices in memory"
```

### Task 3: Add formatting, search, and sound cadence

**Files:**
- Create: `src/BleFinder.Core/Presentation/AddressFormatter.cs`
- Create: `src/BleFinder.Core/Audio/ProximityCadence.cs`
- Test: `tests/BleFinder.Core.Tests/Presentation/AddressFormatterTests.cs`
- Test: `tests/BleFinder.Core.Tests/Audio/ProximityCadenceTests.cs`

**Interfaces:**
- Produces: `AddressFormatter.Full(ulong)`, `AddressFormatter.Short(ulong)`, `AddressFormatter.Masked`, `AddressFormatter.Matches(ulong, string?, string)`, `ProximityCadence.Interval(double)`, and `ProximityCadence.ShouldPlay(bool, bool, TimeSpan)`.

- [ ] **Step 1: Write failing formatter and cadence tests**

```csharp
[Fact]
public void FormatsAndMasksBluetoothAddress()
{
    Assert.Equal("AA:BB:CC:DD:EE:FF", AddressFormatter.Full(0xAABBCCDDEEFF));
    Assert.Equal("AA:BB:…:EE:FF", AddressFormatter.Short(0xAABBCCDDEEFF));
    Assert.Equal("••:••:••:••:••:••", AddressFormatter.Masked);
}

[Theory]
[InlineData("tag", true)]
[InlineData("DD:EE", true)]
[InlineData("sensor", false)]
public void MatchesNameOrAddress(string query, bool expected) =>
    Assert.Equal(expected, AddressFormatter.Matches(0xAABBCCDDEEFF, "My Tag", query));

[Theory]
[InlineData(-100, 1000)]
[InlineData(-95, 1000)]
[InlineData(-50, 70)]
[InlineData(-30, 70)]
public void MapsCadence(double rssi, double milliseconds) =>
    Assert.Equal(milliseconds, ProximityCadence.Interval(rssi).TotalMilliseconds, 3);

[Theory]
[InlineData(true, true, 4.9, true)]
[InlineData(false, true, 1, false)]
[InlineData(true, false, 1, false)]
[InlineData(true, true, 5, false)]
public void AppliesSilenceRules(bool enabled, bool selected, double age, bool expected) =>
    Assert.Equal(expected, ProximityCadence.ShouldPlay(enabled, selected, TimeSpan.FromSeconds(age)));
```

- [ ] **Step 2: Run tests and confirm failure**

Run: `dotnet test tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj --filter "AddressFormatterTests|ProximityCadenceTests"`

Expected: compilation fails for the missing classes.

- [ ] **Step 3: Implement exact formatting and cadence rules**

Format the address as a 12-digit uppercase hexadecimal string split into six byte pairs. Search must trim the query, match the name with `OrdinalIgnoreCase`, and match both colonized and colon-free addresses. Cadence must linearly map the clamped range `-95...-50 dBm` to `1000...70 ms`.

- [ ] **Step 4: Run all core tests and commit**

Run: `dotnet test tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj`

Then:

```bash
git add src/BleFinder.Core/Presentation src/BleFinder.Core/Audio tests/BleFinder.Core.Tests/Presentation tests/BleFinder.Core.Tests/Audio
git commit -m "feat: add device formatting and sound cadence"
```

### Task 4: Implement testable application state

**Files:**
- Create: `src/BleFinder.Core/Scanning/IBleScanner.cs`
- Create: `src/BleFinder.Core/Scanning/ScannerState.cs`
- Create: `src/BleFinder.Core/Audio/IProximitySound.cs`
- Create: `src/BleFinder.Core/Services/IClipboardService.cs`
- Create: `src/BleFinder.Core/Time/IClock.cs`
- Create: `src/BleFinder.Core/Time/SystemClock.cs`
- Create: `src/BleFinder.Core/Presentation/ObservableObject.cs`
- Create: `src/BleFinder.Core/Presentation/RelayCommand.cs`
- Create: `src/BleFinder.Core/Presentation/AsyncRelayCommand.cs`
- Create: `src/BleFinder.Core/Presentation/DeviceRowViewModel.cs`
- Create: `src/BleFinder.Core/Presentation/TrackingViewModel.cs`
- Create: `src/BleFinder.Core/Presentation/MainViewModel.cs`
- Test: `tests/BleFinder.Core.Tests/Presentation/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `DeviceRegistry`, `SignalAnalyzer`, `AddressFormatter`, `ProximityCadence`.
- Produces: `IBleScanner`, `IProximitySound`, `IClipboardService`, `MainViewModel.StartAsync()`, `StartCommand`, `StopCommand`, `SelectCommand`, `CopyAddressCommand`, `BackCommand`, `Refresh()`, and bindable survey/tracking properties.

- [ ] **Step 1: Write fakes and failing view-model tests**

Use a `FakeClock` with mutable `Now`, `FakeScanner` that counts `StartAsync()`/`Stop()` calls and raises observations, `FakeSound` that records `Update(double?, bool, TimeSpan)`, and `FakeClipboard` with a `LastText` property. Add tests that prove:

```csharp
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
```

- [ ] **Step 2: Run the view-model tests and confirm failure**

Run: `dotnet test tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj --filter MainViewModelTests`

Expected: compilation fails for the missing contracts and view models.

- [ ] **Step 3: Implement scanner, sound, clock, and command contracts**

Use these exact contracts:

```csharp
public enum ScannerState { Stopped, Starting, Scanning, BluetoothOff, AdapterMissing, Aborted }
public sealed record ScannerStateChanged(ScannerState State, string? ErrorCode = null);

public interface IBleScanner : IDisposable
{
    event EventHandler<BleObservation>? ObservationReceived;
    event EventHandler<ScannerStateChanged>? StateChanged;
    ScannerState State { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    void Stop();
}

public interface IProximitySound : IDisposable
{
    bool Enabled { get; set; }
    void Update(double? rssi, bool selected, TimeSpan age);
}

public interface IClock { DateTimeOffset Now { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset Now => DateTimeOffset.Now; }
public interface IClipboardService { void SetText(string text); }
```

- [ ] **Step 4: Implement presentation state and commands**

`MainViewModel` subscribes to scanner observations and records them, but only `Refresh()` replaces the bindable `IReadOnlyList<DeviceRowViewModel> Devices`. It keeps `ulong? selectedAddress` and `DeviceSnapshot? lastSelectedSnapshot`. `StartAsync()` returns immediately when already starting/scanning and otherwise awaits `IBleScanner.StartAsync()`. `StartCommand` is an `AsyncRelayCommand`; `SelectCommand` and `CopyAddressCommand` accept boxed `ulong` values; the copy command writes `AddressFormatter.Full(address)` through `IClipboardService`; `BackCommand` clears tracking and disables sound. `HideAddresses` switches both survey and tracking address text to `AddressFormatter.Masked`. `MainViewModel.Dispose()` unsubscribes its scanner handlers, stops scanning, and disposes the scanner and sound; it is safe to call twice.

Use exact fallback strings: `جهاز BLE غير مسمّى`, `جارٍ البحث…`, `Bluetooth متوقف`, `لا يوجد محول BLE`, `توقف المسح`, and `خارج النطاق`.

- [ ] **Step 5: Run all tests and commit**

Run: `dotnet test tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj`

Then:

```bash
git add src/BleFinder.Core tests/BleFinder.Core.Tests/Presentation/MainViewModelTests.cs
git commit -m "feat: add testable scan and tracking state"
```

### Task 5: Add the Windows BLE and sound adapters

**Files:**
- Create: `src/BleFinder.App/BleFinder.App.csproj`
- Create: `src/BleFinder.App/App.xaml`
- Create: `src/BleFinder.App/App.xaml.cs`
- Create: `src/BleFinder.App/Bluetooth/WindowsBleScanner.cs`
- Create: `src/BleFinder.App/Audio/WindowsProximitySound.cs`
- Create: `src/BleFinder.App/Services/WindowsClipboardService.cs`
- Create: `src/BleFinder.App/Resources/click.wav`
- Modify: `BleFinder.sln`

**Interfaces:**
- Consumes: `IBleScanner`, `ScannerStateChanged`, `BleObservation`, `IProximitySound`, `ProximityCadence`.
- Produces: Windows implementations registered at WPF startup.

- [ ] **Step 1: Create the WPF project and force a contract compile failure**

Run:

```powershell
dotnet new wpf -n BleFinder.App -o src/BleFinder.App -f net10.0
dotnet sln BleFinder.sln add src/BleFinder.App/BleFinder.App.csproj
dotnet add src/BleFinder.App/BleFinder.App.csproj reference src/BleFinder.Core/BleFinder.Core.csproj
```

Set the application project properties to:

```xml
<TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
<UseWPF>true</UseWPF>
<OutputType>WinExe</OutputType>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<PublishTrimmed>false</PublishTrimmed>
<DebugType>embedded</DebugType>
```

Add empty `WindowsBleScanner : IBleScanner` and `WindowsProximitySound : IProximitySound` declarations, then run `dotnet build src/BleFinder.App/BleFinder.App.csproj`. Expected: compile errors identify every unimplemented interface member.

- [ ] **Step 2: Implement `WindowsBleScanner`**

In `StartAsync`, await `BluetoothAdapter.GetDefaultAsync()`. Emit `AdapterMissing` when it returns null; otherwise await `adapter.GetRadioAsync()` and emit `BluetoothOff` when the radio is off. Construct `BluetoothLEAdvertisementWatcher` with `ScanningMode = BluetoothLEScanningMode.Active` and `AllowExtendedAdvertisements = true`. On `Received`, copy all WinRT data immediately into CLR values:

```csharp
var observation = new BleObservation(
    args.BluetoothAddress,
    args.Advertisement.LocalName,
    args.RawSignalStrengthInDBm,
    args.Timestamp,
    args.Advertisement.ManufacturerData.Select(x => x.CompanyId).ToArray(),
    args.Advertisement.ServiceUuids.ToArray());
```

Map watcher statuses and stop errors to `ScannerState`. Guard start/stop with a lock so calls are idempotent. Detach `Received` and `Stopped` before disposal. Do not create `BluetoothLEDevice` objects or GATT connections.

- [ ] **Step 3: Implement self-paced Windows sound**

Embed a short mono PCM click WAV as a WPF resource. `WindowsProximitySound.Update` stores the latest inputs under a lock. A `System.Threading.Timer` schedules its next one-shot interval using `ProximityCadence.Interval`; call `SoundPlayer.Play()` only when `ShouldPlay` is true. Dispose the timer and sound player on application exit.

Implement `WindowsClipboardService.SetText` with WPF `Clipboard.SetText(text)`; commands invoke it on the UI thread.

- [ ] **Step 4: Compile the app and run core tests**

Run:

```powershell
dotnet build src/BleFinder.App/BleFinder.App.csproj -c Release
dotnet test tests/BleFinder.Core.Tests/BleFinder.Core.Tests.csproj -c Release
```

Expected: both commands succeed with zero warnings.

- [ ] **Step 5: Commit the Windows adapters**

```bash
git add BleFinder.sln src/BleFinder.App
git commit -m "feat: scan BLE advertisements on Windows"
```

### Task 6: Build the Arabic one-window WPF interface

**Files:**
- Create: `src/BleFinder.App/MainWindow.xaml`
- Create: `src/BleFinder.App/MainWindow.xaml.cs`
- Create: `src/BleFinder.App/Controls/SignalHistoryControl.cs`
- Modify: `src/BleFinder.App/App.xaml`
- Modify: `src/BleFinder.App/App.xaml.cs`

**Interfaces:**
- Consumes: all bindable properties and commands on `MainViewModel`, plus tracking history samples.
- Produces: survey and tracking views, system-theme colours, accessible labels, and a 250 ms dispatcher refresh.

- [ ] **Step 1: Add a compile-time UI shell with all required bindings**

Set `ThemeMode="System"` on `App.xaml`. Set the window to `Width="1100"`, `Height="720"`, `MinWidth="860"`, `MinHeight="580"`, `FlowDirection="RightToLeft"`, and `Language="ar"`. Add bindings for `StatusText`, `StartCommand`, `StopCommand`, `SearchText`, `DeviceCountText`, `HideAddresses`, `Devices`, `SelectCommand`, `CopyAddressCommand`, `Tracking`, `BackCommand`, and sound enabled state. Run the build before the controls and final properties exist.

Expected: XAML or C# compilation fails on the first missing type/property, proving the binding shell is included in the build.

- [ ] **Step 2: Implement the survey view**

Use a header grid with title `محدد أجهزة BLE`, live status dot/text, scan toggle, search box labelled `ابحث بالاسم أو العنوان`, and `إخفاء العناوين`. Render device rows as buttons so keyboard activation works. Each row binds name, address, RSSI text, strength percent, band label, freshness, and last-seen text, with a separate `نسخ العنوان` button bound to `CopyAddressCommand`. Show `لم يظهر أي جهاز بعد — اترك المسح يعمل لبضع ثوانٍ` when the collection is empty.

- [ ] **Step 3: Implement the tracking view and history control**

The tracking view contains a back button, device identity, large centred RSSI, band label, trend arrow/text, freshness, peak, metadata, sound toggle, and warning:

```text
الإشارة تقديرية وتتأثر بالجدران والمعادن واتجاه الجهاز. راقب الاتجاه أثناء الحركة، لا قراءة واحدة.
```

`SignalHistoryControl : FrameworkElement` exposes a dependency property `IReadOnlyList<SignalSample> Samples`. In `OnRender`, draw a rounded dark plot area, five horizontal guides, and a cyan polyline mapping 60 seconds to width and `-100...-35 dBm` to height. Render nothing when fewer than two points exist and call `InvalidateVisual()` from the property callback.

- [ ] **Step 4: Wire composition, refresh, and shutdown**

In `App.OnStartup`, create `DeviceRegistry`, `WindowsBleScanner`, `WindowsProximitySound`, `WindowsClipboardService`, `SystemClock`, and `MainViewModel`, assign it to `MainWindow.DataContext`, show the window, and await `MainViewModel.StartAsync()`. In `MainWindow`, use a `DispatcherTimer` with `Interval = TimeSpan.FromMilliseconds(250)` to call `Refresh()`. On close, stop the timer and dispose only the view model; it owns and disposes the scanner and sound.

- [ ] **Step 5: Build, run the tests, and commit**

Run:

```powershell
dotnet build BleFinder.sln -c Release
dotnet test BleFinder.sln -c Release --no-build
```

Expected: build and tests pass with no warnings.

Then:

```bash
git add src/BleFinder.App
git commit -m "feat: add Arabic BLE finder interface"
```

### Task 7: Add publication, CI, and user documentation

**Files:**
- Create: `scripts/publish.ps1`
- Create: `.github/workflows/build.yml`
- Create: `README.md`
- Create: `LICENSE`
- Create: `THIRD-PARTY-NOTICES.md`

**Interfaces:**
- Consumes: `BleFinder.App.csproj` release configuration.
- Produces: `artifacts/BleFinder-win-x64/BleFinder.exe` and GitHub artifact `BleFinder-win-x64`.

- [ ] **Step 1: Write the publish script with explicit artifact assertions**

`scripts/publish.ps1` must stop on errors, remove only the exact `artifacts/BleFinder-win-x64` directory, run:

```powershell
dotnet publish src/BleFinder.App/BleFinder.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false `
  -o artifacts/BleFinder-win-x64
```

Then assert that `BleFinder.App.exe` exists, rename it to `BleFinder.exe`, fail if any `.dll` or `.pdb` remains, and print the SHA-256 hash.

- [ ] **Step 2: Run the script and confirm the portable artifact shape**

Run: `pwsh -File scripts/publish.ps1`

Expected: exit 0, exactly one EXE plus optional text documentation in `artifacts/BleFinder-win-x64`, no DLL or PDB, and a printed SHA-256.

- [ ] **Step 3: Add the Windows GitHub Actions workflow**

Use `windows-latest`, `actions/checkout@v7`, and `actions/setup-dotnet@v6` with `dotnet-version: 10.0.x`. The workflow runs restore, `dotnet test BleFinder.sln -c Release`, the publish script, and `actions/upload-artifact@v7` with `name: BleFinder-win-x64` and `path: artifacts/BleFinder-win-x64/**`. Trigger on pushes, pull requests, and `v*` tags.

- [ ] **Step 4: Write exact Arabic usage and troubleshooting documentation**

README sections must cover:

- Windows 11 x64 and BLE adapter requirements;
- running the portable EXE and dismissing SmartScreen for an unsigned local build;
- scan, search, selection, movement, trend, sound, and address masking;
- why names can be absent and private addresses can rotate;
- RSSI limitations and the `-50/-65/-75/-88` bands;
- Bluetooth-off, no-adapter, no-advertisement, and stale-device troubleshooting;
- source build and publish commands using .NET 10 SDK;
- privacy statement: in-memory only, no network, no telemetry;
- hardware smoke-test checklist.

Use the MIT license for this new source and list xUnit as test-only in `THIRD-PARTY-NOTICES.md`.

- [ ] **Step 5: Run workflow-equivalent commands and commit**

Run:

```powershell
dotnet restore BleFinder.sln
dotnet test BleFinder.sln -c Release
pwsh -File scripts/publish.ps1
```

Expected: every command exits 0.

Then:

```bash
git add scripts .github README.md LICENSE THIRD-PARTY-NOTICES.md
git commit -m "build: publish portable Windows BLE finder"
```

### Task 8: Final verification and delivery

**Files:**
- Modify only files implicated by verification failures.
- Produce: `artifacts/BleFinder-win-x64/BleFinder.exe`
- Produce: `artifacts/BleFinder-source.zip`

**Interfaces:**
- Consumes: the complete source tree and Windows build workflow.
- Produces: verified source archive, EXE, hash, and documented hardware-only checks.

- [ ] **Step 1: Run clean automated verification on Windows**

```powershell
dotnet clean BleFinder.sln -c Release
dotnet restore BleFinder.sln
dotnet test BleFinder.sln -c Release --no-restore
pwsh -File scripts/publish.ps1
```

Expected: restore succeeds, all tests pass, publish succeeds, and the distribution contains one EXE with no DLL or PDB.

- [ ] **Step 2: Perform a non-radio launch smoke test**

Start `artifacts/BleFinder-win-x64/BleFinder.exe`, confirm the Arabic window opens, resizing preserves both views, keyboard tab order reaches every command, and closing the window exits the process without an unhandled exception. With Bluetooth unavailable, confirm the visible failure state offers retry instead of crashing.

- [ ] **Step 3: Record the hardware-only validation boundary**

In the release notes state that these checks require the user's Windows 11 BLE hardware: live advertisements, Bluetooth-off recovery, address rotation behavior, movement trend, and audible cadence. Do not claim those checks passed on a hosted runner.

- [ ] **Step 4: Create a reproducible source archive**

From the repository root run:

```powershell
git archive --format=zip --output=artifacts/BleFinder-source.zip HEAD
Get-FileHash artifacts/BleFinder-source.zip -Algorithm SHA256
Get-FileHash artifacts/BleFinder-win-x64/BleFinder.exe -Algorithm SHA256
```

Expected: both files exist and both hashes are printed.

- [ ] **Step 5: Commit any verification-only corrections**

If verification changed source, stage only those exact files and commit:

```bash
git commit -m "fix: address final Windows verification findings"
```

If verification made no changes, do not create an empty commit.
