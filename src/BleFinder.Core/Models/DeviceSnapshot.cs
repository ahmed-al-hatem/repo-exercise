namespace BleFinder.Core.Models;

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
