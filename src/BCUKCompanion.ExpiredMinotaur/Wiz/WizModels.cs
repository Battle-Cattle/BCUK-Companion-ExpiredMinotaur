using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

public enum WizDeviceType
{
    Light,
    Plug,
}

public sealed record WizDevice(
    Guid Id,
    string Name,
    string IpAddress,
    WizDeviceType DeviceType,
    bool SupportsColor,
    bool SupportsColorTemperature,
    bool SupportsDimming)
{
    public override string ToString() => $"{Name} ({IpAddress}) - {DeviceType}";
}

public sealed record WizConfig : IEventActionMappingsConfig
{
    public List<WizDevice> Devices { get; init; } = [];
    public List<EventActionMapping> Mappings { get; init; } = [];
}
