namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>
/// Shared shape for the Wiz actions that just push a fixed on/off state to the device
/// (<see cref="WizTurnOnAction"/> and <see cref="WizTurnOffAction"/>), which otherwise differ
/// only in their kind string, description, and target state.
/// </summary>
public abstract class WizSetStateAction : WizDeviceActionBase
{
    /// <summary>The on/off state the action sends.</summary>
    protected abstract bool TargetState { get; }

    /// <inheritdoc/>
    protected override bool SupportsPlugs => true;

    /// <inheritdoc/>
    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device) => [];

    /// <inheritdoc/>
    protected override object BuildPayload() => new { state = TargetState };
}
