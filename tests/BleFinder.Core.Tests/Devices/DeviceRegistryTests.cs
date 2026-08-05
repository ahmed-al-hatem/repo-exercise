using BleFinder.Core.Devices;
using BleFinder.Core.Models;

namespace BleFinder.Core.Tests.Devices;

public sealed class DeviceRegistryTests
{
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

    private static BleObservation Observation(ulong address, short rssi, DateTimeOffset at, string? name = null) =>
        new(address, name, rssi, at, [], []);
}
