using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a tunable-white light on at a set colour temperature.</summary>
public sealed class WizSetColorTemperatureAction : WizRangeSettingAction
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.setColorTemperature";

    /// <summary>The warmest colour temperature a WiZ light accepts, in kelvin.</summary>
    public const int MinColorTemperatureKelvin = 2200;

    /// <summary>The coolest colour temperature a WiZ light accepts, in kelvin.</summary>
    public const int MaxColorTemperatureKelvin = 6500;

    private static readonly RangeSetting Temperature = new(
        "temp", MinColorTemperatureKelvin, MaxColorTemperatureKelvin, device => device.SupportsColorTemperature,
        "color temperature",
        $"Color temperature must be between {MinColorTemperatureKelvin}K and {MaxColorTemperatureKelvin}K.");

    /// <summary>The colour temperature to set, in kelvin (<see cref="MinColorTemperatureKelvin"/>–<see cref="MaxColorTemperatureKelvin"/>).</summary>
    public int ColorTemperatureKelvin { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => ActionKind;

    /// <inheritdoc/>
    protected override RangeSetting Setting => Temperature;

    /// <inheritdoc/>
    protected override int Value => ColorTemperatureKelvin;

    /// <inheritdoc/>
    protected override string DescribeAction() => $"SetColorTemperature {ColorTemperatureKelvin}K";
}
