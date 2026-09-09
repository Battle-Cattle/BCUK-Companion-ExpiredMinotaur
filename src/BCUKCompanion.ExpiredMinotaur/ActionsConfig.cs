using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;

namespace BCUKCompanion.ExpiredMinotaur;

/// <summary>
/// The single on-disk config for every integration: the Wiz device list plus one shared list of
/// <see cref="EventActionMapping"/>, so a mapping's actions can mix Wiz and Treadmill (and any
/// future integration's) kinds instead of each integration keeping its own separate mappings.
/// </summary>
public sealed record ActionsConfig : IEventActionMappingsConfig
{
    public List<WizDevice> Devices { get; init; } = [];
    public List<EventActionMapping> Mappings { get; init; } = [];
}
