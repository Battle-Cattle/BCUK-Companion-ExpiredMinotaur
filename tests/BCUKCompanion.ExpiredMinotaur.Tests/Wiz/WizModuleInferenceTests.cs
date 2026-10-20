using BCUKCompanion.ExpiredMinotaur.Wiz;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz;

public sealed class WizModuleInferenceTests
{
    [Theory]
    [InlineData("ESP10_SOCKET_06", WizDeviceType.Plug)]
    [InlineData("esp10_socket_06", WizDeviceType.Plug)]
    [InlineData("ESP25_SHRGB_01", WizDeviceType.Light)]
    [InlineData("ESP06_SHDW9_01", WizDeviceType.Light)]
    public void InferDeviceType(string moduleName, WizDeviceType expected)
    {
        Assert.Equal(expected, WizModuleInference.InferDeviceType(moduleName));
    }

    [Theory]
    [InlineData("ESP10_SOCKET_06", false, false, false)]
    [InlineData("ESP25_SHRGB_01", true, true, true)] // colour implies colour temperature
    [InlineData("ESP15_SHTW_01I", false, true, true)] // tunable white
    [InlineData("ESP06_SHDW9_01", false, false, true)] // dimmable white only
    public void InferCapabilities(string moduleName, bool color, bool colorTemp, bool dimming)
    {
        Assert.Equal((color, colorTemp, dimming), WizModuleInference.InferCapabilities(moduleName));
    }

    [Fact]
    public void ToDevice_FillsInTheInferredDetails()
    {
        var id = Guid.NewGuid();

        var device = WizModuleInference.ToDevice(new WizDiscoveredDevice("192.168.1.20", "ESP25_SHRGB_01"), id);

        Assert.Equal(new WizDevice(id, "ESP25_SHRGB_01", "192.168.1.20", WizDeviceType.Light, true, true, true), device);
    }
}
