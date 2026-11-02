namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>
/// The speed side of <see cref="TreadmillClient"/>: the belt's reported speed, the target speed, the
/// supported range, and the decisions about them (where a nudge starts from, when a speed set on the
/// treadmill's console becomes the target). Uses only the speeds and times it's given, with no BLE
/// dependency, so it can be tested on its own. Not thread-safe beyond <see cref="RecordBeltSpeed"/>:
/// the client calls everything else under its write lock.
/// </summary>
internal sealed class TreadmillSpeedControl
{
    /// <summary>
    /// A speed reading older than this is treated as unknown, so the follow tick holds the target
    /// and a nudge starts from it, rather than reacting to a belt speed it can no longer see.
    /// </summary>
    internal static readonly TimeSpan SpeedReadingMaxAge = TimeSpan.FromSeconds(5);

    // Follows a speed set on the treadmill's own console, so a nudge starts from it.
    private readonly TreadmillSpeedFollower speedFollower = new();

    // The belt's last reported speed and when it arrived (null = none since connecting). One
    // immutable reading, so the speed and its time are always read together. Written on a BLE
    // callback thread, read under the client's write lock, hence Volatile.
    private BeltReading? lastSpeedReading;

    // Set while connecting, so the first speed reading can seed the target.
    private TaskCompletionSource<double>? firstSpeedReading;

    /// <summary>Gets or sets the target speeds the treadmill accepts: the device's own once connected, a default before.</summary>
    public TreadmillSpeedRange Range { get; set; } = TreadmillSpeedRange.Default;

    /// <summary>Gets the target speed last sent to (or adopted from) the treadmill, in km/h.</summary>
    public double TargetSpeedKmh { get; private set; }

    /// <summary>Gets the belt speed the treadmill last reported, in km/h (0 once disconnected).</summary>
    public double CurrentSpeedKmh { get; private set; }

    /// <summary>Takes in a belt speed the treadmill reported. Safe to call from a BLE callback thread.</summary>
    /// <param name="speedKmh">The reported belt speed, in km/h.</param>
    /// <param name="at">When (UTC) the reading arrived.</param>
    public void RecordBeltSpeed(double speedKmh, DateTime at)
    {
        CurrentSpeedKmh = speedKmh;
        Volatile.Write(ref lastSpeedReading, new BeltReading(speedKmh, at));
        firstSpeedReading?.TrySetResult(speedKmh);
    }

    /// <summary>
    /// Starts waiting for the first speed reading of a new connection. Call before connecting:
    /// the device subscribes to Treadmill Data notifications while connecting, and the first one
    /// can arrive before the connect returns.
    /// </summary>
    /// <returns>A task completing with the first belt speed (km/h) reported after this call.</returns>
    public Task<double> ExpectFirstReading()
    {
        firstSpeedReading = new TaskCompletionSource<double>();
        return firstSpeedReading.Task;
    }

    /// <summary>Stops waiting for a first speed reading (see <see cref="ExpectFirstReading"/>).</summary>
    public void StopExpectingFirstReading() => firstSpeedReading = null;

    /// <summary>
    /// Seeds the target from the belt's speed on connecting, and starts following the belt afresh.
    /// The belt is expected to already be moving when Connect is clicked — starting from a hardcoded
    /// minimum would make the first nudge drag a live, running belt down towards it.
    /// </summary>
    /// <param name="firstReadingKmh">The first belt speed reported after connecting, in km/h, or null if none arrived in time.</param>
    /// <returns>A status line warning about how the target was seeded, or null if it was seeded from a running belt.</returns>
    public string? SeedTarget(double? firstReadingKmh)
    {
        string? warning = null;
        if (firstReadingKmh is { } reading)
        {
            // The belt may genuinely be stopped (reporting 0 km/h) despite the "already
            // running" assumption — clamp into range so a Nudge never sends a target
            // speed below the device's minimum, which SetTargetSpeedAsync rejects.
            TargetSpeedKmh = Range.SnapAndClamp(reading);
            if (reading < Range.MinKmh)
            {
                warning = $"Belt reports {reading:0.0} km/h (stopped) — the first Nudge starts from the minimum, {Range.MinKmh:0.0} km/h.";
            }
        }
        else
        {
            TargetSpeedKmh = Range.MinKmh;
            warning = "No speed reading within 3s of connecting — the first Nudge may change speed unexpectedly.";
        }

        // No grace period: nothing was sent, so a belt that's elsewhere (e.g. the first reading
        // timed out and the target fell back to the minimum) is followed, not pulled to the target.
        speedFollower.OnConnected();
        return warning;
    }

    /// <summary>
    /// Works out the target a nudge sends: the current target plus <paramref name="deltaKmh"/>, or the
    /// belt's speed plus it if the belt is running at a speed set on the treadmill's own console,
    /// snapped to the step and clamped to the range. Doesn't change the stored target.
    /// </summary>
    /// <param name="deltaKmh">How much to change the speed by, in km/h; negative slows down.</param>
    /// <param name="now">The current UTC time, which the belt reading's age is measured against.</param>
    /// <returns>The new target speed to send, in km/h.</returns>
    public double PlanNudge(double deltaKmh, DateTime now)
    {
        // Read once, so the speed nudged from is the one that was classified.
        var belt = RecentBeltReading(now);
        var fromKmh = belt is not null && speedFollower.BeltOverridesTarget(TargetSpeedKmh, belt.Kmh, Range.MinKmh, Range.StepKmh, now)
            ? belt.Kmh
            : TargetSpeedKmh;
        return Range.SnapAndClamp(fromKmh + deltaKmh);
    }

    /// <summary>Records a target the treadmill accepted, so the belt's ramp to it isn't mistaken for a console change.</summary>
    /// <param name="targetKmh">The accepted target speed, in km/h (from <see cref="PlanNudge"/>).</param>
    /// <param name="now">When (UTC) the treadmill accepted it.</param>
    public void OnTargetAccepted(double targetKmh, DateTime now)
    {
        TargetSpeedKmh = targetKmh;
        speedFollower.OnOwnTargetSet(now);
    }

    /// <summary>
    /// One follow tick: checks the belt (see <see cref="TreadmillSpeedFollower"/>) and, once a speed set
    /// on the treadmill's own console settles, makes it the target so the next nudge starts from it.
    /// </summary>
    /// <param name="now">The current UTC time.</param>
    /// <returns>True if the belt's speed was adopted as the new <see cref="TargetSpeedKmh"/>; false if the target was held.</returns>
    public bool FollowBelt(DateTime now)
    {
        // Read once, so the speed adopted is the one Decide classified.
        var belt = RecentBeltReading(now);
        if (speedFollower.Decide(TargetSpeedKmh, belt, Range.MinKmh, Range.StepKmh, now) != FollowAction.AdoptBeltSpeed
            || belt is null)
        {
            return false;
        }

        TargetSpeedKmh = Range.SnapAndClamp(belt.Kmh);
        speedFollower.OnBeltSpeedAdopted();
        return true;
    }

    /// <summary>Forgets the belt's speed once disconnected, so a later connection never acts on it.</summary>
    public void OnDisconnected()
    {
        CurrentSpeedKmh = 0;
        Volatile.Write(ref lastSpeedReading, null);
    }

    /// <summary>The belt's last reading, or null if there's none from the last <see cref="SpeedReadingMaxAge"/>.</summary>
    /// <param name="now">The current UTC time, which the reading's age is measured against.</param>
    /// <returns>The last reading if it's recent enough; otherwise null.</returns>
    private BeltReading? RecentBeltReading(DateTime now)
    {
        var reading = Volatile.Read(ref lastSpeedReading);
        return reading is not null && now - reading.At <= SpeedReadingMaxAge ? reading : null;
    }
}
