using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Wiz;

namespace BCUKCompanion.ExpiredMinotaur;

/// <summary>
/// Combines the Wiz and Treadmill <see cref="IEventActionContext"/>s so a single
/// <see cref="EventActionMapping"/> can freely mix actions from both integrations: each
/// action's <c>GetService&lt;T&gt;()</c> call resolves to whichever integration-specific
/// context it asks for, without either integration's action types knowing about the other.
/// </summary>
public sealed class ActionsContext(WizActionContext wiz, TreadmillActionContext treadmill) : IEventActionContext
{
    public object? GetService(Type serviceType) => wiz.GetService(serviceType) ?? treadmill.GetService(serviceType);
}
