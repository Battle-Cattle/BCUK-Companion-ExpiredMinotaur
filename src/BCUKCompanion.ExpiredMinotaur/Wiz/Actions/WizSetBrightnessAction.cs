using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a dimmable light on at a set brightness.</summary>
public sealed class WizSetBrightnessAction : WizDeviceActionBase
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.setBrightness";
    /// <summary>The lowest brightness a WiZ light accepts, in percent.</summary>
    public const int MinBrightness = 1;

    /// <summary>The highest brightness, in percent.</summary>
    public const int MaxBrightness = 100;

    /// <summary>The brightness to set, in percent (<see cref="MinBrightness"/>–<see cref="MaxBrightness"/>).</summary>
    public int Brightness { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => ActionKind;

    /// <inheritdoc/>
    protected override bool SupportsPlugs => false;

    /// <inheritdoc/>
    protected override string DescribeAction() => $"SetBrightness {Brightness}%";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device)
        => ValidateCapabilityAndRange(new CapabilityRangeCheck(
            device.SupportsDimming,
            $"{device.Name} does not support brightness control.",
            Brightness, MinBrightness, MaxBrightness,
            $"Brightness must be between {MinBrightness} and {MaxBrightness}."));

    /// <inheritdoc/>
    protected override object BuildPayload() => StatePayload("dimming", Brightness);
}
