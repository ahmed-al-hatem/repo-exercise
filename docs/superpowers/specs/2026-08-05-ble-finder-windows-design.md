# BLE Finder for Windows — Design

Date: 2026-08-05
Status: Approved for implementation

## Goal

Build an independent Windows 11 x64 desktop application that helps a person locate any nearby Bluetooth Low Energy device by watching how its received signal strength changes while the person moves. The deliverables are the complete source code and a portable, self-contained EXE.

The application recreates the useful behavior of `ben-z/findphone` for Windows without copying or translating its Swift source. The reference repository declares no license, so this project uses a clean implementation based on public platform APIs and the agreed product behavior.

## Scope

The application will:

- scan all nearby BLE advertisements;
- maintain a live, strongest-first device list;
- show the advertised name when present and a stable session label otherwise;
- let the user select one device for proximity tracking;
- display raw and smoothed RSSI, recent trend, last-seen age, and a 60-second history;
- provide optional proximity clicks that become faster as the signal strengthens;
- handle a missing adapter, a disabled radio, scanner failures, and stale devices;
- use an Arabic Windows 11-style graphical interface;
- publish for Windows 11 x64 as a self-contained single-file EXE.

The application will not:

- claim an exact distance in metres or a compass bearing;
- pair with devices, connect to GATT services, or make a device ring;
- run as a background service after the window closes;
- persist discovered devices or their addresses between sessions;
- promise that every device exposes a human-readable name.

## Technology

- C# on .NET 10 LTS
- WPF with the built-in Fluent theme
- Windows Runtime BLE APIs through a Windows-specific target framework
- `Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher`
- MVVM-style presentation boundaries without a third-party MVVM framework
- xUnit for platform-independent unit tests
- GitHub Actions on `windows-latest` for restore, test, and `win-x64` publish

WPF is preferred over WinUI 3 because it can produce a portable self-contained executable without requiring a separately deployed Windows App SDK runtime. A web shell is unnecessary and would add a native Bluetooth bridge and deployment weight.

## Architecture

### `BleScanner`

Owns one `BluetoothLEAdvertisementWatcher`, configured for active scanning. It converts each received event into an immutable observation containing the Bluetooth address, optional local name, RSSI, timestamp, advertisement type, manufacturer IDs, and advertised service UUIDs. RSSI values of `-127` or values outside `-126...-1` are treated as invalid. Scanner callbacks never mutate WPF-bound state directly.

The scanner exposes start, stop, observation, and stopped/error events. Starting and stopping are idempotent. It detaches WinRT event handlers when disposed.

### `DeviceRegistry`

Runs behind a lock and keys devices by the 48-bit Bluetooth address for the current process only. It merges repeated observations and retains:

- the first non-empty advertised name, updated if a later non-empty name differs;
- the latest valid RSSI;
- an exponentially smoothed RSSI using `smoothed = 0.7 * previous + 0.3 * latest`;
- the strongest RSSI seen in the session;
- a bounded timestamped history for the latest 60 seconds;
- the latest manufacturer and service metadata;
- first-seen and last-seen timestamps.

Entries become stale after 5 seconds without an observation and are removed from the registry after 15 seconds. The view model retains an immutable copy of the last selected snapshot so the tracking view can change to an explicit out-of-range state even after registry removal; that copy is discarded when the user returns to the survey list.

The address is only a session identity. BLE private addresses can rotate, so the design does not attempt to correlate a new address with an earlier device.

### `SignalAnalyzer`

Provides pure functions for presentation and tests:

- proximity band from smoothed RSSI;
- signal percentage mapped from `-100...-35 dBm` and clamped to `0...100`;
- trend by comparing the median of the latest 4 seconds with the median of the preceding 8 seconds;
- trend is closer when the difference is at least `+3 dBm`, farther at `-3 dBm`, otherwise steady;
- trend is unknown until each comparison window has at least two valid readings.

Proximity bands are descriptive only:

| Smoothed RSSI | Label |
|---|---|
| `>= -50 dBm` | قريب جدًا |
| `>= -65 dBm` | قريب |
| `>= -75 dBm` | في الجوار |
| `>= -88 dBm` | بعيد |
| `< -88 dBm` | بعيد جدًا أو خلف عائق |

The UI explains that obstacles, antenna orientation, radio power, and the human body make RSSI a coarse relative signal rather than a distance measurement.

### `ProximitySound`

Receives the selected device's current smoothed RSSI on the UI refresh cycle. It schedules a short embedded click asynchronously. The interval is linearly mapped from one second at `-95 dBm` to 70 milliseconds at `-50 dBm`, clamped at both ends. It stays silent while disabled, when no device is selected, or after the selected reading has been stale for 5 seconds.

### View models

`MainViewModel` owns scan state, search text, device rows, selection, and user commands. A dispatcher timer takes a registry snapshot four times per second, applies the search filter, sorts live devices by smoothed RSSI descending, and updates observable UI state.

`DeviceRowViewModel` contains presentation-ready values only. `TrackingViewModel` contains the selected device's headline RSSI, band, trend, history points, freshness, metadata, and sound state. Neither view model calls WinRT directly.

## User Interface

The application uses a single Arabic, right-to-left window with numeric Bluetooth values kept left-to-right.

### Survey view

The header contains the application title, Bluetooth/scanner status, start/stop button, search box, and current device count. The main list is strongest-first. Each row shows:

- advertised name or `جهاز BLE غير مسمّى`;
- shortened address with a copy action for the full address;
- current dBm and a coloured strength bar;
- proximity label and last-seen age;
- optional manufacturer/service summary when present.

Selecting a row switches the content area to the tracking view without opening another window.

### Tracking view

The selected device view shows:

- device name and address;
- a large smoothed RSSI number with `dBm`;
- proximity label and closer/farther/steady arrow;
- a 60-second signal history chart;
- current, session peak, last-seen age, and advertised metadata;
- a sound toggle;
- a back button that returns to the survey list while scanning continues.

If the device becomes stale, the UI retains its last value but marks it stale. At 15 seconds it shows `خارج النطاق` and stops the sound. Returning to the list removes the expired entry.

### Empty and failure states

- Adapter missing: explain that the computer needs a BLE-capable adapter.
- Bluetooth off: explain how to enable Bluetooth and provide a retry button.
- Scanner aborted: show the platform error code and a restart button.
- No advertisements yet: show a non-error waiting state and keep scanning.

## Concurrency and Data Flow

1. WinRT raises an advertisement on a worker callback.
2. `BleScanner` validates and converts it to an observation.
3. `DeviceRegistry` merges it under a short lock and trims the bounded history.
4. The WPF dispatcher timer requests an immutable registry snapshot every 250 ms.
5. `MainViewModel` filters and sorts the snapshot and updates bound view state on the UI thread.
6. The tracking view and sound controller consume the same smoothed reading and freshness state, keeping the number, trend, graph, and click cadence consistent.

No UI collection is mutated from a WinRT callback. Closing the window stops and disposes the watcher and sound scheduler.

## Privacy and Safety

Bluetooth addresses and names are displayed because they are necessary to choose a target, but they are kept in memory only. No telemetry, network calls, account, or analytics are included. A `إخفاء العناوين` toggle masks addresses in the visible interface for screen recording while leaving selection behavior intact.

The application will state that it only receives public BLE advertisements. It cannot locate a radio that is powered off, out of range, not advertising, or hidden by radio shielding.

## Testing

Unit tests use a fake clock and constructed observations. They cover:

- RSSI validation and invalid `-127` handling;
- exponential smoothing and clamping;
- strongest-first sorting and search filtering;
- closer, farther, steady, and unknown trend decisions;
- stale transition at 5 seconds and removal at 15 seconds;
- history pruning at 60 seconds;
- unnamed-device labels and address formatting/masking;
- sound interval mapping and silence rules;
- idempotent application-level scan commands.

A Windows build check compiles the WPF application and runs the unit tests. A publish check creates a self-contained, single-file `win-x64` executable and verifies that exactly one application EXE is present in the distribution folder. Live BLE behavior must finally be smoke-tested on Windows 11 hardware with Bluetooth enabled because a hosted build runner has no usable BLE radio.

## Distribution

The project includes:

- source and solution files;
- Arabic README with build, use, limitations, and troubleshooting instructions;
- a PowerShell publish script;
- a GitHub Actions workflow that uploads the portable EXE artifact;
- license and third-party notice files for this new implementation.

The publish configuration explicitly sets `RuntimeIdentifier=win-x64`, `SelfContained=true`, and `PublishSingleFile=true`. Trimming is disabled for the first release to avoid reflection and WinRT compatibility surprises. Debug symbols are not included in the portable distribution.

## Acceptance Criteria

The design is complete when all of the following are true:

1. On Windows 11 x64 with Bluetooth enabled, nearby BLE advertisements appear and update without pairing.
2. Devices are ordered by smoothed signal strength and searchable by name or address.
3. Selecting a device shows consistent RSSI, trend, history, freshness, and optional sound feedback.
4. Disabled/missing Bluetooth and scanner failure states are understandable and recoverable.
5. No discovered device data survives process exit.
6. Automated unit tests pass on a Windows GitHub Actions runner.
7. The release workflow produces a self-contained single-file x64 EXE plus source documentation.
