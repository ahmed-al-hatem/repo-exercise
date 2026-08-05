namespace BleFinder.Core.Time;

public interface IClock
{
    DateTimeOffset Now { get; }
}
