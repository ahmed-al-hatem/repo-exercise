using BleFinder.Core.Audio;

namespace BleFinder.Core.Tests.Audio;

public sealed class ProximityCadenceTests
{
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
}
