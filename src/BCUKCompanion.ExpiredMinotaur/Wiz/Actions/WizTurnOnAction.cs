using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

public sealed class WizTurnOnAction : WizDeviceActionBase
{
    public const string ActionKind = "wiz.turnOn";

    [JsonIgnore]
    public override string Kind => ActionKind;

    protected override bool SupportsPlugs => true;

    protected override string DescribeAction() => "TurnOn";

    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device) => [];

    protected override object BuildPayload() => new { state = true };
}
