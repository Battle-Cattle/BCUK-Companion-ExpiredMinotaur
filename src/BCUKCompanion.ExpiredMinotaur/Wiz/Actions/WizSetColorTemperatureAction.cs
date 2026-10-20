namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a tunable-white light on at a set colour temperature.</summary>
public sealed class WizSetColorTemperatureAction() : WizRangeSettingAction(ColorTemperatureSetting)
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.setColorTemperature";

    /// <summary>The colour temperature to set, in kelvin (range: <see cref="WizRangeSettingAction.ColorTemperatureSetting"/>).</summary>
    public int ColorTemperatureKelvin { get => Value; set => Value = value; }
}
