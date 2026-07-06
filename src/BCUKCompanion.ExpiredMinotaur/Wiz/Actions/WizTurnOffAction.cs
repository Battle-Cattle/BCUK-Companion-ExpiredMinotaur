using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

public sealed class WizTurnOffAction : WizDeviceActionBase
{
    public const string ActionKind = "wiz.turnOff";

    [JsonIgnore]
    public override string Kind => ActionKind;

    protected override bool SupportsPlugs => true;

    protected override string DescribeAction() => "TurnOff";

    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device) => [];

    protected override object BuildPayload() => new { state = false };
}
