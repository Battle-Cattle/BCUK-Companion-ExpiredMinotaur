using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Tests;

/// <summary>A context that provides no services, as when an integration's context is missing.</summary>
internal sealed class EmptyActionContext : IEventActionContext
{
    public object? GetService(Type serviceType) => null;
}
