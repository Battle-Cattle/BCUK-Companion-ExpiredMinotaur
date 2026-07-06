using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

/// <summary>
/// The <see cref="IEventActionContext"/> ExpiredMinotaur's Wiz actions resolve to reach the
/// live <see cref="WizClient"/> and the current device list without Core knowing either type.
/// </summary>
public sealed class WizActionContext(WizClient client, IReadOnlyList<WizDevice> devices) : IEventActionContext
{
    public WizClient Client { get; } = client;

    public WizDevice? ResolveDevice(Guid deviceId) => devices.FirstOrDefault(d => d.Id == deviceId);

    public object? GetService(Type serviceType) => serviceType == typeof(WizActionContext) ? this : null;
}
