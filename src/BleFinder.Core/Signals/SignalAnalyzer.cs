using BleFinder.Core.Models;

namespace BleFinder.Core.Signals;

public static class SignalAnalyzer
{
    public static int StrengthPercent(double rssi)
    {
        var clampedRssi = Math.Clamp(rssi, -100d, -35d);
        return (int)Math.Round((clampedRssi + 100d) / 65d * 100d, MidpointRounding.AwayFromZero);
    }

    public static ProximityBand Band(double rssi) => rssi switch
    {
        >= -50d => ProximityBand.VeryClose,
        >= -65d => ProximityBand.Close,
        >= -75d => ProximityBand.Nearby,
        >= -88d => ProximityBand.Far,
        _ => ProximityBand.VeryFar,
    };

    public static SignalTrend Trend(IReadOnlyList<SignalSample> samples, DateTimeOffset now)
    {
        var recentValues = new List<double>();
        var olderValues = new List<double>();

        foreach (var sample in samples)
        {
            var age = now - sample.Timestamp;

            if (age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(4))
            {
                recentValues.Add(sample.Rssi);
            }
            else if (age > TimeSpan.FromSeconds(4) && age <= TimeSpan.FromSeconds(12))
            {
                olderValues.Add(sample.Rssi);
            }
        }

        if (recentValues.Count < 2 || olderValues.Count < 2)
        {
            return SignalTrend.Unknown;
        }

        recentValues.Sort();
        olderValues.Sort();

        var change = recentValues[recentValues.Count / 2] - olderValues[olderValues.Count / 2];
        return change switch
        {
            >= 3d => SignalTrend.Closer,
            <= -3d => SignalTrend.Farther,
            _ => SignalTrend.Steady,
        };
    }

    public static string Label(ProximityBand band) => band switch
    {
        ProximityBand.VeryClose => "قريب جدًا",
        ProximityBand.Close => "قريب",
        ProximityBand.Nearby => "بالقرب",
        ProximityBand.Far => "بعيد",
        ProximityBand.VeryFar => "بعيد جدًا",
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, null),
    };
}
