namespace BleFinder.Core.Audio;

public static class ProximityCadence
{
    public static TimeSpan Interval(double rssi)
    {
        var clampedRssi = Math.Clamp(rssi, -95d, -50d);
        var progress = (clampedRssi + 95d) / 45d;
        return TimeSpan.FromMilliseconds(1000d - (progress * 930d));
    }

    public static bool ShouldPlay(bool enabled, bool selected, TimeSpan age) =>
        enabled && selected && age < TimeSpan.FromSeconds(5);
}
