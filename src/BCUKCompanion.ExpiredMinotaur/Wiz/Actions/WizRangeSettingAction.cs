using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>
/// Shared shape for the Wiz actions that turn a light on at one numeric setting in a fixed range
/// (<see cref="WizSetBrightnessAction"/>, <see cref="WizSetColorTemperatureAction"/>). A subclass
/// only supplies its <see cref="RangeSetting"/> and exposes <see cref="Value"/> under its own
/// JSON property name.
/// </summary>
/// <param name="setting">Which setting the action sets.</param>
public abstract class WizRangeSettingAction(WizRangeSettingAction.RangeSetting setting) : WizDeviceActionBase
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => setting.Kind;

    /// <summary>Lights only: a plug has no brightness or colour temperature.</summary>
    protected override bool SupportsPlugs => false;

    /// <summary>The value the action sets; each subclass saves it under its own property name.</summary>
    protected int Value { get; set; }

    /// <inheritdoc/>
    protected override string DescribeAction() => setting.Describe(Value);

    /// <summary>Checks the device has the setting's capability and the value is within its range.</summary>
    /// <param name="device">The target device.</param>
    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device)
        => ValidateCapabilityAndRange(new CapabilityRangeCheck(
            setting.IsSupportedBy(device),
            $"{device.Name} does not support {setting.Capability}.",
            Value, setting.Min, setting.Max,
            setting.OutOfRangeMessage));

    /// <summary>Turns the light on with the setting's <c>setPilot</c> property set to <see cref="Value"/>.</summary>
    protected override object BuildPayload() => StatePayload(setting.PilotProperty, Value);

    /// <summary>One numeric light setting.</summary>
    /// <param name="Kind">The action's <c>kind</c> in actions-config.json.</param>
    /// <param name="PilotProperty">The <c>setPilot</c> property it's sent as, e.g. <c>dimming</c>.</param>
    /// <param name="Min">The lowest value a WiZ light accepts.</param>
    /// <param name="Max">The highest value a WiZ light accepts.</param>
    /// <param name="IsSupportedBy">Whether a device has the capability.</param>
    /// <param name="Capability">The capability's name in the "does not support …" error.</param>
    /// <param name="OutOfRangeMessage">The error for a value outside Min–Max.</param>
    /// <param name="Describe">The action's description in the mappings list, from its value.</param>
    public sealed record RangeSetting(
        string Kind,
        string PilotProperty,
        int Min,
        int Max,
        Func<WizDevice, bool> IsSupportedBy,
        string Capability,
        string OutOfRangeMessage,
        Func<int, string> Describe);
}
