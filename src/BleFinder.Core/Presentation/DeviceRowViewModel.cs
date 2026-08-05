using System.Globalization;
using BleFinder.Core.Models;
using BleFinder.Core.Signals;

namespace BleFinder.Core.Presentation;

public sealed class DeviceRowViewModel : ObservableObject
{
    private bool _hideAddress;

    public DeviceRowViewModel(DeviceSnapshot snapshot, DateTimeOffset now, bool hideAddress)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _hideAddress = hideAddress;

        var age = NonNegativeAge(now, snapshot.LastSeen);
        Address = snapshot.Address;
        Name = string.IsNullOrWhiteSpace(snapshot.LocalName) ? "جهاز BLE غير مسمّى" : snapshot.LocalName;
        RssiText = string.Create(CultureInfo.InvariantCulture, $"{snapshot.SmoothedRssi:0} dBm");
        StrengthPercent = SignalAnalyzer.StrengthPercent(snapshot.SmoothedRssi);
        BandLabel = SignalAnalyzer.Label(SignalAnalyzer.Band(snapshot.SmoothedRssi));
        IsStale = snapshot.IsStale;
        Freshness = snapshot.IsStale ? "الإشارة قديمة" : "مباشر";
        LastSeenText = string.Create(CultureInfo.InvariantCulture, $"آخر ظهور منذ {age.TotalSeconds:0.#} ث");
        MetadataText = MetadataFormatter.Summary(snapshot);
    }

    public ulong Address { get; }

    public string Name { get; }

    public string AddressText => _hideAddress ? AddressFormatter.Masked : AddressFormatter.Short(Address);

    public string RssiText { get; }

    public int StrengthPercent { get; }

    public string BandLabel { get; }

    public bool IsStale { get; }

    public string Freshness { get; }

    public string LastSeenText { get; }

    public string MetadataText { get; }

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
}
