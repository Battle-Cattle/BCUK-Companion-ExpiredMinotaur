using System.Reflection;
using System.Text.Json;
using BCUKCompanion.Core.Actions;
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

    private static string Payload(WizDeviceActionBase action) => JsonSerializer.Serialize(
        typeof(WizDeviceActionBase).GetMethod("BuildPayload", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(action, null));

    [Fact]
    public void BuildPayload_SendsTheSetPilotParamsEachActionNeeds()
    {
        Assert.Equal("""{"state":true,"dimming":40}""", Payload(new WizSetBrightnessAction { Brightness = 40 }));
        Assert.Equal("""{"state":true,"temp":2700}""", Payload(new WizSetColorTemperatureAction { ColorTemperatureKelvin = 2700 }));
        Assert.Equal("""{"state":true,"r":1,"g":2,"b":3}""", Payload(new WizSetColorAction { R = 1, G = 2, B = 3 }));
        Assert.Equal("""{"state":true}""", Payload(new WizTurnOnAction()));
        Assert.Equal("""{"state":false}""", Payload(new WizTurnOffAction()));
    }

    [Fact]
    public void Serialization_KeepsTheSavedPropertyNames()
    {
        var registry = new EventActionTypeRegistry();
        registry.Register<WizSetBrightnessAction>(WizSetBrightnessAction.ActionKind);
        registry.Register<WizSetColorTemperatureAction>(WizSetColorTemperatureAction.ActionKind);
        registry.Register<WizSetColorAction>(WizSetColorAction.ActionKind);
        var options = new JsonSerializerOptions { Converters = { new EventActionJsonConverter(registry) } };
        IEventAction[] actions =
        [
            new WizSetBrightnessAction { DeviceId = ColorLight.Id, Brightness = 40 },
            new WizSetColorTemperatureAction { DeviceId = ColorLight.Id, ColorTemperatureKelvin = 2700 },
            new WizSetColorAction { DeviceId = ColorLight.Id, R = 10, G = 20, B = 30 },
        ];

        string json = JsonSerializer.Serialize(actions, options);
        var roundTripped = JsonSerializer.Deserialize<IEventAction[]>(json, options)!;

        Assert.Contains("\"Brightness\":40", json);
        Assert.Contains("\"ColorTemperatureKelvin\":2700", json);
        Assert.Contains("\"R\":10,\"G\":20,\"B\":30", json);
        Assert.DoesNotContain("Values", json);
        Assert.Equal(40, Assert.IsType<WizSetBrightnessAction>(roundTripped[0]).Brightness);
        Assert.Equal(2700, Assert.IsType<WizSetColorTemperatureAction>(roundTripped[1]).ColorTemperatureKelvin);
        Assert.Equal(ColorLight.Id, Assert.IsType<WizSetColorTemperatureAction>(roundTripped[1]).DeviceId);
        var color = Assert.IsType<WizSetColorAction>(roundTripped[2]);
        Assert.Equal((10, 20, 30), (color.R, color.G, color.B));
    }

    [Fact]
    public void Validate_ColorOnAPlug_ReportsBothProblems()
    {
        var errors = new WizSetColorAction { DeviceId = Plug.Id, R = 1 }.Validate(Context);

        Assert.Equal(["Plugs do not support wiz.setColor.", "Fan does not support color."], errors);
    }

    [Fact]
    public void ForKind_FindsEachLightSetting()
    {
        Assert.Same(WizLightSettingAction.BrightnessSetting, WizLightSettingAction.ForKind(WizSetBrightnessAction.ActionKind));
        Assert.Same(WizLightSettingAction.ColorTemperatureSetting, WizLightSettingAction.ForKind(WizSetColorTemperatureAction.ActionKind));
        Assert.Same(WizLightSettingAction.ColorSetting, WizLightSettingAction.ForKind(WizSetColorAction.ActionKind));
        Assert.Null(WizLightSettingAction.ForKind(WizToggleAction.ActionKind));
    }

    [Fact]
    public void LightSetting_IsSupportedBy_MatchesTheDeviceCapability()
    {
        Assert.True(WizLightSettingAction.ColorSetting.IsSupportedBy(ColorLight));
        Assert.False(WizLightSettingAction.ColorSetting.IsSupportedBy(WhiteLight));
        Assert.False(WizLightSettingAction.BrightnessSetting.IsSupportedBy(WhiteLight));
        Assert.False(WizLightSettingAction.ColorTemperatureSetting.IsSupportedBy(WhiteLight));
    }

    [Theory]
    [InlineData(0, 255, 128, true)]
    [InlineData(-1, 0, 0, false)]
    [InlineData(0, 256, 0, false)]
    [InlineData(0, 0, 300, false)]
    public void LightSetting_Accepts_ChecksEveryChannel(int r, int g, int b, bool accepted)
    {
        Assert.Equal(accepted, WizLightSettingAction.ColorSetting.Accepts([r, g, b]));
    }

    [Fact]
    public void WizActionContext_ProvidesOnlyItself()
    {
        Assert.Same(Context, Context.GetService(typeof(WizActionContext)));
        Assert.Null(Context.GetService(typeof(WizClient)));
    }
}
