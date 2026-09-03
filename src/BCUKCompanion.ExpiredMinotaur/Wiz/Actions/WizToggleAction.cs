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

    protected override async Task<object> BuildPayloadAsync(WizClient client, WizDevice device, CancellationToken cancellationToken)
    {
        var status = await client.GetPilotAsync(device.IpAddress, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (status is null)
        {
            throw new InvalidOperationException("Device did not respond to status query.");
        }

        if (status.State is not bool currentState)
        {
            throw new InvalidOperationException("Device did not report its current on/off state; cannot toggle.");
        }

        return new { state = !currentState };
    }
}
