namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>
/// Shared shape for the Wiz actions that just push a fixed on/off state to the device
/// (<see cref="WizTurnOnAction"/> and <see cref="WizTurnOffAction"/>), which otherwise differ
/// only in their kind string, description, and target state.
/// </summary>
public abstract class WizSetStateAction : WizDeviceActionBase
{
    protected abstract bool TargetState { get; }

    protected override bool SupportsPlugs => true;

    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device) => [];

    protected override object BuildPayload() => new { state = TargetState };
}
