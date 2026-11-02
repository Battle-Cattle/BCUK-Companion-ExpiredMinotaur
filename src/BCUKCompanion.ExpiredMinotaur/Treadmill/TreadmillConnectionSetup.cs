using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>
/// The steps of connecting to the treadmill, for <see cref="TreadmillClient.ConnectAsync"/>: scan for it,
/// connect, read its supported speed range, request control, and seed the target from the belt's first
/// reported speed. Reports progress and problems as status lines. The client holds its write lock around
/// <see cref="RunAsync"/> and decides what a successful return means for its connected state.
/// </summary>
internal sealed class TreadmillConnectionSetup
{
    /// <summary>How long to scan for the treadmill before giving up.</summary>
    internal static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long to wait after connecting for the belt's first speed reading.</summary>
    internal static readonly TimeSpan FirstReadingTimeout = TimeSpan.FromSeconds(3);

    private readonly FitnessMachineScanner scanner;
    private readonly TreadmillSpeedControl speed;
    private readonly TimeProvider timeProvider;
    private readonly Action<string> reportStatus;

    /// <summary>Creates the connect steps over the given parts.</summary>
    /// <param name="scanner">Finds the treadmill's Bluetooth address.</param>
    /// <param name="speed">The client's speed state, which takes the device's range and is seeded with the first reading.</param>
    /// <param name="timeProvider">The clock for the first-reading wait.</param>
    /// <param name="reportStatus">Raises a user-facing status line.</param>
    public TreadmillConnectionSetup(FitnessMachineScanner scanner, TreadmillSpeedControl speed, TimeProvider timeProvider, Action<string> reportStatus)
    {
        this.scanner = scanner;
        this.speed = speed;
        this.timeProvider = timeProvider;
        this.reportStatus = reportStatus;
    }

    /// <summary>
    /// Scans for and connects to the treadmill, reads its setup-dependent state, and seeds
    /// the target speed from its first reported speed. Returns null (having already reported a
    /// status saying why) on any failure or cancellation; if that happens after the device
    /// connected, the caller must drop the connection.
    /// </summary>
    /// <param name="device">The fresh, unconnected device to connect.</param>
    /// <param name="cancellationToken">Cancels the scan or the wait for the first speed reading.</param>
    /// <returns>A task completing with the Control Point service for the connected device; null on failure or cancellation.</returns>
    public async Task<ControlPointService?> RunAsync(ITreadmillDevice device, CancellationToken cancellationToken)
    {
        var address = await FindAddressAsync(cancellationToken).ConfigureAwait(false);
        if (address is null)
        {
            return null;
        }

        // device.ConnectAsync subscribes to Treadmill Data notifications, and the first one can
        // arrive before it returns, so wait for it from before the connect rather than after.
        var firstReading = speed.ExpectFirstReading();
        try
        {
            reportStatus("Connecting...");
            if (!await device.ConnectAsync(address.Value).ConfigureAwait(false))
            {
                reportStatus("Connection failed.");
                return null;
            }

            var controlPointService = new ControlPointService(device);
            await ReadSpeedRangeAsync(device, controlPointService).ConfigureAwait(false);
            await RequestControlAsync(controlPointService).ConfigureAwait(false);
            return await SeedTargetAsync(firstReading, cancellationToken).ConfigureAwait(false) ? controlPointService : null;
        }
        finally
        {
            speed.StopExpectingFirstReading();
        }
    }

    /// <summary>Scans for the treadmill, reporting a status if none is found or the scan is cancelled.</summary>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>A task completing with the treadmill's Bluetooth address; null if none was found or the scan was cancelled.</returns>
    private async Task<ulong?> FindAddressAsync(CancellationToken cancellationToken)
    {
        reportStatus("Scanning...");
        var address = await scanner.FindFitnessMachineAddressAsync(ScanTimeout, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            reportStatus("Connect cancelled.");
            return null;
        }

        if (address is null)
        {
            reportStatus("No fitness machine found. Is it powered on and in range?");
        }

        return address;
    }

    /// <summary>
    /// Reads the treadmill's Supported Speed Range and applies it to the speed state and the Control
    /// Point service. It varies by how this treadmill is set up, so it's read fresh on every connect
    /// instead of trusting the hardcoded defaults, which are used (with a warning) if the read fails.
    /// </summary>
    /// <param name="device">The connected device to read the range from.</param>
    /// <param name="controlPointService">The new connection's Control Point service, which checks targets against the range.</param>
    /// <returns>A task that completes once the range is applied.</returns>
    private async Task ReadSpeedRangeAsync(ITreadmillDevice device, ControlPointService controlPointService)
    {
        // Reset to the hardcoded defaults first: otherwise a reconnect to a different device whose
        // read fails would leave the previous device's values in place instead of the defaults.
        speed.Range = TreadmillSpeedRange.Default;

        var (range, warning) = TreadmillSpeedRange.FromDevice(await device.ReadSupportedSpeedRangeAsync().ConfigureAwait(false));
        speed.Range = range;
        controlPointService.MinSpeedKmh = range.MinKmh;
        controlPointService.MaxSpeedKmh = range.MaxKmh;
        if (warning is not null)
        {
            reportStatus(warning);
        }
    }

    /// <summary>Requests control of the treadmill, reporting a status if it's refused. The connection carries on either way.</summary>
    /// <param name="controlPointService">The new connection's Control Point service.</param>
    /// <returns>A task that completes once the treadmill has answered.</returns>
    private async Task RequestControlAsync(ControlPointService controlPointService)
    {
        reportStatus("Requesting control...");
        var controlResult = await controlPointService.RequestControlAsync().ConfigureAwait(false);
        if (!controlResult.Accepted)
        {
            reportStatus($"Connected, but control request failed: {controlResult}");
        }
    }

    /// <summary>Waits briefly for the belt's first speed reading and seeds the target from it (see <see cref="TreadmillSpeedControl.SeedTarget"/>).</summary>
    /// <param name="firstReading">Completes with the first belt speed reported since connecting began.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task completing with true once the target is seeded; false if the wait was cancelled.</returns>
    private async Task<bool> SeedTargetAsync(Task<double> firstReading, CancellationToken cancellationToken)
    {
        var completed = await Task.WhenAny(firstReading, Task.Delay(FirstReadingTimeout, timeProvider, cancellationToken)).ConfigureAwait(false);

        // Task.Delay(..., cancellationToken) transitions to Canceled (not Faulted) when the
        // token fires, which still makes WhenAny complete — so without this check, a cancelled
        // connect attempt falls into the "no reading yet" branch and goes on to report success.
        if (cancellationToken.IsCancellationRequested)
        {
            reportStatus("Connect cancelled.");
            return false;
        }

        var warning = speed.SeedTarget(completed == firstReading ? firstReading.Result : null);
        if (warning is not null)
        {
            reportStatus(warning);
        }

        return true;
    }
}
