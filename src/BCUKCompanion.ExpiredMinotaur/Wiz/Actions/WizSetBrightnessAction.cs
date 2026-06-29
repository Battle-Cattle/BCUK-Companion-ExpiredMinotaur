using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

public sealed class WizSetBrightnessAction : WizDeviceActionBase
{
    public const string ActionKind = "wiz.setBrightness";
    public const int MinBrightness = 1;
    public const int MaxBrightness = 100;

    public int Brightness { get; set; }

    [JsonIgnore]
    public override string Kind => ActionKind;

    protected override bool SupportsPlugs => false;

    protected override string DescribeAction() => $"SetBrightness {Brightness}%";

    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device)
    {
        var errors = new List<string>();
        if (!device.SupportsDimming)
        {
            errors.Add($"{device.Name} does not support brightness control.");
        }
        if (Brightness < MinBrightness || Brightness > MaxBrightness)
        {
            errors.Add($"Brightness must be between {MinBrightness} and {MaxBrightness}.");
        }
        return errors;
    }

    protected override object BuildPayload() => new { state = true, dimming = Brightness };
}
