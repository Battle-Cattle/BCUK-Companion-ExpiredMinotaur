using System.Net;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

/// <summary>Rules for the configured list of WiZ devices, kept out of the settings window so they can be tested.</summary>
internal static class WizDeviceList
{
    /// <summary>Status line shown when an added or edited device's address is already in use.</summary>
    public const string DuplicateIpMessage = "A device with that IP address already exists.";

    /// <summary>The status line after a device was edited.</summary>
    /// <param name="removedActionCount">How many of its actions the edit made invalid and removed.</param>
    /// <returns>"Updated device.", plus how many actions were removed when there were any.</returns>
    public static string UpdatedMessage(int removedActionCount) =>
        removedActionCount > 0
            ? $"Updated device and removed {removedActionCount} action(s) no longer valid for it."
            : "Updated device.";

    /// <summary>The status line after a device was removed.</summary>
    /// <param name="removedActionCount">How many actions targeted it and were removed with it.</param>
    /// <returns>"Removed device.", plus how many actions were removed when there were any.</returns>
    public static string RemovedMessage(int removedActionCount) =>
        removedActionCount > 0
            ? $"Removed device and {removedActionCount} action(s) that referenced it."
            : "Removed device.";

    /// <summary>
    /// Whether another device in <paramref name="devices"/> already uses <paramref name="ipAddress"/>.
    /// Addresses that both parse are compared as addresses, so two spellings of the same IPv6
    /// address match; anything else is compared as text, ignoring case.
    /// </summary>
    /// <param name="devices">The configured devices.</param>
    /// <param name="ipAddress">The address being added or edited.</param>
    /// <param name="exceptId">The device being edited, which may keep its own address; <see langword="null"/> when adding.</param>
    /// <returns>True if a device other than <paramref name="exceptId"/> has the same address.</returns>
    public static bool IsDuplicateIp(IEnumerable<WizDevice> devices, string ipAddress, Guid? exceptId = null) =>
        devices.Any(d => d.Id != exceptId && SameAddress(d.IpAddress, ipAddress));

    /// <summary>Whether two device addresses point at the same host.</summary>
    /// <param name="a">One address as stored or typed.</param>
    /// <param name="b">The other address as stored or typed.</param>
    /// <returns>True when both parse to equal addresses, or (if either doesn't parse) the texts match ignoring case.</returns>
    private static bool SameAddress(string a, string b) =>
        IPAddress.TryParse(a, out var left) && IPAddress.TryParse(b, out var right)
            ? left.Equals(right)
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
