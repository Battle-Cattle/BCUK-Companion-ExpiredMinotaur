using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz;

public sealed class WizActionTests
{
    private static readonly WizDevice ColorLight =
        new(Guid.NewGuid(), "Desk", "192.168.1.20", WizDeviceType.Light, true, true, true);

    private static readonly WizDevice WhiteLight =
        new(Guid.NewGuid(), "Hall", "192.168.1.21", WizDeviceType.Light, false, false, false);

    private static readonly WizDevice Plug =
        new(Guid.NewGuid(), "Fan", "192.168.1.22", WizDeviceType.Plug, false, false, false);

    private static readonly WizActionContext Context = new(new WizClient(), [ColorLight, WhiteLight, Plug]);

    [Fact]
    public void Describe_NamesTheDeviceAndAction()
    {
        Assert.Equal("Desk: TurnOn", new WizTurnOnAction { DeviceId = ColorLight.Id }.Describe(Context));
        Assert.Equal("Desk: TurnOff", new WizTurnOffAction { DeviceId = ColorLight.Id }.Describe(Context));
        Assert.Equal("Desk: Toggle", new WizToggleAction { DeviceId = ColorLight.Id }.Describe(Context));
        Assert.Equal("Desk: SetBrightness 40%", new WizSetBrightnessAction { DeviceId = ColorLight.Id, Brightness = 40 }.Describe(Context));
        Assert.Equal("Desk: SetColor (1,2,3)", new WizSetColorAction { DeviceId = ColorLight.Id, R = 1, G = 2, B = 3 }.Describe(Context));
        Assert.Equal(
            "Desk: SetColorTemperature 2700K",
            new WizSetColorTemperatureAction { DeviceId = ColorLight.Id, ColorTemperatureKelvin = 2700 }.Describe(Context));
    }

    [Fact]
    public void Describe_RemovedDevice_SaysUnknown()
    {
        Assert.Equal("(unknown device): TurnOn", new WizTurnOnAction { DeviceId = Guid.NewGuid() }.Describe(Context));
        Assert.Equal("(unknown device): TurnOn", new WizTurnOnAction { DeviceId = ColorLight.Id }.Describe(new EmptyActionContext()));
    }

    [Fact]
    public void Validate_RemovedDevice_ReportsIt()
    {
        Assert.Equal(["Device not found."], new WizToggleAction { DeviceId = Guid.NewGuid() }.Validate(Context));
    }

    [Fact]
    public void Validate_OnOffActionsOnAPlug_AreValid()
    {
        Assert.Empty(new WizTurnOnAction { DeviceId = Plug.Id }.Validate(Context));
        Assert.Empty(new WizTurnOffAction { DeviceId = Plug.Id }.Validate(Context));
        Assert.Empty(new WizToggleAction { DeviceId = Plug.Id }.Validate(Context));
    }

    [Fact]
    public void Validate_LightOnlyActionOnAPlug_ReportsBothProblems()
    {
        var errors = new WizSetBrightnessAction { DeviceId = Plug.Id, Brightness = 50 }.Validate(Context);

        Assert.Equal(["Plugs do not support wiz.setBrightness.", "Fan does not support brightness control."], errors);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    public void Validate_BrightnessRange(int brightness, bool valid)
    {
        var errors = new WizSetBrightnessAction { DeviceId = ColorLight.Id, Brightness = brightness }.Validate(Context);

        Assert.Equal(valid, errors.Count == 0);
    }

    [Theory]
    [InlineData(2200, true)]
    [InlineData(6500, true)]
    [InlineData(2199, false)]
    [InlineData(6501, false)]
    public void Validate_ColorTemperatureRange(int kelvin, bool valid)
    {
        var errors = new WizSetColorTemperatureAction { DeviceId = ColorLight.Id, ColorTemperatureKelvin = kelvin }.Validate(Context);

        Assert.Equal(valid, errors.Count == 0);
    }

    [Fact]
    public void Validate_CapabilityTheLightLacks_ReportsIt()
    {
        Assert.Equal(["Hall does not support color."], new WizSetColorAction { DeviceId = WhiteLight.Id }.Validate(Context));
        Assert.Equal(
            ["Hall does not support color temperature."],
            new WizSetColorTemperatureAction { DeviceId = WhiteLight.Id, ColorTemperatureKelvin = 2700 }.Validate(Context));
        Assert.Equal(
            ["Hall does not support brightness control.", "Brightness must be between 1 and 100."],
            new WizSetBrightnessAction { DeviceId = WhiteLight.Id, Brightness = 0 }.Validate(Context));
    }

    [Fact]
    public async Task ExecuteAsync_WithoutWizContext_Throws()
    {
        var action = new WizTurnOnAction { DeviceId = ColorLight.Id };

        await Assert.ThrowsAsync<InvalidOperationException>(() => action.ExecuteAsync(new EmptyActionContext(), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_RemovedDevice_Throws()
    {
        var action = new WizTurnOnAction { DeviceId = Guid.NewGuid() };

        await Assert.ThrowsAsync<InvalidOperationException>(() => action.ExecuteAsync(Context, CancellationToken.None));
    }

    [Fact]
    public void WizActionContext_ProvidesOnlyItself()
    {
        Assert.Same(Context, Context.GetService(typeof(WizActionContext)));
        Assert.Null(Context.GetService(typeof(WizClient)));
    }
}
