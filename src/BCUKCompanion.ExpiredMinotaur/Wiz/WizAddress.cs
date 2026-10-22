using System.Net;
using System.Net.Sockets;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

/// <summary>Checks a WiZ device's IP address as the user typed it or as actions-config.json stores it.</summary>
internal static class WizAddress
{
    /// <summary>
    /// Whether <paramref name="ipAddress"/> is a usable device address: a full dotted quad such as
    /// <c>192.168.1.20</c>, or an IPv6 address. <see cref="IPAddress.TryParse(string?, out IPAddress?)"/>
    /// also accepts inet_aton shorthand (<c>192.168.1</c> becomes <c>192.168.0.1</c>), so a typo
    /// would silently point at another host; an IPv4 address only counts when it's written exactly
    /// as it parses back.
    /// </summary>
    /// <param name="ipAddress">The address as text; may be null.</param>
    /// <returns>True for a canonical dotted-quad IPv4 address or any IPv6 address; otherwise false.</returns>
    public static bool IsValid(string? ipAddress) =>
        IPAddress.TryParse(ipAddress, out var address)
        && (address.AddressFamily != AddressFamily.InterNetwork || address.ToString() == ipAddress);
}
