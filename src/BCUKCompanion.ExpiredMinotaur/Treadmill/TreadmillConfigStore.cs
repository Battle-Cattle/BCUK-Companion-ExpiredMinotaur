using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

public sealed record TreadmillConfig
{
    public List<EventActionMapping> Mappings { get; init; } = [];
}

public sealed class TreadmillConfigStore(string dataFolderName, EventActionTypeRegistry registry)
    : EventActionConfigStore<TreadmillConfig>(dataFolderName, "treadmill-config.json", registry);
