using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

public sealed class WizSetColorAction : WizDeviceActionBase
{
    public const string ActionKind = "wiz.setColor";

    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    [JsonIgnore]
    public override string Kind => ActionKind;

    protected override bool SupportsPlugs => false;

    protected override string DescribeAction() => $"SetColor ({R},{G},{B})";

    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device)
        => device.SupportsColor ? [] : [$"{device.Name} does not support color."];

    protected override object BuildPayload() => new { state = true, r = R, g = G, b = B };
}
