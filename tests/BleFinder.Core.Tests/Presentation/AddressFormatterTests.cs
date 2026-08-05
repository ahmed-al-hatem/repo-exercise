using BleFinder.Core.Presentation;

namespace BleFinder.Core.Tests.Presentation;

public sealed class AddressFormatterTests
{
    [Fact]
    public void FormatsAndMasksBluetoothAddress()
    {
        Assert.Equal("AA:BB:CC:DD:EE:FF", AddressFormatter.Full(0xAABBCCDDEEFF));
        Assert.Equal("AA:BB:…:EE:FF", AddressFormatter.Short(0xAABBCCDDEEFF));
        Assert.Equal("••:••:••:••:••:••", AddressFormatter.Masked);
    }

    [Theory]
    [InlineData("tag", true)]
    [InlineData("DD:EE", true)]
    [InlineData("sensor", false)]
    public void MatchesNameOrAddress(string query, bool expected) =>
        Assert.Equal(expected, AddressFormatter.Matches(0xAABBCCDDEEFF, "My Tag", query));
}
