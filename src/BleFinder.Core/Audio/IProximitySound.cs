namespace BleFinder.Core.Audio;

public interface IProximitySound : IDisposable
{
    bool Enabled { get; set; }

    void Update(double? rssi, bool selected, TimeSpan age);
}
