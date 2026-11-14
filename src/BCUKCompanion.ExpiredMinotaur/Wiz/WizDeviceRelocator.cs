using System.Diagnostics;
using System.IO;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

/// <summary>
/// Finds a WiZ device again after its IP address changed (a new DHCP lease, a router reboot):
/// runs a short discovery, looks for the device's MAC address, and saves the new address to
/// actions-config.json so later actions go straight there.
/// </summary>
public sealed class WizDeviceRelocator
{
    /// <summary>How long to listen for the device; short, since an action is waiting on it.</summary>
    internal static readonly TimeSpan SearchWindow = TimeSpan.FromSeconds(2);

    private readonly Func<TimeSpan, CancellationToken, IAsyncEnumerable<WizDiscoveredDevice>> discover;
    private readonly ActionsConfigStore store;

    /// <summary>Creates a relocator that discovers with <paramref name="discover"/> and saves to <paramref name="store"/>.</summary>
    /// <param name="discover">Runs discovery for the given window, yielding each device that answers (normally <see cref="WizClient.DiscoverAsync"/>).</param>
    /// <param name="store">Where the device list is saved.</param>
    public WizDeviceRelocator(
        Func<TimeSpan, CancellationToken, IAsyncEnumerable<WizDiscoveredDevice>> discover, ActionsConfigStore store)
    {
        this.discover = discover;
        this.store = store;
    }

    /// <summary>Looks for <paramref name="device"/> at a new address, by its MAC address.</summary>
    /// <param name="device">The device that didn't answer at its saved address.</param>
    /// <param name="cancellationToken">Ends the search early.</param>
    /// <returns>
    /// A task whose result is the device at its new address, already saved; or null when it has no
    /// MAC address, wasn't found, or answered from the address it already had.
    /// </returns>
    public async Task<WizDevice?> RelocateAsync(WizDevice device, CancellationToken cancellationToken)
    {
        if (device.MacAddress is not { } mac
            || await FindAsync(mac, cancellationToken).ConfigureAwait(false) is not { } address
            || address == device.IpAddress)
        {
            return null;
        }

        var moved = device with { IpAddress = address };
        Save(moved);
        return moved;
    }

    /// <summary>Runs discovery until a device with <paramref name="mac"/> answers.</summary>
    /// <param name="mac">The MAC address to look for.</param>
    /// <param name="cancellationToken">Ends the search early.</param>
    /// <returns>A task whose result is the address it answered from, or null when it didn't answer within <see cref="SearchWindow"/>.</returns>
    private async Task<string?> FindAsync(string mac, CancellationToken cancellationToken)
    {
        await foreach (var found in discover(SearchWindow, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (WizDeviceList.SameMac(found.MacAddress, mac))
            {
                return found.IpAddress;
            }
        }

        return null;
    }

    /// <summary>Saves <paramref name="moved"/>'s new address, best-effort: the action uses it either way.</summary>
    /// <param name="moved">The device at its new address.</param>
    private void Save(WizDevice moved)
    {
        try
        {
            var config = store.Load();
            var index = config.Devices.FindIndex(d => d.Id == moved.Id);
            if (index >= 0)
            {
                config.Devices[index] = config.Devices[index] with { IpAddress = moved.IpAddress };
                store.Save(config);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't save Wiz device '{moved.Name}' at its new address {moved.IpAddress}: {ex.Message}");
        }
    }
}
