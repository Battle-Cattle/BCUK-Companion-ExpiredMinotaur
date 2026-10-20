namespace BCUKCompanion.ExpiredMinotaur.Wiz;

/// <summary>What kind of WiZ device an entry is, which decides which actions it supports.</summary>
public enum WizDeviceType
{
    /// <summary>A bulb or light strip: on/off, plus whatever of dimming, colour and colour temperature it supports.</summary>
    Light,

    /// <summary>A smart plug: on/off only.</summary>
    Plug,
}

/// <summary>A WiZ device in the device list, as stored in actions-config.json.</summary>
/// <param name="Id">Identifies the device; actions refer to it by this, so renaming or moving it keeps them working.</param>
/// <param name="Name">The user's name for it.</param>
/// <param name="IpAddress">Its address on the LAN.</param>
/// <param name="DeviceType">Whether it's a light or a plug.</param>
/// <param name="SupportsColor">Whether it can be set to an RGB colour.</param>
/// <param name="SupportsColorTemperature">Whether it can be set to a white colour temperature.</param>
/// <param name="SupportsDimming">Whether its brightness can be set.</param>
public sealed record WizDevice(
    Guid Id,
    string Name,
    string IpAddress,
    WizDeviceType DeviceType,
    bool SupportsColor,
    bool SupportsColorTemperature,
    bool SupportsDimming)
{
    /// <summary>The device as shown in lists, e.g. "Desk lamp (192.168.1.20) - Light".</summary>
    public override string ToString() => $"{Name} ({IpAddress}) - {DeviceType}";
}
