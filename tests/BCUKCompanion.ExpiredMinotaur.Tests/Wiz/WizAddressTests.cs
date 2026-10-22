using BCUKCompanion.ExpiredMinotaur.Wiz;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz;

public sealed class WizAddressTests
{
    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData("10.0.0.5")]
    [InlineData("255.255.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::8a2e:370:7334")]
    public void IsValid_FullDottedQuadOrIPv6_IsAccepted(string ipAddress)
    {
        Assert.True(WizAddress.IsValid(ipAddress));
    }

    [Theory]
    [InlineData("192.168.1")] // inet_aton shorthand for 192.168.0.1
    [InlineData("10.5")] // 10.0.0.5
    [InlineData("3232235777")] // 192.168.1.1
    [InlineData("192.168.001.020")] // leading zeros, which inet_aton can read as octal
    [InlineData("0x7f.0.0.1")]
    [InlineData("192.168.1.256")]
    [InlineData(" 192.168.1.20")]
    [InlineData("lamp.local")]
    [InlineData("")]
    [InlineData(null)]
    public void IsValid_ShorthandOrNonAddress_IsRejected(string? ipAddress)
    {
        Assert.False(WizAddress.IsValid(ipAddress));
    }
}
