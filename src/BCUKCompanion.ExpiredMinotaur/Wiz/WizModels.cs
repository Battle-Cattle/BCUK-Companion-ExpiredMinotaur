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
