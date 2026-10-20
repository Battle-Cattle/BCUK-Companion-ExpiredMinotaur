using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a tunable-white light on at a set colour temperature.</summary>
public sealed class WizSetColorTemperatureAction : WizDeviceActionBase
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.setColorTemperature";
    /// <summary>The warmest colour temperature a WiZ light accepts, in kelvin.</summary>
    public const int MinColorTemperatureKelvin = 2200;

    /// <summary>The coolest colour temperature a WiZ light accepts, in kelvin.</summary>
    public const int MaxColorTemperatureKelvin = 6500;

    /// <summary>The colour temperature to set, in kelvin (<see cref="MinColorTemperatureKelvin"/>–<see cref="MaxColorTemperatureKelvin"/>).</summary>
    public int ColorTemperatureKelvin { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => ActionKind;

    /// <inheritdoc/>
    protected override bool SupportsPlugs => false;

    /// <inheritdoc/>
    protected override string DescribeAction() => $"SetColorTemperature {ColorTemperatureKelvin}K";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device)
        => ValidateCapabilityAndRange(new CapabilityRangeCheck(
            device.SupportsColorTemperature,
            $"{device.Name} does not support color temperature.",
            ColorTemperatureKelvin, MinColorTemperatureKelvin, MaxColorTemperatureKelvin,
            $"Color temperature must be between {MinColorTemperatureKelvin}K and {MaxColorTemperatureKelvin}K."));

    /// <inheritdoc/>
    protected override object BuildPayload() => StatePayload("temp", ColorTemperatureKelvin);
}
