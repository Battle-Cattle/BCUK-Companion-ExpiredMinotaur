using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

public sealed class WizTurnOnAction : WizSetStateAction
{
    public const string ActionKind = "wiz.turnOn";

    [JsonIgnore]
    public override string Kind => ActionKind;

    protected override bool TargetState => true;

    protected override string DescribeAction() => "TurnOn";
}
