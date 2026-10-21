using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

/// <summary>Which Wiz action kinds a device can run, kept out of the editor so it can be tested without WPF.</summary>
internal static class WizActionKinds
{
    /// <summary>The kinds a plug supports: plain on/off.</summary>
    public static readonly IReadOnlyList<string> PlugKinds = [WizTurnOnAction.ActionKind, WizTurnOffAction.ActionKind, WizToggleAction.ActionKind];

    /// <summary>Every Wiz action kind, in the order the editor lists them.</summary>
    public static readonly IReadOnlyList<string> AllKinds =
    [
        WizTurnOnAction.ActionKind,
        WizTurnOffAction.ActionKind,
        WizToggleAction.ActionKind,
        WizSetBrightnessAction.ActionKind,
        WizSetColorAction.ActionKind,
        WizSetColorTemperatureAction.ActionKind,
    ];

    /// <summary>The kinds <paramref name="device"/> can run, in <see cref="AllKinds"/> order.</summary>
    /// <param name="device">The selected device, or null when none is selected.</param>
    /// <returns>
    /// Nothing for no device; the on/off kinds for a plug; for a light, the on/off kinds plus each light setting the
    /// light has the capability for.
    /// </returns>
    public static IReadOnlyList<string> For(WizDevice? device) => device switch
    {
        null => [],
        { DeviceType: WizDeviceType.Plug } => PlugKinds,
        _ => AllKinds.Where(kind => WizLightSettingAction.ForKind(kind)?.IsSupportedBy(device) ?? true).ToArray(),
    };
}
