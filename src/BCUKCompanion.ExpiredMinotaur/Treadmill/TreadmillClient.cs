using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>
/// Owns the BLE connection lifecycle and keep-alive loop for a single treadmill, wrapping
/// <see cref="FtmsDevice"/> + <see cref="ControlPointService"/> into one always-there object
/// with no WPF dependency (matches <c>WizClient</c>, which is also UI-framework-agnostic).
/// </summary>
public sealed class TreadmillClient : IDisposable
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FirstReadingTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(2);

    // Guards every Control Point write (Nudge and keep-alive both go through it). A
    // redemption-triggered Nudge can land at any moment concurrent with the keep-alive tick;
    // without serialization, two overlapping SetTargetSpeed writes could race on which BLE
    // indication resolves which caller's awaiter.
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly System.Threading.Timer keepAliveTimer;

    // Not readonly: DisconnectAsync() swaps in a fresh instance so a later Connect can
    // scan/re-pair rather than reusing a torn-down device, matching the test app's Disconnect button.
    private FtmsDevice device = new();
    private ControlPointService? controlPointService;
    private TaskCompletionSource<double>? firstSpeedReading;

    /// <summary>
    /// Fires on whatever thread triggered it (a BLE callback thread, or the keep-alive
    /// timer's thread-pool thread) — any WPF subscriber must marshal via Dispatcher.Invoke
    /// itself, same pattern the test app already uses.
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    public bool IsConnected { get; private set; }
    public double MinSpeedKmh { get; private set; } = FtmsConstants.MinSpeedKmh;
    public double MaxSpeedKmh { get; private set; } = FtmsConstants.MaxSpeedKmh;
    public double SpeedStepKmh { get; private set; } = FtmsConstants.SpeedStepKmh;
    public double TargetSpeedKmh { get; private set; }
    public double CurrentSpeedKmh { get; private set; }

    public TreadmillClient()
    {
        keepAliveTimer = new System.Threading.Timer(_ => _ = SendKeepAliveAsync(), null, Timeout.Infinite, Timeout.Infinite);
        AttachDeviceEvents(device);
    }

    private void AttachDeviceEvents(FtmsDevice attachedDevice)
    {
        attachedDevice.TreadmillDataReceived += (_, data) =>
        {
            try
            {
                var snapshot = TreadmillDataParser.Parse(data);
                if (snapshot.InstantaneousSpeedKmh is { } speed)
                {
                    CurrentSpeedKmh = speed;
                    firstSpeedReading?.TrySetResult(speed);
                }
            }
            catch (Exception ex)
            {
                RaiseStatus($"Treadmill Data parse error: {ex.Message}");
            }
        };
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        RaiseStatus("Scanning...");
        var address = await FtmsDevice.FindFitnessMachineAddressAsync(ScanTimeout).ConfigureAwait(false);
        if (address is null)
        {
            RaiseStatus("No fitness machine found. Is it powered on and in range?");
            return false;
        }

        RaiseStatus("Connecting...");
        var connected = await device.ConnectAsync(address.Value).ConfigureAwait(false);
        if (!connected)
        {
            RaiseStatus("Connection failed.");
            return false;
        }

        // Subscription for Treadmill Data notifications is wired up inside device.ConnectAsync
        // above, so create this before anything else that awaits, in case a notification
        // arrives while RequestControl/ReadSupportedSpeedRange are still in flight.
        firstSpeedReading = new TaskCompletionSource<double>();

        controlPointService = new ControlPointService(device);

        // Supported speed range varies by how this treadmill is set up, so read it fresh on
        // every connect instead of trusting the hardcoded defaults — falls back to those
        // defaults if the read fails.
        var speedRange = await device.ReadSupportedSpeedRangeAsync().ConfigureAwait(false);
        if (speedRange is { } range)
        {
            MinSpeedKmh = range.MinKmh;
            MaxSpeedKmh = range.MaxKmh;
            SpeedStepKmh = range.IncrementKmh;
            controlPointService.MinSpeedKmh = range.MinKmh;
            controlPointService.MaxSpeedKmh = range.MaxKmh;
        }
        else
        {
            RaiseStatus($"Could not read Supported Speed Range (0x2AD4) — using defaults {MinSpeedKmh:0.0}-{MaxSpeedKmh:0.0} km/h.");
        }

        RaiseStatus("Requesting control...");
        var controlResult = await controlPointService.RequestControlAsync().ConfigureAwait(false);
        if (!controlResult.Accepted)
        {
            RaiseStatus($"Connected, but control request failed: {controlResult}");
        }

        // The belt is expected to already be moving when Connect is clicked — starting the
        // keep-alive loop from a hardcoded minimum would immediately drag a live, running
        // belt down to minimum the moment the app connects. Seed TargetSpeedKmh from the
        // belt's actual current speed instead, waiting briefly for the first notification.
        var readingTask = firstSpeedReading.Task;
        var completed = await Task.WhenAny(readingTask, Task.Delay(FirstReadingTimeout, cancellationToken)).ConfigureAwait(false);
        if (completed == readingTask)
        {
            // The belt may genuinely be stopped (reporting 0 km/h) despite the "already
            // running" assumption — clamp into range so keep-alive never sends a target
            // speed below the device's minimum, which SetTargetSpeedAsync rejects.
            var reading = readingTask.Result;
            TargetSpeedKmh = SnapAndClamp(reading);
            if (reading < MinSpeedKmh)
            {
                RaiseStatus($"Belt reports {reading:0.0} km/h (stopped) — starting keep-alive at the minimum, {MinSpeedKmh:0.0} km/h.");
            }
        }
        else
        {
            TargetSpeedKmh = MinSpeedKmh;
            RaiseStatus("No speed reading within 3s of connecting — the first Nudge may change speed unexpectedly.");
        }
        firstSpeedReading = null;

        IsConnected = true;
        keepAliveTimer.Change(KeepAliveInterval, KeepAliveInterval);
        RaiseStatus(device.LastConnectWarnings.Count > 0
            ? $"Connected. WARNING: {string.Join(" | ", device.LastConnectWarnings)}"
            : "Connected.");
        return true;
    }

    /// <summary>
    /// Tears down the BLE connection and swaps in a fresh <see cref="FtmsDevice"/> so a
    /// later Connect can scan and re-read setup-dependent state instead of reusing a
    /// torn-down instance, exactly like the test app's Disconnect button.
    /// </summary>
    public async Task DisconnectAsync()
    {
        keepAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);

        // Wait for any write already in flight (Nudge or keep-alive) to release the lock,
        // so we never dispose the FtmsDevice a concurrent write is still using. Async so a
        // WPF click handler awaiting this doesn't freeze the UI for the write's duration.
        await writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            controlPointService = null;
            IsConnected = false;
            CurrentSpeedKmh = 0;

            device.Dispose();
            device = new FtmsDevice();
            AttachDeviceEvents(device);
        }
        finally
        {
            writeLock.Release();
        }

        RaiseStatus("Disconnected.");
    }

    /// <summary>
    /// Clamps <see cref="TargetSpeedKmh"/> + <paramref name="deltaKmh"/> into
    /// [<see cref="MinSpeedKmh"/>, <see cref="MaxSpeedKmh"/>], snaps to
    /// <see cref="SpeedStepKmh"/>, sends SetTargetSpeed, and only updates the stored target
    /// if the write was accepted. Deliberately never sends Start — if the treadmill isn't
    /// already moving, nudging simply won't move a stopped belt.
    /// </summary>
    public async Task<bool> NudgeSpeedAsync(double deltaKmh, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || controlPointService is null)
        {
            return false;
        }

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapped = SnapAndClamp(TargetSpeedKmh + deltaKmh);
            var result = await controlPointService.SetTargetSpeedAsync(snapped).ConfigureAwait(false);
            if (result.Accepted)
            {
                TargetSpeedKmh = snapped;
            }
            else
            {
                RaiseStatus($"Nudge failed: {result}");
            }
            return result.Accepted;
        }
        catch (Exception ex)
        {
            RaiseStatus($"Nudge write failed: {ex.Message}");
            return false;
        }
        finally
        {
            writeLock.Release();
        }
    }

    private double SnapAndClamp(double speedKmh)
    {
        var snapped = Math.Round(speedKmh / SpeedStepKmh) * SpeedStepKmh;
        return Math.Clamp(snapped, MinSpeedKmh, MaxSpeedKmh);
    }

    // Proven load-bearing in the test app, not just watchdog avoidance: a single
    // SetTargetSpeed write doesn't reliably stick without reinforcement.
    private async Task SendKeepAliveAsync()
    {
        if (!IsConnected || controlPointService is null)
        {
            return;
        }

        await writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await controlPointService.SetTargetSpeedAsync(TargetSpeedKmh).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaiseStatus($"Keep-alive write failed: {ex.Message}");
        }
        finally
        {
            writeLock.Release();
        }
    }

    private void RaiseStatus(string message) => StatusChanged?.Invoke(this, message);

    public void Dispose()
    {
        keepAliveTimer.Dispose();

        // Same race DisconnectAsync() guards against: without the lock, a concurrent
        // NudgeSpeedAsync/SendKeepAliveAsync could Release() a SemaphoreSlim we've
        // already disposed, throwing ObjectDisposedException out of their finally block.
        writeLock.Wait();
        try
        {
            device.Dispose();
        }
        finally
        {
            writeLock.Release();
        }
        writeLock.Dispose();
    }
}
