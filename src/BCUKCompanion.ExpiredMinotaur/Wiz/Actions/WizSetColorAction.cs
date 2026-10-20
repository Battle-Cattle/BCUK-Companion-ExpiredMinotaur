using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a colour light on at a set RGB colour.</summary>
public sealed class WizSetColorAction : WizDeviceActionBase
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.setColor";

    /// <summary>The colour's red component.</summary>
    public byte R { get; set; }

    /// <summary>The colour's green component.</summary>
    public byte G { get; set; }

    /// <summary>The colour's blue component.</summary>
    public byte B { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => ActionKind;

    /// <inheritdoc/>
    protected override bool SupportsPlugs => false;

    /// <inheritdoc/>
    protected override string DescribeAction() => $"SetColor ({R},{G},{B})";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device)
        => device.SupportsColor ? [] : [$"{device.Name} does not support color."];

    /// <inheritdoc/>
    protected override object BuildPayload() => new { state = true, r = R, g = G, b = B };
}
