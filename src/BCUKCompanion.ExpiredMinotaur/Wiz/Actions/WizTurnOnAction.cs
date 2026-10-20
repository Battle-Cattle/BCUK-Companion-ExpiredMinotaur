using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a light or plug on.</summary>
public sealed class WizTurnOnAction : WizSetStateAction
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.turnOn";

    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => ActionKind;

    /// <inheritdoc/>
    protected override bool TargetState => true;

    /// <inheritdoc/>
    protected override string DescribeAction() => "TurnOn";
}
