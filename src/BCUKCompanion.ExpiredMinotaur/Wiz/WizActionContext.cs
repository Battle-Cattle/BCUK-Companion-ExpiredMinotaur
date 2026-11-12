using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

/// <summary>
/// The <see cref="IEventActionContext"/> ExpiredMinotaur's Wiz actions resolve to reach the
/// live <see cref="WizClient"/> and the current device list without Core knowing either type.
/// </summary>
/// <param name="client">The live client the Wiz actions send their commands through.</param>
/// <param name="devices">The configured Wiz devices the actions can target.</param>
public sealed class WizActionContext(WizClient client, IReadOnlyList<WizDevice> devices) : IEventActionContext
{
    /// <summary>The client the actions send their commands through.</summary>
    public WizClient Client { get; } = client;

    /// <summary>Finds a device in the current list by ID.</summary>
    /// <param name="deviceId">The device's <see cref="WizDevice.Id"/>.</param>
    /// <returns>The device, or null if it's been removed.</returns>
    public WizDevice? ResolveDevice(Guid deviceId) => devices.FirstOrDefault(d => d.Id == deviceId);

    /// <summary>Returns this context for <see cref="WizActionContext"/>, and null for any other type.</summary>
    /// <param name="serviceType">The type being asked for.</param>
    /// <returns>This context when <paramref name="serviceType"/> is <see cref="WizActionContext"/>; otherwise null.</returns>
    public object? GetService(Type serviceType) => serviceType == typeof(WizActionContext) ? this : null;
}
