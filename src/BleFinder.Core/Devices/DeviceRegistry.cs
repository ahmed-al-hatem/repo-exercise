using BleFinder.Core.Models;

namespace BleFinder.Core.Devices;

public sealed class DeviceRegistry
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RemoveAfter = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HistoryLifetime = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Dictionary<ulong, DeviceEntry> _entries = [];

    public void Record(BleObservation observation)
    {
        if (!BleObservation.IsValidRssi(observation.Rssi))
        {
            return;
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(observation.Address, out var entry))
            {
                _entries.Add(observation.Address, new DeviceEntry(observation));
                return;
            }

            entry.Record(observation);
        }
    }

    public IReadOnlyList<DeviceSnapshot> Snapshot(DateTimeOffset now)
    {
        lock (_gate)
        {
            var snapshots = new List<DeviceSnapshot>();

            foreach (var (address, entry) in _entries.ToArray())
            {
                if (now - entry.LastSeen >= RemoveAfter)
                {
                    _entries.Remove(address);
                    continue;
                }

                entry.PruneHistory(now);
                snapshots.Add(entry.Snapshot(now - entry.LastSeen >= StaleAfter));
            }

            return snapshots.OrderByDescending(snapshot => snapshot.SmoothedRssi).ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private sealed class DeviceEntry
    {
        private readonly List<SignalSample> _history;

        public DeviceEntry(BleObservation observation)
        {
            Address = observation.Address;
            LocalName = string.IsNullOrWhiteSpace(observation.LocalName) ? null : observation.LocalName;
            LatestRssi = observation.Rssi;
            SmoothedRssi = observation.Rssi;
            PeakRssi = observation.Rssi;
            FirstSeen = observation.Timestamp;
            LastSeen = observation.Timestamp;
            _history = [new SignalSample(observation.Rssi, observation.Timestamp)];
            ManufacturerIds = observation.ManufacturerIds.ToArray();
            ServiceUuids = observation.ServiceUuids.ToArray();
        }

        public ulong Address { get; }

        public string? LocalName { get; private set; }

        public short LatestRssi { get; private set; }

        public double SmoothedRssi { get; private set; }

        public short PeakRssi { get; private set; }

        public DateTimeOffset FirstSeen { get; }

        public DateTimeOffset LastSeen { get; private set; }

        public IReadOnlyList<ushort> ManufacturerIds { get; private set; }

        public IReadOnlyList<Guid> ServiceUuids { get; private set; }

        public void Record(BleObservation observation)
        {
            if (!string.IsNullOrWhiteSpace(observation.LocalName))
            {
                LocalName = observation.LocalName;
            }

            LatestRssi = observation.Rssi;
            SmoothedRssi = (0.7d * SmoothedRssi) + (0.3d * observation.Rssi);
            PeakRssi = Math.Max(PeakRssi, observation.Rssi);
            LastSeen = observation.Timestamp;
            _history.Add(new SignalSample(observation.Rssi, observation.Timestamp));
            PruneHistory(observation.Timestamp);

            if (observation.ManufacturerIds.Count > 0)
            {
                ManufacturerIds = observation.ManufacturerIds.ToArray();
            }

            if (observation.ServiceUuids.Count > 0)
            {
                ServiceUuids = observation.ServiceUuids.ToArray();
            }
        }

        public void PruneHistory(DateTimeOffset now) =>
            _history.RemoveAll(sample => sample.Timestamp < now - HistoryLifetime);

        public DeviceSnapshot Snapshot(bool isStale) =>
            new(
                Address,
                LocalName,
                LatestRssi,
                SmoothedRssi,
                PeakRssi,
                FirstSeen,
                LastSeen,
                isStale,
                Array.AsReadOnly(_history.ToArray()),
                Array.AsReadOnly(ManufacturerIds.ToArray()),
                Array.AsReadOnly(ServiceUuids.ToArray()));
    }
}
