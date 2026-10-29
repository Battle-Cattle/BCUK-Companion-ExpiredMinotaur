namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

/// <summary>Scans BLE advertisements for the first device offering the Fitness Machine service (0x1826).</summary>
public sealed class FitnessMachineScanner
{
    private readonly Func<IAdvertisementWatcher> createWatcher;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates a scanner that uses Windows' BLE advertisement watcher.</summary>
    public FitnessMachineScanner()
        : this(() => new WinRtAdvertisementWatcher(), TimeProvider.System)
    {
    }

    /// <summary>Creates a scanner over another watcher and clock, so tests can stand in for the radio.</summary>
    /// <param name="createWatcher">Creates a fresh watcher for each scan.</param>
    /// <param name="timeProvider">The clock the scan's timeout runs on.</param>
    internal FitnessMachineScanner(Func<IAdvertisementWatcher> createWatcher, TimeProvider timeProvider)
    {
        this.createWatcher = createWatcher;
        this.timeProvider = timeProvider;
    }

    /// <summary>
    /// Scans for BLE advertisements and returns the address of the first device advertising the
    /// Fitness Machine service, or null if none is found before <paramref name="timeout"/> or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="timeout">How long to scan before giving up.</param>
    /// <param name="cancellationToken">Stops the scan early, completing with null.</param>
    /// <returns>A task completing with the treadmill's 48-bit Bluetooth address, or null if none was found.</returns>
    public Task<ulong?> FindFitnessMachineAddressAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<ulong?>();
        var watcher = createWatcher();

        // The Received callback and the timeout continuation can both observe an
        // incomplete tcs at the same time (the callback runs on a background
        // advertisement-watcher thread, the timeout on the thread pool), so a plain
        // `if (!tcs.Task.IsCompleted)` guard on either side is a race: a device found
        // right at the timeout boundary could have its address discarded in favor of a
        // `null` result. This gate makes "who gets to stop the watcher, detach the
        // handler, and complete tcs" atomic — only the first caller to flip it wins.
        var completionGate = 0;

        EventHandler<BleAdvertisement>? handler = null;

        // Found, timed out or cancelled: whichever comes first stops the watcher and sets the result.
        void Finish(ulong? address)
        {
            if (Interlocked.CompareExchange(ref completionGate, 1, 0) == 0)
            {
                watcher.Stop();
                watcher.Received -= handler;
                tcs.TrySetResult(address);
            }
        }

        handler = (_, advertisement) =>
        {
            if (advertisement.ServiceUuids.Contains(FtmsConstants.FitnessMachineService))
            {
                Finish(advertisement.BluetoothAddress);
            }
        };
        watcher.Received += handler;

        watcher.Start();

        var cancellationRegistration = cancellationToken.Register(() => Finish(null));
        _ = Task.Delay(timeout, timeProvider).ContinueWith(_ => Finish(null), TaskScheduler.Default);
        _ = tcs.Task.ContinueWith(_ => cancellationRegistration.Dispose(), TaskScheduler.Default);

        return tcs.Task;
    }
}
