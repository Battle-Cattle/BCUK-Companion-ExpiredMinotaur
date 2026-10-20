namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>
/// Shared shape for the Wiz actions that turn a light on at one numeric setting in a fixed range
/// (<see cref="WizSetBrightnessAction"/>, <see cref="WizSetColorTemperatureAction"/>): they differ
/// only in which <see cref="RangeSetting"/> they set and the value they hold.
/// </summary>
public abstract class WizRangeSettingAction : WizDeviceActionBase
{
    /// <summary>Lights only: a plug has no brightness or colour temperature.</summary>
    protected override bool SupportsPlugs => false;

    /// <summary>Which setting the action sets, with its range and the capability it needs.</summary>
    protected abstract RangeSetting Setting { get; }

    /// <summary>The value the action sets.</summary>
    protected abstract int Value { get; }

    /// <summary>Checks the device has the setting's capability and the value is within its range.</summary>
    /// <param name="device">The target device.</param>
    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device)
        => ValidateCapabilityAndRange(new CapabilityRangeCheck(
            Setting.IsSupportedBy(device),
            $"{device.Name} does not support {Setting.Capability}.",
            Value, Setting.Min, Setting.Max,
            Setting.OutOfRangeMessage));

    /// <summary>Turns the light on with the setting's <c>setPilot</c> property set to <see cref="Value"/>.</summary>
    protected override object BuildPayload() => StatePayload(Setting.PilotProperty, Value);

    /// <summary>One numeric light setting.</summary>
    /// <param name="PilotProperty">The <c>setPilot</c> property it's sent as, e.g. <c>dimming</c>.</param>
    /// <param name="Min">The lowest value a WiZ light accepts.</param>
    /// <param name="Max">The highest value a WiZ light accepts.</param>
    /// <param name="IsSupportedBy">Whether a device has the capability.</param>
    /// <param name="Capability">The capability's name in the "does not support …" error.</param>
    /// <param name="OutOfRangeMessage">The error for a value outside Min–Max.</param>
    protected sealed record RangeSetting(
        string PilotProperty, int Min, int Max, Func<WizDevice, bool> IsSupportedBy, string Capability, string OutOfRangeMessage);
}
