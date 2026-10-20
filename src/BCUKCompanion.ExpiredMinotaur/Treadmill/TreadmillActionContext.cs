using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>
/// The <see cref="IEventActionContext"/> ExpiredMinotaur's treadmill actions resolve to reach
/// the live <see cref="TreadmillClient"/> without Core knowing the type. Unlike Wiz, there's
/// exactly one treadmill, so this has no device list to expose.
/// </summary>
public sealed class TreadmillActionContext(TreadmillClient client) : IEventActionContext
{
    /// <summary>The live treadmill client the actions send their commands through.</summary>
    public TreadmillClient Client { get; } = client;

    /// <summary>Returns this context for <see cref="TreadmillActionContext"/>, and null for any other type.</summary>
    /// <param name="serviceType">The type being asked for.</param>
    public object? GetService(Type serviceType) => serviceType == typeof(TreadmillActionContext) ? this : null;
}
