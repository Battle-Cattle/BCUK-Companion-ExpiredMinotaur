using BCUKCompanion.ExpiredMinotaur.Wiz;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz;

public sealed class WizDeviceListTests
{
    private static WizDevice Device(string ipAddress, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "Lamp", ipAddress, WizDeviceType.Light, true, true, true);

    [Fact]
    public void IsDuplicateIp_Adding_AnAddressAlreadyInUse_IsDuplicate()
    {
        var devices = new[] { Device("192.168.1.20"), Device("192.168.1.21") };

        Assert.True(WizDeviceList.IsDuplicateIp(devices, "192.168.1.21"));
    }

    [Fact]
    public void IsDuplicateIp_Adding_AnUnusedAddress_IsNotDuplicate()
    {
        var devices = new[] { Device("192.168.1.20") };

        Assert.False(WizDeviceList.IsDuplicateIp(devices, "192.168.1.22"));
    }

    [Fact]
    public void IsDuplicateIp_Adding_ToAnEmptyList_IsNotDuplicate()
    {
        Assert.False(WizDeviceList.IsDuplicateIp([], "192.168.1.20"));
    }

    [Fact]
    public void IsDuplicateIp_Editing_ADeviceKeepingItsOwnAddress_IsNotDuplicate()
    {
        var id = Guid.NewGuid();
        var devices = new[] { Device("192.168.1.20", id), Device("192.168.1.21") };

        Assert.False(WizDeviceList.IsDuplicateIp(devices, "192.168.1.20", exceptId: id));
    }

    [Fact]
    public void IsDuplicateIp_Editing_ToAnotherDevicesAddress_IsDuplicate()
    {
        var id = Guid.NewGuid();
        var devices = new[] { Device("192.168.1.20", id), Device("192.168.1.21") };

        Assert.True(WizDeviceList.IsDuplicateIp(devices, "192.168.1.21", exceptId: id));
    }

    [Theory]
    [InlineData("fe80::1", "FE80::1")]
    [InlineData("fe80::1", "fe80:0:0::1")]
    [InlineData("2001:db8::1", "2001:0db8:0000:0000:0000:0000:0000:0001")]
    public void IsDuplicateIp_TwoSpellingsOfTheSameIPv6Address_AreDuplicates(string existing, string candidate)
    {
        Assert.True(WizDeviceList.IsDuplicateIp([Device(existing)], candidate));
    }

    [Fact]
    public void IsDuplicateIp_UnparsableAddresses_AreComparedAsTextIgnoringCase()
    {
        // A hand-edited config can hold an invalid address; it still mustn't be added twice.
        Assert.True(WizDeviceList.IsDuplicateIp([Device("Lamp.Local")], "lamp.local"));
        Assert.False(WizDeviceList.IsDuplicateIp([Device("lamp.local")], "desk.local"));
    }

    [Theory]
    [InlineData(0, "Updated device.")]
    [InlineData(2, "Updated device and removed 2 action(s) no longer valid for it.")]
    public void UpdatedMessage_MentionsRemovedActionsOnlyWhenThereWereAny(int removed, string expected)
    {
        Assert.Equal(expected, WizDeviceList.UpdatedMessage(removed));
    }

    [Theory]
    [InlineData(0, "Removed device.")]
    [InlineData(3, "Removed device and 3 action(s) that referenced it.")]
    public void RemovedMessage_MentionsRemovedActionsOnlyWhenThereWereAny(int removed, string expected)
    {
        Assert.Equal(expected, WizDeviceList.RemovedMessage(removed));
    }
}
