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

    protected override object BuildPayload() => throw new NotSupportedException("Toggle overrides SendAsync instead.");

    protected override async Task<bool> SendAsync(WizClient client, WizDevice device, CancellationToken cancellationToken)
    {
        var status = await client.GetPilotAsync(device.IpAddress, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (status is null)
        {
            return false;
        }

        return await client.SetPilotAsync(device.IpAddress, new { state = !status.State }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
