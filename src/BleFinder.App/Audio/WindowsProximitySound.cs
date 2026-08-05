using System.Media;
using System.Windows;
using BleFinder.Core.Audio;

namespace BleFinder.App.Audio;

public sealed class WindowsProximitySound : IProximitySound
{
    private static readonly TimeSpan DisabledInterval = Timeout.InfiniteTimeSpan;

    private readonly object _gate = new();
    private readonly Stream _clickStream;
    private readonly SoundPlayer _player;
    private readonly System.Threading.Timer _timer;
    private bool _enabled;
    private bool _selected;
    private double? _rssi;
    private TimeSpan _age;
    private bool _timerScheduled;
    private bool _disposed;

    public WindowsProximitySound()
    {
        var resource = Application.GetResourceStream(
            new Uri("pack://application:,,,/Resources/click.wav", UriKind.Absolute));
        _clickStream = resource?.Stream
            ?? throw new InvalidOperationException("The embedded proximity click resource is unavailable.");
        _player = new SoundPlayer(_clickStream);
        _player.Load();
        _timer = new System.Threading.Timer(OnTimerElapsed, null, DisabledInterval, DisabledInterval);
    }

    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
        set
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _enabled = value;
                UpdateScheduleLocked();
            }
        }
    }

    public void Update(double? rssi, bool selected, TimeSpan age)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _rssi = rssi;
            _selected = selected;
            _age = age;
            UpdateScheduleLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timerScheduled = false;
            _timer.Dispose();
            _player.Dispose();
            _clickStream.Dispose();
        }
    }

    private void OnTimerElapsed(object? state)
    {
        lock (_gate)
        {
            _timerScheduled = false;

            if (_disposed || !CanPlayLocked())
            {
                return;
            }

            _player.Play();
            ScheduleNextLocked();
        }
    }

    private void UpdateScheduleLocked()
    {
        if (!CanPlayLocked())
        {
            _timerScheduled = false;
            _timer.Change(DisabledInterval, DisabledInterval);
            return;
        }

        if (!_timerScheduled)
        {
            ScheduleNextLocked();
        }
    }

    private void ScheduleNextLocked()
    {
        _timerScheduled = true;
        _timer.Change(ProximityCadence.Interval(_rssi!.Value), DisabledInterval);
    }

    private bool CanPlayLocked() =>
        _rssi.HasValue && ProximityCadence.ShouldPlay(_enabled, _selected, _age);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
