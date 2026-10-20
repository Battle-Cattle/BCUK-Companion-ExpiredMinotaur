using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>
/// Owns the BLE connection lifecycle and belt-following loop for a single treadmill, wrapping
/// <see cref="FtmsDevice"/> + <see cref="ControlPointService"/> into one always-there object
/// with no WPF dependency (matches <c>WizClient</c>, which is also UI-framework-agnostic).
/// </summary>
public sealed class TreadmillClient : IDisposable
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FirstReadingTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(2);

    // A speed reading older than this is treated as unknown, so the follow tick holds the target
    // and a nudge starts from it, rather than reacting to a belt speed it can no longer see.
    private static readonly TimeSpan SpeedReadingMaxAge = TimeSpan.FromSeconds(5);

    // Guards every Control Point write (Nudge), the target/follower state the follow tick and
    // Nudge share, AND the full Connect/Disconnect lifecycle. Redemption-triggered Nudges can
    // land at any moment, concurrent with each other and with the follow tick; without
    // serialization, two overlapping SetTargetSpeed writes could race on which BLE indication
    // resolves which caller's awaiter. Connect/Disconnect hold it for their entire body (not just the device
    // swap/dispose) so the two can never interleave: without that, a Disconnect racing a
    // still-in-flight Connect could dispose/replace the very `device`/`controlPointService`
    // Connect is mid-use of, and then Connect's tail would unconditionally set
    // IsConnected = true and restart the follow timer against state Disconnect already
    // tore down. Holding this lock across Connect's BLE scan/connect/read calls is safe
    // rather than deadlock-prone: on an initial connect, NudgeSpeedAsync/FollowBeltSpeedAsync
    // both bail out on their IsConnected/controlPointService check *before* ever waiting on
    // the lock, since IsConnected/controlPointService are only set once Connect already holds
    // it. During a reconnect, or while DisconnectAsync is waiting for the lock, that pre-lock
    // check can still pass (IsConnected/controlPointService haven't been cleared yet) and the
    // caller queues on writeLock — each method re-checks after acquiring it, so a queued
    // caller that loses the race still safely no-ops instead of using torn-down state.
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly System.Threading.Timer followTimer;

    // Interlocked-guarded: 0 = live, 1 = disposed. Set before Dispose() attempts to acquire
    // writeLock, and checked by every other method right after it acquires the lock, so a
    // Connect/Nudge/follow-tick call that was already queued when Dispose() ran never touches
    // `device`/`followTimer` after they've been disposed.
    private int disposed;

    // Not readonly: DisconnectAsync() swaps in a fresh instance so a later Connect can
    // scan/re-pair rather than reusing a torn-down device, matching the test app's Disconnect button.
    private FtmsDevice device = new();
    private ControlPointService? controlPointService;
    private TaskCompletionSource<double>? firstSpeedReading;

    // Follows a speed set on the treadmill's own console, so a nudge starts from it.
    // Only used under writeLock.
    private readonly TreadmillSpeedFollower speedFollower = new();

    // The belt's last reported speed and when it arrived (null = none since connecting). One
    // immutable reading, so the speed and its time are always read together. Written on a BLE
    // callback thread, read under writeLock, hence Volatile.
    private BeltReading? lastSpeedReading;

    /// <summary>
    /// Fires on whatever thread triggered it (a BLE callback thread, or the follow
    /// timer's thread-pool thread) — any WPF subscriber must marshal via Dispatcher.Invoke
    /// itself, same pattern the test app already uses.
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>Whether a treadmill is connected and accepting commands.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>The lowest target speed, in km/h: the device's own once connected, a default before.</summary>
    public double MinSpeedKmh { get; private set; } = FtmsConstants.MinSpeedKmh;

    /// <summary>The highest target speed, in km/h: the device's own once connected, a default before.</summary>
    public double MaxSpeedKmh { get; private set; } = FtmsConstants.MaxSpeedKmh;

    /// <summary>The device's speed step, in km/h; targets are rounded to it.</summary>
    public double SpeedStepKmh { get; private set; } = FtmsConstants.SpeedStepKmh;

    /// <summary>The target speed last sent to (or adopted from) the treadmill, in km/h.</summary>
    public double TargetSpeedKmh { get; private set; }

    /// <summary>The belt speed the treadmill last reported, in km/h.</summary>
    public double CurrentSpeedKmh { get; private set; }

    /// <summary>Creates a disconnected client; call <see cref="ConnectAsync"/> to find and connect to the treadmill.</summary>
    public TreadmillClient()
    {
        followTimer = new System.Threading.Timer(_ => _ = FollowBeltSpeedAsync(), null, Timeout.Infinite, Timeout.Infinite);
        AttachDeviceEvents(device);
    }

    /// <summary>Subscribes to a device's disconnect and speed-reading events.</summary>
    private void AttachDeviceEvents(FtmsDevice attachedDevice)
    {
        // Without this the client kept reporting itself connected after the treadmill was powered
        // off or went out of range: every nudge failed until the user clicked Disconnect then Connect.
        attachedDevice.ConnectionLost += (_, _) => _ = DisconnectAsync(attachedDevice, "Treadmill disconnected (powered off or out of range). Click Connect to reconnect.");

        attachedDevice.TreadmillDataReceived += (_, data) =>
        {
            try
            {
                var snapshot = TreadmillDataParser.Parse(data);
                if (snapshot.InstantaneousSpeedKmh is { } speed)
                {
                    CurrentSpeedKmh = speed;
                    Volatile.Write(ref lastSpeedReading, new BeltReading(speed, DateTime.UtcNow));
                    firstSpeedReading?.TrySetResult(speed);
                }
            }
            catch (Exception ex)
            {
                RaiseStatus($"Treadmill Data parse error: {ex.Message}");
            }
        };
    }

    /// <summary>
    /// Scans for a fitness machine, connects, requests control and starts following the belt.
    /// </summary>
    /// <param name="cancellationToken">Cancels the scan and the wait for the first speed reading.</param>
    /// <returns>True once connected; false if already connected, nothing was found, or it failed.</returns>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        // Held for the entire method — see the writeLock field comment — so a concurrent
        // DisconnectAsync can never dispose/replace `device`/`controlPointService` out from
        // under this call, and this call can never finish by unconditionally declaring itself
        // connected against state a Disconnect already tore down.
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return false;
            }

            if (IsConnected)
            {
                // FtmsDevice.ConnectAsync tears down the active BLE connection (via
                // ReleaseCurrentDevice) before establishing a new one — if that new attempt
                // then failed, this class would be left reporting IsConnected == true against
                // a device that's no longer actually connected. Reject the reconnect outright
                // rather than risk that; callers must Disconnect first.
                RaiseStatus("Already connected.");
                return false;
            }

            var ready = await ScanConnectAndConfigureAsync(cancellationToken).ConfigureAwait(false);
            if (!ready || Volatile.Read(ref disposed) != 0)
            {
                // Either a step above failed (already reported via RaiseStatus), or Dispose()
                // ran concurrently — don't publish a connected state for an object whose owner
                // already tried to tear it down. Cleanup happens in `finally` below, alongside
                // every other early-return path in this method.
                return false;
            }

            IsConnected = true;

            if (Volatile.Read(ref disposed) != 0)
            {
                // Dispose() set the flag (it does so without holding writeLock, so it can
                // happen at any point during this call, including right here) after our last
                // check above but before this publish — decline to report success rather than
                // handing back "connected" state whose owner already tried to dispose this
                // object. `finally` below now triggers cleanup on `disposed` alone, so it will
                // undo the IsConnected/timer state this block just set.
                return false;
            }

            followTimer.Change(FollowInterval, FollowInterval);
            RaiseStatus(device.LastConnectWarnings.Count > 0
                ? $"Connected. WARNING: {string.Join(" | ", device.LastConnectWarnings)}"
                : "Connected.");
            return true;
        }
        finally
        {
            // Dispose() ran concurrently at some point during this call — its bounded wait for
            // writeLock timed out because we were still scanning/connecting/reading setup
            // state (or it raced the IsConnected publish above), so it deliberately left
            // `device`/`followTimer` undisposed for exactly this situation (see Dispose()'s
            // comment). Finish that cleanup here, on *every* path above — including a
            // just-published "success" — rather than leaking the BLE device and timer, or
            // handing back a live connection whose owner already tried to dispose it.
            FinishDeferredDisposalIfPending();

            if (Volatile.Read(ref disposed) == 0 && !IsConnected && device.IsConnected)
            {
                // Setup failed or was cancelled (see ScanConnectAndConfigureAsync) *after*
                // device.ConnectAsync had already established a live BLE connection — without
                // this, that connection, its GATT subscriptions, and controlPointService would
                // all be left dangling while this class reports itself disconnected. Swap in a
                // fresh FtmsDevice (same pattern DisconnectAsync uses) rather than calling
                // device.Dispose() in place, since a disposed FtmsDevice can't reconnect and
                // has its events torn down (see FtmsDevice.Dispose).
                controlPointService = null;
                firstSpeedReading = null;
                device.Dispose();
                device = new FtmsDevice();
                AttachDeviceEvents(device);
            }
            writeLock.Release();
        }
    }

    /// <summary>
    /// Scans for and connects to the treadmill, reads its setup-dependent state, and seeds
    /// <see cref="TargetSpeedKmh"/> from its first reported speed. Must be called with
    /// <see cref="writeLock"/> already held. Returns <see langword="false"/> (having already
    /// raised a status describing why) on any failure or cancellation; the caller is
    /// responsible for deciding what a successful return means for <see cref="IsConnected"/>.
    /// </summary>
    private async Task<bool> ScanConnectAndConfigureAsync(CancellationToken cancellationToken)
    {
        RaiseStatus("Scanning...");
        var address = await FtmsDevice.FindFitnessMachineAddressAsync(ScanTimeout, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            RaiseStatus("Connect cancelled.");
            return false;
        }

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
        // defaults if the read fails. Reset to the hardcoded defaults first: otherwise a
        // reconnect to a different device with a malformed range would leave the previous
        // device's values in place instead of the defaults the fallback status message claims.
        MinSpeedKmh = FtmsConstants.MinSpeedKmh;
        MaxSpeedKmh = FtmsConstants.MaxSpeedKmh;
        SpeedStepKmh = FtmsConstants.SpeedStepKmh;

        var speedRange = await device.ReadSupportedSpeedRangeAsync().ConfigureAwait(false);
        if (speedRange is { IncrementKmh: > 0 } range)
        {
            MinSpeedKmh = range.MinKmh;
            MaxSpeedKmh = range.MaxKmh;
            SpeedStepKmh = range.IncrementKmh;
            controlPointService.MinSpeedKmh = range.MinKmh;
            controlPointService.MaxSpeedKmh = range.MaxKmh;
        }
        else if (speedRange is { } malformedRange)
        {
            // A zero (or negative) increment would make SnapAndClamp divide by zero and
            // produce NaN, which silently slips past range checks (NaN comparisons are always
            // false) and gets truncated to 0 km/h — so treat it the same as a failed read and
            // keep the hardcoded default step instead.
            RaiseStatus($"Supported Speed Range (0x2AD4) reported an invalid increment ({malformedRange.IncrementKmh:0.0}) — using default step {SpeedStepKmh:0.0} km/h.");
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
        // target from a hardcoded minimum would make the first Nudge drag a live, running
        // belt down towards the minimum. Seed TargetSpeedKmh from the belt's actual current
        // speed instead, waiting briefly for the first notification.
        var readingTask = firstSpeedReading.Task;
        var completed = await Task.WhenAny(readingTask, Task.Delay(FirstReadingTimeout, cancellationToken)).ConfigureAwait(false);

        // Task.Delay(..., cancellationToken) transitions to Canceled (not Faulted) when the
        // token fires, which still makes WhenAny complete — so without this check, a cancelled
        // connect attempt falls into the "no reading yet" branch below and goes on to report
        // success. Bail out the same way every other failure path here does (return false)
        // rather than throw, so ConnectAsync doesn't need to special-case this method.
        if (cancellationToken.IsCancellationRequested)
        {
            RaiseStatus("Connect cancelled.");
            return false;
        }

        if (completed == readingTask)
        {
            // The belt may genuinely be stopped (reporting 0 km/h) despite the "already
            // running" assumption — clamp into range so a Nudge never sends a target
            // speed below the device's minimum, which SetTargetSpeedAsync rejects.
            var reading = readingTask.Result;
            TargetSpeedKmh = SnapAndClamp(reading);
            if (reading < MinSpeedKmh)
            {
                RaiseStatus($"Belt reports {reading:0.0} km/h (stopped) — the first Nudge starts from the minimum, {MinSpeedKmh:0.0} km/h.");
            }
        }
        else
        {
            TargetSpeedKmh = MinSpeedKmh;
            RaiseStatus("No speed reading within 3s of connecting — the first Nudge may change speed unexpectedly.");
        }
        firstSpeedReading = null;
        // No grace period: nothing was sent, so a belt that's elsewhere (e.g. the first reading
        // timed out and the target fell back to the minimum) is followed, not pulled to the target.
        speedFollower.OnConnected();

        return true;
    }

    /// <summary>
    /// Tears down the BLE connection and swaps in a fresh <see cref="FtmsDevice"/> so a
    /// later Connect can scan and re-read setup-dependent state instead of reusing a
    /// torn-down instance, exactly like the test app's Disconnect button.
    /// </summary>
    public Task DisconnectAsync() => DisconnectAsync(onlyDevice: null, "Disconnected.");

    /// <summary>
    /// Tears down the connection (see <see cref="DisconnectAsync()"/>) and reports
    /// <paramref name="status"/>. With <paramref name="onlyDevice"/> set, does nothing unless that
    /// is still the current device, so a late connection-lost callback from a device already
    /// replaced by a reconnect can't tear down the new connection.
    /// </summary>
    private async Task DisconnectAsync(FtmsDevice? onlyDevice, string status)
    {
        // Wait for any write already in flight (a Nudge) — or a ConnectAsync
        // still mid-flight, which now holds this same lock for its entire body — to release
        // the lock, so we never dispose the FtmsDevice a concurrent write/connect is still
        // using, and never race a Connect that's about to declare itself connected. Async so
        // a WPF click handler awaiting this doesn't freeze the UI for that duration.
        await writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref disposed) != 0
                || (onlyDevice is not null && (!ReferenceEquals(onlyDevice, device) || !IsConnected)))
            {
                return;
            }

            // Stop the timer only once we hold the lock: a ConnectAsync that was still
            // in flight when DisconnectAsync was called can start the timer (at the tail of
            // its own locked body) *after* an earlier, unlocked Change(Infinite) call here
            // would have run, leaving follow ticks running against a torn-down connection.
            followTimer.Change(Timeout.Infinite, Timeout.Infinite);

            controlPointService = null;
            IsConnected = false;
            CurrentSpeedKmh = 0;
            Volatile.Write(ref lastSpeedReading, null);

            device.Dispose();
            device = new FtmsDevice();
            AttachDeviceEvents(device);
        }
        finally
        {
            FinishDeferredDisposalIfPending();
            writeLock.Release();
        }

        RaiseStatus(status);
    }

    /// <summary>
    /// Clamps <see cref="TargetSpeedKmh"/> + <paramref name="deltaKmh"/> into
    /// [<see cref="MinSpeedKmh"/>, <see cref="MaxSpeedKmh"/>], snaps to
    /// <see cref="SpeedStepKmh"/>, sends SetTargetSpeed, and only updates the stored target
    /// if the write was accepted. If the belt is running at a speed set on the treadmill's own
    /// console, the nudge starts from that speed instead of the stale target. Deliberately never
    /// sends Start — if the treadmill isn't already moving, nudging simply won't move a stopped belt.
    /// </summary>
    public async Task<bool> NudgeSpeedAsync(double deltaKmh, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || controlPointService is null)
        {
            return false;
        }

        try
        {
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The lock was never acquired, so there's nothing to Release() — return here
            // rather than falling into the try/finally below. Callers (see
            // TreadmillNudgeSpeedAction's Validate() comment) rely on this method failing
            // gracefully — return false, never throw — regardless of why it failed.
            return false;
        }

        try
        {
            // Re-check: DisconnectAsync only nulls controlPointService once it holds this
            // same lock, so a disconnect completing between the check above and acquiring
            // the lock here would otherwise leave us dereferencing a null reference. Same for
            // `disposed`: Dispose() only sets it while holding the lock too.
            if (Volatile.Read(ref disposed) != 0 || !IsConnected || controlPointService is null)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            // Read once, so the speed nudged from is the one that was classified.
            var belt = RecentBeltReading(now);
            var fromKmh = belt is not null && speedFollower.BeltOverridesTarget(TargetSpeedKmh, belt.Kmh, MinSpeedKmh, SpeedStepKmh, now)
                ? belt.Kmh
                : TargetSpeedKmh;
            var snapped = SnapAndClamp(fromKmh + deltaKmh);
            var result = await controlPointService.SetTargetSpeedAsync(snapped).ConfigureAwait(false);
            if (result.Accepted)
            {
                TargetSpeedKmh = snapped;
                speedFollower.OnOwnTargetSet(DateTime.UtcNow);
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
            FinishDeferredDisposalIfPending();
            writeLock.Release();
        }
    }

    /// <summary>The belt's last reading, or null if there's none from the last <see cref="SpeedReadingMaxAge"/>.</summary>
    private BeltReading? RecentBeltReading(DateTime now)
    {
        var reading = Volatile.Read(ref lastSpeedReading);
        return reading is not null && now - reading.At <= SpeedReadingMaxAge ? reading : null;
    }

    /// <summary>Rounds a speed to the treadmill's step and clamps it to its supported range.</summary>
    private double SnapAndClamp(double speedKmh)
    {
        var snapped = Math.Round(speedKmh / SpeedStepKmh) * SpeedStepKmh;
        return Math.Clamp(snapped, MinSpeedKmh, MaxSpeedKmh);
    }

    /// <summary>
    /// One follow tick: checks the belt (see <see cref="TreadmillSpeedFollower"/>) and, once a speed set
    /// on the treadmill's own console settles, makes it the target so the next nudge starts from it.
    /// Never writes to the treadmill: it ramps to a target on its own, and re-sending the target every 2s
    /// stopped its console updating time and distance.
    /// </summary>
    private async Task FollowBeltSpeedAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        await writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref disposed) != 0 || !IsConnected)
            {
                return;
            }

            var now = DateTime.UtcNow;
            // Read once, so the speed adopted is the one Decide classified.
            var belt = RecentBeltReading(now);
            if (speedFollower.Decide(TargetSpeedKmh, belt, MinSpeedKmh, SpeedStepKmh, now) == FollowAction.AdoptBeltSpeed
                && belt is not null)
            {
                TargetSpeedKmh = SnapAndClamp(belt.Kmh);
                speedFollower.OnBeltSpeedAdopted();
                RaiseStatus($"Speed changed on the treadmill — now following {TargetSpeedKmh:0.0} km/h.");
            }
        }
        finally
        {
            FinishDeferredDisposalIfPending();
            writeLock.Release();
        }
    }

    /// <summary>
    /// If Dispose() ran concurrently and its bounded 1s wait for <see cref="writeLock"/>
    /// timed out because this call was still holding it (a BLE indication in
    /// <see cref="ControlPointService.SetTargetSpeedAsync"/> can take up to 2s — longer than
    /// Dispose()'s wait), finish that deferred cleanup here, right before releasing the lock,
    /// rather than leaking <see cref="device"/>/<see cref="followTimer"/> until process
    /// exit. Safe to call even when nothing is pending: <see cref="System.Threading.Timer.Dispose()"/>
    /// and <see cref="FtmsDevice.Dispose"/> are both idempotent.
    /// </summary>
    private void FinishDeferredDisposalIfPending()
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            return;
        }

        controlPointService = null;
        IsConnected = false;
        followTimer.Dispose();
        device.Dispose();
    }

    /// <summary>Raises <see cref="StatusChanged"/> with a user-facing status line.</summary>
    private void RaiseStatus(string message) => StatusChanged?.Invoke(this, message);

    /// <summary>Stops the follow timer and disconnects; any call still waiting on the write lock then does nothing.</summary>
    public void Dispose()
    {
        // Set before attempting the lock: any Connect/Nudge/follow-tick call still queued on
        // writeLock re-checks this right after it acquires the lock (each method already had
        // to re-check IsConnected/controlPointService there for the same reason), so it can
        // never touch `device`/`followTimer` once we've disposed them below — even though
        // this method doesn't hold the lock for their whole queue, only for its own turn.
        Interlocked.Exchange(ref disposed, 1);

        // Same race DisconnectAsync() guards against: without the lock, a concurrent
        // NudgeSpeedAsync/FollowBeltSpeedAsync could Release() a SemaphoreSlim we've
        // already disposed, throwing ObjectDisposedException out of their finally block.
        // Bounded rather than an unbounded Wait(): ConnectAsync now holds this lock across
        // its whole BLE scan + first-reading wait (up to ScanTimeout + FirstReadingTimeout,
        // ~13s), and an unbounded wait here would stall a WPF shutdown path for that long.
        // If we time out, a ConnectAsync is still in flight and will Release() this same
        // SemaphoreSlim from its finally block once it unwinds, and may still call
        // followTimer.Change(...) on its success path — so skip disposing `device` *and*
        // `followTimer` in that case, leaving both for the OS to reclaim on process exit,
        // rather than risk ObjectDisposedException out of that later Release()/Change() call.
        if (writeLock.Wait(TimeSpan.FromSeconds(1)))
        {
            try
            {
                controlPointService = null;
                IsConnected = false;
                followTimer.Dispose();
                device.Dispose();
            }
            finally
            {
                writeLock.Release();
            }
        }

        // Deliberately never call writeLock.Dispose(): a Nudge/follow-tick/Connect call could
        // already be queued on WaitAsync() when we get here (we only waited up to 1s above,
        // not indefinitely), and disposing the SemaphoreSlim while another thread is waiting
        // on or about to Release() it is itself a race (ObjectDisposedException either from
        // that waiter or from its own Release()). SemaphoreSlim holds no unmanaged handle
        // unless AvailableWaitHandle is touched (it never is here), so leaving it for the GC
        // costs nothing.
    }
}
