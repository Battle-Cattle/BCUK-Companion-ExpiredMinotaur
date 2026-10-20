namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a dimmable light on at a set brightness.</summary>
public sealed class WizSetBrightnessAction() : WizLightSettingAction(BrightnessSetting)
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.setBrightness";

    /// <summary>The brightness to set, in percent (range: <see cref="WizLightSettingAction.BrightnessSetting"/>).</summary>
    public int Brightness { get => Values[0]; set => Values[0] = value; }
}
