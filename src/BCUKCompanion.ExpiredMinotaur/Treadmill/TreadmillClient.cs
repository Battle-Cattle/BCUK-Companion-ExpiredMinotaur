using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>
/// Owns the BLE connection lifecycle and belt-following loop for a single treadmill, wrapping
/// <see cref="FtmsDevice"/> + <see cref="ControlPointService"/> into one always-there object
/// with no WPF dependency (matches <c>WizClient</c>, which is also UI-framework-agnostic).
/// </summary>
public sealed class TreadmillClient : IDisposable
{
    private static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(2);

    // Guards every Control Point write (Nudge), the target/follower state the follow tick and
    // Nudge share, AND the full Connect/Disconnect lifecycle. Redemption-triggered Nudges can
    // land at any moment, concurrent with each other and with the follow tick; without
    // serialization, two overlapping SetTargetSpeed writes could race on which BLE indication
    // resolves which caller's awaiter. Connect/Disconnect hold it for their entire body (not just the device
    // swap/dispose) so the two can never interleave: without that, a Disconnect racing a
    // still-in-flight Connect could dispose/replace the very current device/`controlPointService`
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
    private readonly ITimer followTimer;
    private readonly TimeProvider timeProvider;

    // The speed side: range, target, belt readings and following. Everything but its
    // RecordBeltSpeed (called on a BLE callback thread) is only used under writeLock.
    private readonly TreadmillSpeedControl speed = new();
    private readonly TreadmillConnectionSetup setup;

    // DisconnectAsync() swaps in a fresh device so a later Connect can scan/re-pair rather
    // than reusing a torn-down one, matching the test app's Disconnect button.
    private readonly TreadmillDeviceSlot devices;

    // Interlocked-guarded: 0 = live, 1 = disposed. Set before Dispose() attempts to acquire
    // writeLock, and checked by every other method right after it acquires the lock, so a
    // Connect/Nudge/follow-tick call that was already queued when Dispose() ran never touches
    // the device/`followTimer` after they've been disposed.
    private int disposed;

    private ControlPointService? controlPointService;

    // Numbers each connection change in the order it happened; only written under writeLock.
    private long connectionSequence;

    /// <summary>
    /// Fires on whatever thread triggered it (a BLE callback thread, or the follow
    /// timer's thread-pool thread) — any WPF subscriber must marshal via Dispatcher.Invoke
    /// itself, same pattern the test app already uses.
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Raised when the treadmill connects, is disconnected on request, or drops on its own
    /// (powered off, out of range). Fires on whatever thread made the change, after the change has
    /// released the client's lock, so two changes can be raised out of order: compare their
    /// <see cref="TreadmillConnectionChanged.Sequence"/> and ignore one older than the last seen.
    /// </summary>
    public event EventHandler<TreadmillConnectionChanged>? ConnectionChanged;

    /// <summary>Whether a treadmill is connected and accepting commands.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>The lowest target speed, in km/h: the device's own once connected, a default before.</summary>
    public double MinSpeedKmh => speed.Range.MinKmh;

    /// <summary>The highest target speed, in km/h: the device's own once connected, a default before.</summary>
    public double MaxSpeedKmh => speed.Range.MaxKmh;

    /// <summary>The device's speed step, in km/h; targets are rounded to it.</summary>
    public double SpeedStepKmh => speed.Range.StepKmh;

    /// <summary>The target speed last sent to (or adopted from) the treadmill, in km/h.</summary>
    public double TargetSpeedKmh => speed.TargetSpeedKmh;

    /// <summary>The belt speed the treadmill last reported, in km/h.</summary>
    public double CurrentSpeedKmh => speed.CurrentSpeedKmh;

    /// <summary>Creates a disconnected client; call <see cref="ConnectAsync"/> to find and connect to the treadmill.</summary>
    public TreadmillClient()
        : this(new FitnessMachineScanner(), () => new FtmsDevice(), TimeProvider.System)
    {
    }

    /// <summary>Creates a disconnected client over other parts, so tests can stand in for the treadmill and the clock.</summary>
    /// <param name="scanner">Finds the treadmill's address on <see cref="ConnectAsync"/>.</param>
    /// <param name="createDevice">Creates a fresh, unconnected device; called now and after every disconnect.</param>
    /// <param name="timeProvider">The clock for speed readings, the first-reading wait and the follow timer.</param>
    internal TreadmillClient(FitnessMachineScanner scanner, Func<ITreadmillDevice> createDevice, TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        setup = new TreadmillConnectionSetup(scanner, speed, timeProvider, RaiseStatus);
        followTimer = timeProvider.CreateTimer(_ => _ = FollowBeltSpeedAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        devices = new TreadmillDeviceSlot(createDevice, AttachDeviceEvents);
    }

    /// <summary>Subscribes to a device's disconnect and speed-reading events.</summary>
    /// <param name="attachedDevice">The device (current or freshly swapped in) whose events to handle.</param>
    private void AttachDeviceEvents(ITreadmillDevice attachedDevice)
    {
        // Without this the client kept reporting itself connected after the treadmill was powered
        // off or went out of range: every nudge failed until the user clicked Disconnect then Connect.
        attachedDevice.ConnectionLost += (_, _) => _ = DisconnectAsync(attachedDevice, "Treadmill disconnected (powered off or out of range). Click Connect to reconnect.");
        attachedDevice.TreadmillDataReceived += (_, data) => OnTreadmillData(data);
    }

    /// <summary>Takes in a Treadmill Data notification, recording the belt speed if it carries one.</summary>
    /// <param name="data">The raw Treadmill Data (0x2ACD) characteristic value.</param>
    private void OnTreadmillData(byte[] data)
    {
        try
        {
            var snapshot = TreadmillDataParser.Parse(data);
            if (snapshot.InstantaneousSpeedKmh is { } speedKmh)
            {
                speed.RecordBeltSpeed(speedKmh, timeProvider.GetUtcNow().UtcDateTime);
            }
        }
        catch (Exception ex)
        {
            RaiseStatus($"Treadmill Data parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Scans for a fitness machine, connects, requests control and starts following the belt,
    /// then raises <see cref="ConnectionChanged"/> once the client's lock has been released.
    /// </summary>
    /// <param name="cancellationToken">Cancels the scan and the wait for the first speed reading.</param>
    /// <returns>True once connected; false if already connected, nothing was found, or it failed.</returns>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (await ConnectUnderLockAsync(cancellationToken).ConfigureAwait(false) is not { } sequence)
        {
            return false;
        }

        // Raised after the lock is released, so a handler can call back into this client.
        ConnectionChanged?.Invoke(this, new TreadmillConnectionChanged(TreadmillConnectionChange.Connected, sequence));
        return true;
    }

    /// <summary>Does <see cref="ConnectAsync"/>'s work while holding the client's lock.</summary>
    /// <param name="cancellationToken">Cancels the scan and the wait for the first speed reading.</param>
    /// <returns>A task whose result is the connection change's sequence number once connected; null if already connected, nothing was found, or it failed.</returns>
    private async Task<long?> ConnectUnderLockAsync(CancellationToken cancellationToken)
    {
        // Held for the entire method — see the writeLock field comment — so a concurrent
        // DisconnectAsync can never dispose/replace the device/`controlPointService` out from
        // under this call, and this call can never finish by unconditionally declaring itself
        // connected against state a Disconnect already tore down.
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return null;
            }

            if (IsConnected)
            {
                // FtmsDevice.ConnectAsync tears down the active BLE connection (via
                // ReleaseCurrentDevice) before establishing a new one — if that new attempt
                // then failed, this class would be left reporting IsConnected == true against
                // a device that's no longer actually connected. Reject the reconnect outright
                // rather than risk that; callers must Disconnect first.
                RaiseStatus("Already connected.");
                return null;
            }

            var service = await setup.RunAsync(devices.Current, cancellationToken).ConfigureAwait(false);
            if (service is null || Volatile.Read(ref disposed) != 0)
            {
                // Either a step failed (already reported via RaiseStatus), or Dispose()
                // ran concurrently — don't publish a connected state for an object whose owner
                // already tried to tear it down. Cleanup happens in `finally` below, alongside
                // every other early-return path in this method.
                return null;
            }

            controlPointService = service;
            IsConnected = true;
            long connectedSequence = ++connectionSequence;

            if (Volatile.Read(ref disposed) != 0)
            {
                // Dispose() set the flag (it does so without holding writeLock, so it can
                // happen at any point during this call, including right here) after our last
                // check above but before this publish — decline to report success rather than
                // handing back "connected" state whose owner already tried to dispose this
                // object. `finally` below now triggers cleanup on `disposed` alone, so it will
                // undo the IsConnected/timer state this block just set.
                return null;
            }

            followTimer.Change(FollowInterval, FollowInterval);
            var warnings = devices.Current.LastConnectWarnings;
            RaiseStatus(warnings.Count > 0
                ? $"Connected. WARNING: {string.Join(" | ", warnings)}"
                : "Connected.");
            return connectedSequence;
        }
        finally
        {
            // Dispose() ran concurrently at some point during this call — its bounded wait for
            // writeLock timed out because we were still scanning/connecting/reading setup
            // state (or it raced the IsConnected publish above), so it deliberately left
            // the device/`followTimer` undisposed for exactly this situation (see Dispose()'s
            // comment). Finish that cleanup here, on *every* path above — including a
            // just-published "success" — rather than leaking the BLE device and timer, or
            // handing back a live connection whose owner already tried to dispose it.
            FinishDeferredDisposalIfPending();

            if (Volatile.Read(ref disposed) == 0 && !IsConnected && devices.Current.IsConnected)
            {
                // Setup failed or was cancelled (see TreadmillConnectionSetup) *after*
                // device.ConnectAsync had already established a live BLE connection — without
                // this, that connection and its GATT subscriptions would be left dangling while
                // this class reports itself disconnected. Swap in a fresh device (same pattern
                // DisconnectAsync uses) rather than disposing it in place, since a disposed
                // FtmsDevice can't reconnect and has its events torn down (see FtmsDevice.Dispose).
                controlPointService = null;
                devices.Replace();
            }

            writeLock.Release();
        }
    }

    /// <summary>
    /// Tears down the BLE connection and swaps in a fresh <see cref="FtmsDevice"/> so a
    /// later Connect can scan and re-read setup-dependent state instead of reusing a
    /// torn-down instance, exactly like the test app's Disconnect button.
    /// </summary>
    /// <returns>A task that completes once the connection is torn down and the status reported.</returns>
    public Task DisconnectAsync() => DisconnectAsync(onlyDevice: null, "Disconnected.");

    /// <summary>
    /// Tears down the connection (see <see cref="DisconnectAsync()"/>) and reports
    /// <paramref name="status"/>. With <paramref name="onlyDevice"/> set, does nothing unless that
    /// is still the current device, so a late connection-lost callback from a device already
    /// replaced by a reconnect can't tear down the new connection.
    /// </summary>
    /// <param name="onlyDevice">The device whose connection was lost, or null to disconnect whatever is current.</param>
    /// <param name="status">The status line to raise once torn down.</param>
    /// <returns>A task that completes once the teardown has finished (and <see cref="ConnectionChanged"/> has been raised) or been skipped.</returns>
    private async Task DisconnectAsync(ITreadmillDevice? onlyDevice, string status)
    {
        // Wait for any write already in flight (a Nudge) — or a ConnectAsync
        // still mid-flight, which now holds this same lock for its entire body — to release
        // the lock, so we never dispose the FtmsDevice a concurrent write/connect is still
        // using, and never race a Connect that's about to declare itself connected. Async so
        // a WPF click handler awaiting this doesn't freeze the UI for that duration.
        TreadmillConnectionChanged change;
        await writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref disposed) != 0
                || (onlyDevice is not null && (!ReferenceEquals(onlyDevice, devices.Current) || !IsConnected)))
            {
                return;
            }

            // Stop the timer only once we hold the lock: a ConnectAsync that was still
            // in flight when DisconnectAsync was called can start the timer (at the tail of
            // its own locked body) *after* an earlier, unlocked Change(Infinite) call here
            // would have run, leaving follow ticks running against a torn-down connection.
            followTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            controlPointService = null;
            IsConnected = false;
            speed.OnDisconnected();
            devices.Replace();
            change = new TreadmillConnectionChanged(
                onlyDevice is null ? TreadmillConnectionChange.Disconnected : TreadmillConnectionChange.Dropped, ++connectionSequence);
        }
        finally
        {
            FinishDeferredDisposalIfPending();
            writeLock.Release();
        }

        RaiseStatus(status);
        ConnectionChanged?.Invoke(this, change);
    }

    /// <summary>
    /// Sends <see cref="TargetSpeedKmh"/> + <paramref name="deltaKmh"/>, clamped into
    /// [<see cref="MinSpeedKmh"/>, <see cref="MaxSpeedKmh"/>] and snapped to
    /// <see cref="SpeedStepKmh"/>, as SetTargetSpeed, and only updates the stored target
    /// if the write was accepted. If the belt is running at a speed set on the treadmill's own
    /// console, the nudge starts from that speed instead of the stale target (see
    /// <see cref="TreadmillSpeedControl.PlanNudge"/>). Deliberately never sends Start — if the
    /// treadmill isn't already moving, nudging simply won't move a stopped belt.
    /// </summary>
    /// <param name="deltaKmh">How much to change the speed by, in km/h; negative slows down.</param>
    /// <param name="cancellationToken">Cancels waiting for the write lock (the method then returns false).</param>
    /// <returns>A task completing with true if the treadmill accepted the new target; false if disconnected, cancelled, rejected or the write failed. Device and write errors are reported as false, but an exception thrown by a <see cref="StatusChanged"/> handler propagates.</returns>
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

            var target = speed.PlanNudge(deltaKmh, timeProvider.GetUtcNow().UtcDateTime);
            var result = await controlPointService.SetTargetSpeedAsync(target).ConfigureAwait(false);
            if (result.Accepted)
            {
                speed.OnTargetAccepted(target, timeProvider.GetUtcNow().UtcDateTime);
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

    /// <summary>
    /// One follow tick: checks the belt (see <see cref="TreadmillSpeedControl.FollowBelt"/>) and, once a
    /// speed set on the treadmill's own console settles, makes it the target so the next nudge starts
    /// from it. Never writes to the treadmill: it ramps to a target on its own, and re-sending the target
    /// every 2s stopped its console updating time and distance.
    /// </summary>
    /// <returns>A task that completes once the tick has finished.</returns>
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

            if (speed.FollowBelt(timeProvider.GetUtcNow().UtcDateTime))
            {
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
    /// rather than leaking the device/<see cref="followTimer"/> until process
    /// exit. Safe to call even when nothing is pending: <see cref="IDisposable.Dispose"/> on the timer
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
        devices.Current.Dispose();
    }

    /// <summary>Raises <see cref="StatusChanged"/> with a user-facing status line.</summary>
    /// <param name="message">The status line to show.</param>
    private void RaiseStatus(string message) => StatusChanged?.Invoke(this, message);

    /// <summary>Stops the follow timer and disconnects; any call still waiting on the write lock then does nothing.</summary>
    public void Dispose()
    {
        // Set before attempting the lock: any Connect/Nudge/follow-tick call still queued on
        // writeLock re-checks this right after it acquires the lock (each method already had
        // to re-check IsConnected/controlPointService there for the same reason), so it can
        // never touch the device/`followTimer` once we've disposed them below — even though
        // this method doesn't hold the lock for their whole queue, only for its own turn.
        Interlocked.Exchange(ref disposed, 1);

        // Same race DisconnectAsync() guards against: without the lock, a concurrent
        // NudgeSpeedAsync/FollowBeltSpeedAsync could Release() a SemaphoreSlim we've
        // already disposed, throwing ObjectDisposedException out of their finally block.
        // Bounded rather than an unbounded Wait(): ConnectAsync now holds this lock across
        // its whole BLE scan + first-reading wait (up to TreadmillConnectionSetup's ScanTimeout + FirstReadingTimeout,
        // ~13s), and an unbounded wait here would stall a WPF shutdown path for that long.
        // If we time out, a ConnectAsync is still in flight and will Release() this same
        // SemaphoreSlim from its finally block once it unwinds, and may still call
        // followTimer.Change(...) on its success path — so skip disposing the device *and*
        // `followTimer` in that case, leaving both for the OS to reclaim on process exit,
        // rather than risk ObjectDisposedException out of that later Release()/Change() call.
        if (writeLock.Wait(TimeSpan.FromSeconds(1)))
        {
            try
            {
                controlPointService = null;
                IsConnected = false;
                followTimer.Dispose();
                devices.Current.Dispose();
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

/// <summary>How a <see cref="TreadmillClient"/>'s connection changed.</summary>
public enum TreadmillConnectionChange
{
    /// <summary>It connected.</summary>
    Connected,

    /// <summary>It was disconnected on request (the Disconnect button).</summary>
    Disconnected,

    /// <summary>It dropped on its own: the treadmill was powered off or went out of range.</summary>
    Dropped,
}

/// <summary>A change to a <see cref="TreadmillClient"/>'s connection.</summary>
/// <param name="Change">How the connection changed.</param>
/// <param name="Sequence">Increases with each change, in the order the changes happened; a handler can ignore one lower than the last it saw.</param>
public readonly record struct TreadmillConnectionChanged(TreadmillConnectionChange Change, long Sequence);
