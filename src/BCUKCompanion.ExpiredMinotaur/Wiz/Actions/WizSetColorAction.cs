namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a colour light on at a set RGB colour.</summary>
public sealed class WizSetColorAction() : WizLightSettingAction(ColorSetting)
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.setColor";

    /// <summary>The colour's red component.</summary>
    public byte R { get => (byte)Values[0]; set => Values[0] = value; }

    /// <summary>The colour's green component.</summary>
    public byte G { get => (byte)Values[1]; set => Values[1] = value; }

    /// <summary>The colour's blue component.</summary>
    public byte B { get => (byte)Values[2]; set => Values[2] = value; }
}
