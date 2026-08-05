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
