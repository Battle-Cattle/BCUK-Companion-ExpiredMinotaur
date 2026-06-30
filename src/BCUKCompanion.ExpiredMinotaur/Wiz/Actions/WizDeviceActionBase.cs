using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>
/// Shared device-resolution, plug-compatibility, and dispatch boilerplate for the Wiz action
/// kinds that target a specific device (everything except <see cref="DelayAction"/>).
/// </summary>
public abstract class WizDeviceActionBase : IEventAction
{
    public Guid DeviceId { get; set; }

    public abstract string Kind { get; }

    protected abstract bool SupportsPlugs { get; }

    public string Describe(IEventActionContext context)
    {
        var device = context.GetService<WizActionContext>()?.ResolveDevice(DeviceId);
        return $"{device?.Name ?? "(unknown device)"}: {DescribeAction()}";
    }

    public IReadOnlyList<string> Validate(IEventActionContext context)
    {
        var device = context.GetService<WizActionContext>()?.ResolveDevice(DeviceId);
        if (device is null)
        {
            return ["Device not found."];
        }

        var errors = new List<string>();
        if (device.DeviceType == WizDeviceType.Plug && !SupportsPlugs)
        {
            errors.Add($"Plugs do not support {Kind}.");
        }

        errors.AddRange(ValidateDeviceSpecific(device));
        return errors;
    }

    public async Task<bool> ExecuteAsync(IEventActionContext context, CancellationToken cancellationToken)
    {
        var wiz = context.GetService<WizActionContext>()
            ?? throw new InvalidOperationException("Wiz action context not available.");
        var device = wiz.ResolveDevice(DeviceId)
            ?? throw new InvalidOperationException("Device not found.");

        return await SendAsync(wiz.Client, device, cancellationToken).ConfigureAwait(false);
    }

    protected abstract string DescribeAction();

    protected abstract IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device);

    protected abstract object BuildPayload();

    protected static List<string> ValidateCapabilityAndRange(
        WizDevice device, bool supported, string unsupportedMessage,
        int value, int min, int max, string outOfRangeMessage)
    {
        var errors = new List<string>();
        if (!supported) errors.Add(unsupportedMessage);
        if (value < min || value > max) errors.Add(outOfRangeMessage);
        return errors;
    }

    protected virtual async Task<bool> SendAsync(WizClient client, WizDevice device, CancellationToken cancellationToken)
        => await client.SetPilotAsync(device.IpAddress, BuildPayload(), cancellationToken: cancellationToken).ConfigureAwait(false);
}
