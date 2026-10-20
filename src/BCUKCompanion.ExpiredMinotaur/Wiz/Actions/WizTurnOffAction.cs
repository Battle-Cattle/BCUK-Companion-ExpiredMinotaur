using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a light or plug off.</summary>
public sealed class WizTurnOffAction : WizSetStateAction
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.turnOff";

    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => ActionKind;

    /// <inheritdoc/>
    protected override bool TargetState => false;

    /// <inheritdoc/>
    protected override string DescribeAction() => "TurnOff";
}
