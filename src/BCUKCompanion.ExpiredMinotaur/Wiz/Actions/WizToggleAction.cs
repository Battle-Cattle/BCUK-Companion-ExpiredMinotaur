using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

public sealed class WizToggleAction : WizDeviceActionBase
{
    public const string ActionKind = "wiz.toggle";

    [JsonIgnore]
    public override string Kind => ActionKind;

    protected override bool SupportsPlugs => true;

    protected override string DescribeAction() => "Toggle";

    protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device) => [];

    // Overrides SendAsync rather than BuildPayloadAsync: an offline device or one that omits
    // its "state" field is an ordinary dispatch failure here (matching every other Wiz action,
    // which reports false rather than throwing), not an exceptional condition that should
    // surface as "Action dispatch crashed".
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
