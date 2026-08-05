namespace BleFinder.Core.Presentation;

public static class AddressFormatter
{
    public const string Masked = "••:••:••:••:••:••";

    public static string Full(ulong address)
    {
        var hexadecimal = (address & 0xFFFFFFFFFFFF).ToString("X12");
        return $"{hexadecimal[..2]}:{hexadecimal[2..4]}:{hexadecimal[4..6]}:{hexadecimal[6..8]}:{hexadecimal[8..10]}:{hexadecimal[10..12]}";
    }

    public static string Short(ulong address)
    {
        var hexadecimal = Full(address);
        return $"{hexadecimal[..6]}…:{hexadecimal[12..]}";
    }

    public static bool Matches(ulong address, string? name, string query)
    {
        var trimmedQuery = query.Trim();
        var colonizedAddress = Full(address);
        var uncolonizedAddress = colonizedAddress.Replace(":", string.Empty, StringComparison.Ordinal);

        return name?.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) == true ||
               colonizedAddress.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
               uncolonizedAddress.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase);
    }
}
