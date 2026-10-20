using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a dimmable light on at a set brightness.</summary>
public sealed class WizSetBrightnessAction : WizRangeSettingAction
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.setBrightness";

    /// <summary>The lowest brightness a WiZ light accepts, in percent.</summary>
    public const int MinBrightness = 1;

    /// <summary>The highest brightness, in percent.</summary>
    public const int MaxBrightness = 100;

    private static readonly RangeSetting Dimming = new(
        "dimming", MinBrightness, MaxBrightness, device => device.SupportsDimming,
        "brightness control", $"Brightness must be between {MinBrightness} and {MaxBrightness}.");

    /// <summary>The brightness to set, in percent (<see cref="MinBrightness"/>–<see cref="MaxBrightness"/>).</summary>
    public int Brightness { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => ActionKind;

    /// <inheritdoc/>
    protected override RangeSetting Setting => Dimming;

    /// <inheritdoc/>
    protected override int Value => Brightness;

    /// <inheritdoc/>
    protected override string DescribeAction() => $"SetBrightness {Brightness}%";
}
