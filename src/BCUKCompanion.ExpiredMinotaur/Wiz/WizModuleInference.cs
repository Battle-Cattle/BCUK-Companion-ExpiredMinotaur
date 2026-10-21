namespace BCUKCompanion.ExpiredMinotaur.Wiz;

/// <summary>
/// Guesses a discovered device's type and capabilities from its reported module name (e.g.
/// <c>ESP25_SHRGB_01</c>, <c>ESP10_SOCKET_06</c>), so devices found by discovery can be added
/// without the user filling the details in by hand.
/// </summary>
internal static class WizModuleInference
{
    /// <summary>A module name containing "SOCKET" is a plug; anything else is a light.</summary>
    /// <param name="moduleName">The module name the device reported.</param>
    /// <returns><see cref="WizDeviceType.Plug"/> for a socket module; otherwise <see cref="WizDeviceType.Light"/>.</returns>
    public static WizDeviceType InferDeviceType(string moduleName) =>
        IsSocket(moduleName) ? WizDeviceType.Plug : WizDeviceType.Light;

    /// <summary>
    /// A plug supports nothing beyond on/off. A light supports dimming; colour if its module name
    /// contains "RGB", and colour temperature if it supports colour or its name contains "TW"
    /// (tunable white).
    /// </summary>
    /// <param name="moduleName">The module name the device reported.</param>
    /// <returns>Whether the device supports colour, colour temperature and dimming; all false for a plug.</returns>
    public static (bool Color, bool ColorTemp, bool Dimming) InferCapabilities(string moduleName)
    {
        if (IsSocket(moduleName))
        {
            return (false, false, false);
        }

        var supportsColor = moduleName.Contains("RGB", StringComparison.OrdinalIgnoreCase);
        var supportsColorTemp = supportsColor || moduleName.Contains("TW", StringComparison.OrdinalIgnoreCase);
        return (supportsColor, supportsColorTemp, true);
    }

    /// <summary>Builds a new device entry for a discovered device, named after its module.</summary>
    /// <param name="discovered">The device that answered discovery.</param>
    /// <param name="id">The new entry's ID.</param>
    /// <returns>A device named after its module, at its discovered address, with the inferred type and capabilities.</returns>
    public static WizDevice ToDevice(WizDiscoveredDevice discovered, Guid id)
    {
        var (color, colorTemp, dimming) = InferCapabilities(discovered.ModuleName);
        return new WizDevice(
            id,
            discovered.ModuleName,
            discovered.IpAddress,
            InferDeviceType(discovered.ModuleName),
            color,
            colorTemp,
            dimming);
    }

    /// <summary>Whether the module name identifies a smart plug.</summary>
    /// <param name="moduleName">The module name the device reported.</param>
    /// <returns>True if the name contains "SOCKET" (case-insensitive); otherwise false.</returns>
    private static bool IsSocket(string moduleName) =>
        moduleName.Contains("SOCKET", StringComparison.OrdinalIgnoreCase);
}
