namespace BleFinder.Core.Scanning;

public enum ScannerState
{
    Stopped,
    Starting,
    Scanning,
    BluetoothOff,
    AdapterMissing,
    Aborted,
}

public sealed record ScannerStateChanged(ScannerState State, string? ErrorCode = null);
