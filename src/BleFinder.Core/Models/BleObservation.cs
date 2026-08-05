namespace BleFinder.Core.Models;

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
