namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a dimmable light on at a set brightness.</summary>
public sealed class WizSetBrightnessAction() : WizRangeSettingAction(Dimming)
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.setBrightness";

    /// <summary>The lowest brightness a WiZ light accepts, in percent.</summary>
    public const int MinBrightness = 1;

    /// <summary>The highest brightness, in percent.</summary>
    public const int MaxBrightness = 100;

    private static readonly RangeSetting Dimming = new(
        ActionKind, "dimming", MinBrightness, MaxBrightness, device => device.SupportsDimming, "brightness control",
        $"Brightness must be between {MinBrightness} and {MaxBrightness}.", value => $"SetBrightness {value}%");

    /// <summary>The brightness to set, in percent (<see cref="MinBrightness"/>–<see cref="MaxBrightness"/>).</summary>
    public int Brightness { get => Value; set => Value = value; }
}
