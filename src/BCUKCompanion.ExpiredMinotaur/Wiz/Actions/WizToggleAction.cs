using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>Turns a light or plug off if it's on, and on if it's off.</summary>
public sealed class WizToggleAction : WizDeviceActionBase
{
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "wiz.toggle";

    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => ActionKind;

    /// <inheritdoc/>
    protected override bool SupportsPlugs => true;

    /// <inheritdoc/>
    protected override string DescribeAction() => "Toggle";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device) => [];

    /// <summary>
    /// Reads the device's current state and sends the opposite. Overrides SendAsync rather than
    /// BuildPayloadAsync: an offline device or one that omits its "state" field is an ordinary
    /// dispatch failure here (matching every other Wiz action, which reports false rather than
    /// throwing), not an exceptional condition that should surface as "Action dispatch crashed".
    /// </summary>
    /// <param name="client">The client to read and set the device's state with.</param>
    /// <param name="device">The device to toggle.</param>
    /// <param name="cancellationToken">Cancels the read or the command.</param>
    /// <returns>False if the device's state couldn't be read, otherwise whether the toggle was accepted.</returns>
    protected override async Task<bool> SendAsync(WizClient client, WizDevice device, CancellationToken cancellationToken)
    {
        var status = await client.GetPilotAsync(device.IpAddress, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (status?.State is not bool currentState)
        {
            return false;
        }

        return await client.SetPilotAsync(device.IpAddress, new { state = !currentState }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
