using System.Globalization;
using BleFinder.Core.Models;
using BleFinder.Core.Signals;

namespace BleFinder.Core.Presentation;

public sealed class DeviceRowViewModel : ObservableObject
{
    private bool _hideAddress;
    private string _name = string.Empty;
    private string _rssiText = string.Empty;
    private int _strengthPercent;
    private string _bandLabel = string.Empty;
    private bool _isStale;
    private string _freshness = string.Empty;
    private string _lastSeenText = string.Empty;
    private string _metadataText = string.Empty;

    public DeviceRowViewModel(DeviceSnapshot snapshot, DateTimeOffset now, bool hideAddress)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Address = snapshot.Address;
        _hideAddress = hideAddress;
        Update(snapshot, now, hideAddress);
    }

    public ulong Address { get; }

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public string AddressText => _hideAddress ? AddressFormatter.Masked : AddressFormatter.Short(Address);

    public string RssiText
    {
        get => _rssiText;
        private set => SetProperty(ref _rssiText, value);
    }

    public int StrengthPercent
    {
        get => _strengthPercent;
        private set => SetProperty(ref _strengthPercent, value);
    }

    public string BandLabel
    {
        get => _bandLabel;
        private set => SetProperty(ref _bandLabel, value);
    }

    public bool IsStale
    {
        get => _isStale;
        private set => SetProperty(ref _isStale, value);
    }

    public string Freshness
    {
        get => _freshness;
        private set => SetProperty(ref _freshness, value);
    }

    public string LastSeenText
    {
        get => _lastSeenText;
        private set => SetProperty(ref _lastSeenText, value);
    }

    public string MetadataText
    {
        get => _metadataText;
        private set => SetProperty(ref _metadataText, value);
    }

    internal void Update(DeviceSnapshot snapshot, DateTimeOffset now, bool hideAddress)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Address != Address)
        {
            throw new ArgumentException("A device row cannot change its Bluetooth address.", nameof(snapshot));
        }

        SetAddressHidden(hideAddress);

        var age = NonNegativeAge(now, snapshot.LastSeen);
        Name = string.IsNullOrWhiteSpace(snapshot.LocalName) ? "جهاز BLE غير مسمّى" : snapshot.LocalName;
        RssiText = string.Create(CultureInfo.InvariantCulture, $"{snapshot.SmoothedRssi:0} dBm");
        StrengthPercent = SignalAnalyzer.StrengthPercent(snapshot.SmoothedRssi);
        BandLabel = SignalAnalyzer.Label(SignalAnalyzer.Band(snapshot.SmoothedRssi));
        IsStale = snapshot.IsStale;
        Freshness = snapshot.IsStale ? "الإشارة قديمة" : "مباشر";
        LastSeenText = string.Create(CultureInfo.InvariantCulture, $"آخر ظهور منذ {age.TotalSeconds:0.#} ث");
        MetadataText = MetadataFormatter.Summary(snapshot);
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
}
