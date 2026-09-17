using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

public sealed class WizTurnOffAction : WizSetStateAction
{
    public const string ActionKind = "wiz.turnOff";

    [JsonIgnore]
    public override string Kind => ActionKind;

    protected override bool TargetState => false;

    protected override string DescribeAction() => "TurnOff";
}
