using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>
/// Holds <see cref="TreadmillClient"/>'s current device and swaps in a fresh one after a disconnect, so a
/// later connect scans and re-reads setup-dependent state instead of reusing a torn-down device (a
/// disposed <see cref="FtmsDevice"/> can't reconnect, and has its events torn down). Not thread-safe:
/// the client only swaps under its write lock.
/// </summary>
internal sealed class TreadmillDeviceSlot
{
    private readonly Func<ITreadmillDevice> createDevice;
    private readonly Action<ITreadmillDevice> attachEvents;

    /// <summary>Creates the slot, and its first device.</summary>
    /// <param name="createDevice">Creates a fresh, unconnected device; called now and on every <see cref="Replace"/>.</param>
    /// <param name="attachEvents">Subscribes to a newly created device's events.</param>
    public TreadmillDeviceSlot(Func<ITreadmillDevice> createDevice, Action<ITreadmillDevice> attachEvents)
    {
        this.createDevice = createDevice;
        this.attachEvents = attachEvents;
        Current = CreateAttached();
    }

    /// <summary>Gets the device the client currently uses.</summary>
    public ITreadmillDevice Current { get; private set; }

    /// <summary>Disposes the current device, dropping any connection it has, and swaps in a fresh one.</summary>
    public void Replace()
    {
        Current.Dispose();
        Current = CreateAttached();
    }

    /// <summary>Creates a fresh device and subscribes to its events.</summary>
    /// <returns>The new, unconnected device.</returns>
    private ITreadmillDevice CreateAttached()
    {
        var device = createDevice();
        attachEvents(device);
        return device;
    }
}
