using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.Actions;

/// <summary>
/// Shared device-resolution, plug-compatibility, and dispatch boilerplate for the Wiz action
/// kinds that target a specific device (everything except <see cref="DelayAction"/>).
/// </summary>
public abstract class WizDeviceActionBase : IEventAction
{
    /// <summary>The <see cref="WizDevice.Id"/> of the device the action targets.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>The action's kind, as stored in actions-config.json.</summary>
    public abstract string Kind { get; }

    /// <summary>Whether the action also works on plugs, not just lights.</summary>
    protected abstract bool SupportsPlugs { get; }

    /// <summary>Describes the action for the mappings list, e.g. "Desk lamp: TurnOn".</summary>
    /// <param name="context">Provides the device list, to show the device's name.</param>
    public string Describe(IEventActionContext context)
    {
        var device = context.GetService<WizActionContext>()?.ResolveDevice(DeviceId);
        return $"{device?.Name ?? "(unknown device)"}: {DescribeAction()}";
    }

    /// <summary>
    /// Checks the device still exists, is a type the action supports, and supports what the action
    /// sets (see <see cref="ValidateDeviceSpecific"/>).
    /// </summary>
    /// <param name="context">Provides the device list.</param>
    /// <returns>Every problem found; empty when the action is valid.</returns>
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

    /// <summary>Sends the action to its device.</summary>
    /// <param name="context">Must provide a <see cref="WizActionContext"/>.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>Whether the device accepted the command.</returns>
    /// <exception cref="InvalidOperationException">The context has no Wiz context, or the device no longer exists.</exception>
    public async Task<bool> ExecuteAsync(IEventActionContext context, CancellationToken cancellationToken)
    {
        var wiz = context.GetService<WizActionContext>()
            ?? throw new InvalidOperationException("Wiz action context not available.");
        var device = wiz.ResolveDevice(DeviceId)
            ?? throw new InvalidOperationException("Device not found.");

        return await SendAsync(wiz.Client, device, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The action-specific part of <see cref="Describe"/>, e.g. "SetBrightness 50%".</summary>
    protected abstract string DescribeAction();

    /// <summary>Checks the device supports what this action sets, and that its values are in range.</summary>
    /// <param name="device">The target device.</param>
    /// <returns>Every problem found; empty when there are none.</returns>
    protected abstract IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device);

    /// <summary>The <c>setPilot</c> params to send. Override this or <see cref="BuildPayloadAsync"/>.</summary>
    protected virtual object BuildPayload()
        => throw new NotSupportedException($"{GetType().Name} must override {nameof(BuildPayload)} or {nameof(BuildPayloadAsync)}.");

    /// <summary>The <c>setPilot</c> params to send, for actions that need to ask the device first; defaults to <see cref="BuildPayload"/>.</summary>
    /// <param name="client">The client to query the device with.</param>
    /// <param name="device">The target device.</param>
    /// <param name="cancellationToken">Cancels any query.</param>
    protected virtual Task<object> BuildPayloadAsync(WizClient client, WizDevice device, CancellationToken cancellationToken)
        => Task.FromResult(BuildPayload());

    /// <summary>Builds the payload and sends it with <c>setPilot</c>.</summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="device">The target device.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>Whether the device accepted the command.</returns>
    protected virtual async Task<bool> SendAsync(WizClient client, WizDevice device, CancellationToken cancellationToken)
    {
        var payload = await BuildPayloadAsync(client, device, cancellationToken).ConfigureAwait(false);
        return await client.SetPilotAsync(device.IpAddress, payload, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
