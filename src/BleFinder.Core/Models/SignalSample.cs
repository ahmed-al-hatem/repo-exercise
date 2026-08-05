namespace BleFinder.Core.Models;

public readonly record struct SignalSample(double Rssi, DateTimeOffset Timestamp);
