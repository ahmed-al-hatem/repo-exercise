using System.Globalization;
using BleFinder.Core.Models;
using BleFinder.Core.Signals;

namespace BleFinder.Core.Presentation;

public sealed class TrackingViewModel : ObservableObject
{
    private readonly Action<bool>? _soundEnabledChanged;
    private bool _hideAddress;
    private bool _soundEnabled;

    public TrackingViewModel(
        DeviceSnapshot snapshot,
        DateTimeOffset now,
        bool hideAddress,
        bool isOutOfRange,
        bool soundEnabled,
        Action<bool>? soundEnabledChanged = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _hideAddress = hideAddress;
        _soundEnabled = soundEnabled;
        _soundEnabledChanged = soundEnabledChanged;

        var age = NonNegativeAge(now, snapshot.LastSeen);
        var trend = SignalAnalyzer.Trend(snapshot.History, now);
        Address = snapshot.Address;
        Name = string.IsNullOrWhiteSpace(snapshot.LocalName) ? "جهاز BLE غير مسمّى" : snapshot.LocalName;
        Rssi = snapshot.SmoothedRssi;
        RssiText = string.Create(CultureInfo.InvariantCulture, $"{snapshot.SmoothedRssi:0} dBm");
        BandLabel = SignalAnalyzer.Label(SignalAnalyzer.Band(snapshot.SmoothedRssi));
        Trend = trend;
        TrendArrow = TrendArrowFor(trend);
        TrendText = TrendTextFor(trend);
        History = snapshot.History;
        IsStale = snapshot.IsStale;
        IsOutOfRange = isOutOfRange;
        Freshness = isOutOfRange ? "خارج النطاق" : snapshot.IsStale ? "الإشارة قديمة" : "مباشر";
        PeakText = string.Create(CultureInfo.InvariantCulture, $"{snapshot.PeakRssi} dBm");
        LastSeenText = string.Create(CultureInfo.InvariantCulture, $"آخر ظهور منذ {age.TotalSeconds:0.#} ث");
        ManufacturerText = MetadataFormatter.Manufacturers(snapshot.ManufacturerIds);
        ServicesText = MetadataFormatter.Services(snapshot.ServiceUuids);
    }

    public ulong Address { get; }

    public string Name { get; }

    public string AddressText => _hideAddress ? AddressFormatter.Masked : AddressFormatter.Full(Address);

    public double Rssi { get; }

    public string RssiText { get; }

    public string BandLabel { get; }

    public SignalTrend Trend { get; }

    public string TrendArrow { get; }

    public string TrendText { get; }

    public IReadOnlyList<SignalSample> History { get; }

    public bool IsStale { get; }

    public bool IsOutOfRange { get; }

    public string Freshness { get; }

    public string PeakText { get; }

    public string LastSeenText { get; }

    public string ManufacturerText { get; }

    public string ServicesText { get; }

    public bool SoundEnabled
    {
        get => _soundEnabled;
        set
        {
            if (!SetProperty(ref _soundEnabled, value))
            {
                return;
            }

            _soundEnabledChanged?.Invoke(value);
        }
    }

    internal void SetAddressHidden(bool hidden)
    {
        if (_hideAddress == hidden)
        {
            return;
        }

        _hideAddress = hidden;
        OnPropertyChanged(nameof(AddressText));
    }

    private static TimeSpan NonNegativeAge(DateTimeOffset now, DateTimeOffset lastSeen) =>
        now <= lastSeen ? TimeSpan.Zero : now - lastSeen;

    private static string TrendArrowFor(SignalTrend trend) => trend switch
    {
        SignalTrend.Closer => "↑",
        SignalTrend.Farther => "↓",
        SignalTrend.Steady => "→",
        _ => "—",
    };

    private static string TrendTextFor(SignalTrend trend) => trend switch
    {
        SignalTrend.Closer => "أقرب",
        SignalTrend.Farther => "أبعد",
        SignalTrend.Steady => "ثابت",
        _ => "غير كافٍ",
    };
}

internal static class MetadataFormatter
{
    public static string Summary(DeviceSnapshot snapshot)
    {
        var manufacturers = Manufacturers(snapshot.ManufacturerIds);
        var services = Services(snapshot.ServiceUuids);
        return string.Join(" • ", new[] { manufacturers, services }.Where(value => value.Length > 0));
    }

    public static string Manufacturers(IReadOnlyList<ushort> manufacturerIds) =>
        manufacturerIds.Count == 0
            ? string.Empty
            : $"الشركات: {string.Join(", ", manufacturerIds.Select(id => $"0x{id:X4}"))}";

    public static string Services(IReadOnlyList<Guid> serviceUuids) =>
        serviceUuids.Count == 0
            ? string.Empty
            : $"الخدمات: {string.Join(", ", serviceUuids)}";
}
