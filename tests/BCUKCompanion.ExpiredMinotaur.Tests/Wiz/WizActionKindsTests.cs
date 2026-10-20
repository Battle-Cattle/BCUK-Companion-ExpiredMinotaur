using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz;

public sealed class WizActionKindsTests
{
    private static readonly string[] OnOff = [WizTurnOnAction.ActionKind, WizTurnOffAction.ActionKind, WizToggleAction.ActionKind];

    private static WizDevice Light(bool color = false, bool temperature = false, bool dimming = false) =>
        new(Guid.NewGuid(), "Lamp", "192.168.1.30", WizDeviceType.Light, color, temperature, dimming);

    [Fact]
    public void NoDevice_OffersNothing()
    {
        Assert.Empty(WizActionKinds.For(null));
    }

    [Fact]
    public void Plug_OffersOnlyOnOff()
    {
        var plug = new WizDevice(Guid.NewGuid(), "Fan", "192.168.1.31", WizDeviceType.Plug, true, true, true);

        Assert.Equal(OnOff, WizActionKinds.For(plug));
    }

    [Fact]
    public void LightWithNoCapabilities_OffersOnlyOnOff()
    {
        Assert.Equal(OnOff, WizActionKinds.For(Light()));
    }

    [Theory]
    [InlineData(true, false, false, WizSetColorAction.ActionKind)]
    [InlineData(false, true, false, WizSetColorTemperatureAction.ActionKind)]
    [InlineData(false, false, true, WizSetBrightnessAction.ActionKind)]
    public void LightWithOneCapability_OffersOnOffPlusThatSetting(bool color, bool temperature, bool dimming, string kind)
    {
        Assert.Equal([.. OnOff, kind], WizActionKinds.For(Light(color, temperature, dimming)));
    }

    [Fact]
    public void FullColorLight_OffersEveryKindInOrder()
    {
        Assert.Equal(WizActionKinds.AllKinds, WizActionKinds.For(Light(true, true, true)));
    }
}
