using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

public sealed class WizSetColorTemperatureAction : WizDeviceActionBase
{
    public const string ActionKind = "wiz.setColorTemperature";
    public const int MinColorTemperatureKelvin = 2200;
    public const int MaxColorTemperatureKelvin = 6500;

    public int ColorTemperatureKelvin { get; set; }

    [JsonIgnore]
    public override string Kind => ActionKind;

    protected override bool SupportsPlugs => false;

    protected override string DescribeAction() => $"SetColorTemperature {ColorTemperatureKelvin}K";

    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device)
        => ValidateCapabilityAndRange(device, device.SupportsColorTemperature,
            $"{device.Name} does not support color temperature.",
            ColorTemperatureKelvin, MinColorTemperatureKelvin, MaxColorTemperatureKelvin,
            $"Color temperature must be between {MinColorTemperatureKelvin}K and {MaxColorTemperatureKelvin}K.");

    protected override object BuildPayload() => new { state = true, temp = ColorTemperatureKelvin };
}
