using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>
/// Shared shape for the Wiz actions that turn a light on at a setting: brightness
/// (<see cref="WizSetBrightnessAction"/>), colour temperature (<see cref="WizSetColorTemperatureAction"/>) and RGB
/// colour (<see cref="WizSetColorAction"/>). Each setting is defined here as a <see cref="LightSetting"/>; a
/// subclass only picks one and exposes its <see cref="Values"/> under its own JSON property names.
/// </summary>
/// <param name="setting">Which setting the action sets.</param>
public abstract class WizLightSettingAction(WizLightSettingAction.LightSetting setting) : WizDeviceActionBase
{
    /// <summary>Brightness, in percent: the light must support dimming.</summary>
    public static readonly LightSetting BrightnessSetting = new()
    {
        Kind = WizSetBrightnessAction.ActionKind,
        IsSupportedBy = device => device.SupportsDimming,
        Capability = "brightness control",
        Channels = [new("dimming", 1, 100)],
        OutOfRangeMessage = "Brightness must be between 1 and 100.",
        Describe = values => $"SetBrightness {values[0]}%",
    };

    /// <summary>Colour temperature, in kelvin: the light must support tunable white.</summary>
    public static readonly LightSetting ColorTemperatureSetting = new()
    {
        Kind = WizSetColorTemperatureAction.ActionKind,
        IsSupportedBy = device => device.SupportsColorTemperature,
        Capability = "color temperature",
        Channels = [new("temp", 2200, 6500)],
        OutOfRangeMessage = "Color temperature must be between 2200K and 6500K.",
        Describe = values => $"SetColorTemperature {values[0]}K",
    };

    /// <summary>RGB colour, one 0–255 value per component: the light must support colour.</summary>
    public static readonly LightSetting ColorSetting = new()
    {
        Kind = WizSetColorAction.ActionKind,
        IsSupportedBy = device => device.SupportsColor,
        Capability = "color",
        Channels = [new("r", 0, 255), new("g", 0, 255), new("b", 0, 255)],
        OutOfRangeMessage = "Each color component must be between 0 and 255.",
        Describe = values => $"SetColor ({values[0]},{values[1]},{values[2]})",
    };

    /// <summary>Every light setting, so callers can look one up by its action kind.</summary>
    public static readonly IReadOnlyList<LightSetting> AllSettings = [BrightnessSetting, ColorTemperatureSetting, ColorSetting];

    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => setting.Kind;

    /// <summary>Lights only: a plug has no brightness, colour temperature or colour.</summary>
    protected override bool SupportsPlugs => false;

    /// <summary>The values the action sets, one per <see cref="LightSetting.Channels"/> entry, in the same order.</summary>
    protected int[] Values { get; } = new int[setting.Channels.Count];

    /// <summary>Finds the light setting an action kind sets.</summary>
    /// <param name="kind">An action's <c>kind</c>, e.g. <c>wiz.setBrightness</c>.</param>
    /// <returns>The setting, or null when the kind isn't a light-setting action (e.g. <c>wiz.toggle</c>).</returns>
    public static LightSetting? ForKind(string kind) => AllSettings.FirstOrDefault(s => s.Kind == kind);

    /// <inheritdoc/>
    protected override string DescribeAction() => setting.Describe(Values);

    /// <summary>Checks the device has the setting's capability and every value is within its channel's range.</summary>
    /// <param name="device">The target device.</param>
    /// <returns>The missing capability and the out-of-range error, each when it applies.</returns>
    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device)
    {
        var errors = new List<string>();
        if (!setting.IsSupportedBy(device)) errors.Add($"{device.Name} does not support {setting.Capability}.");
        if (!setting.Accepts(Values)) errors.Add(setting.OutOfRangeMessage);
        return errors;
    }

    /// <summary>Turns the light on with each channel's <c>setPilot</c> property set to its value.</summary>
    /// <returns>The <c>setPilot</c> params, <c>state</c> first and then the channels in order.</returns>
    protected override object BuildPayload()
    {
        var payload = new Dictionary<string, object> { ["state"] = true };
        for (int i = 0; i < Values.Length; i++)
        {
            payload[setting.Channels[i].PilotProperty] = Values[i];
        }

        return payload;
    }

    /// <summary>One light setting: what a light needs to support it, and the values it's sent as.</summary>
    public sealed class LightSetting
    {
        /// <summary>The action's <c>kind</c> in actions-config.json.</summary>
        public required string Kind { get; init; }

        /// <summary>Whether a device has the capability the setting needs.</summary>
        public required Func<WizDevice, bool> IsSupportedBy { get; init; }

        /// <summary>The capability's name in the "does not support …" error.</summary>
        public required string Capability { get; init; }

        /// <summary>The values the setting is sent as, in order.</summary>
        public required IReadOnlyList<Channel> Channels { get; init; }

        /// <summary>The error for a value outside its channel's range.</summary>
        public required string OutOfRangeMessage { get; init; }

        /// <summary>The action's description in the mappings list, from its values.</summary>
        public required Func<IReadOnlyList<int>, string> Describe { get; init; }

        /// <summary>Whether there's one value per channel and each is within its channel's range.</summary>
        /// <param name="values">One value per channel, in channel order.</param>
        /// <returns>True when the count matches and every value is in range; false for too few or too many values.</returns>
        public bool Accepts(IReadOnlyList<int> values)
            => values.Count == Channels.Count
                && Channels.Select((channel, i) => values[i] >= channel.Min && values[i] <= channel.Max).All(ok => ok);
    }

    /// <summary>One value a light setting is sent as.</summary>
    /// <param name="PilotProperty">The <c>setPilot</c> property it's sent as, e.g. <c>dimming</c>.</param>
    /// <param name="Min">The lowest value a WiZ light accepts.</param>
    /// <param name="Max">The highest value a WiZ light accepts.</param>
    public sealed record Channel(string PilotProperty, int Min, int Max);
}
