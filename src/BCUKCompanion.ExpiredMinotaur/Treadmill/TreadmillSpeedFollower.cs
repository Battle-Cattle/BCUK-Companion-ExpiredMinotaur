namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>What the keep-alive should do on one tick (see <see cref="TreadmillSpeedFollower.Decide"/>).</summary>
internal enum KeepAliveAction
{
    /// <summary>Re-send the current target speed, as the keep-alive always has.</summary>
    SendTarget,

    /// <summary>Send nothing this tick: the belt is being changed (or was stopped) on the treadmill itself.</summary>
    Skip,

    /// <summary>The belt settled at a speed set on the treadmill itself: make that the target, then send it.</summary>
    AdoptBeltSpeed,
}

/// <summary>A belt speed the treadmill reported, and when (UTC) it arrived.</summary>
internal sealed record BeltReading(double Kmh, DateTime At);

/// <summary>
/// Tells a speed set on the treadmill's own console apart from the belt still ramping to a target
/// this app sent, so the keep-alive follows the console instead of dragging the belt back.
/// The keep-alive has to keep re-sending a target (one SetTargetSpeed write doesn't reliably stick
/// on its own), which used to undo any console change within 2 seconds.
/// Uses only the speeds and times it's given, with no BLE dependency, so it can be tested on its own.
/// </summary>
internal sealed class TreadmillSpeedFollower
{
    /// <summary>
    /// After this app sets a target, how long the belt gets to start moving towards it before a
    /// difference counts as a console change. After that, a belt still closing in on the target
    /// counts as this app's own change too.
    /// </summary>
    internal static readonly TimeSpan OwnChangeGrace = TimeSpan.FromSeconds(10);

    /// <summary>How many ticks in a row an off-target belt must hold steady before it's adopted, so a console change still ramping isn't caught part-way.</summary>
    internal const int SteadyTicksToAdopt = 2;

    private DateTime ownChangeUntil;
    private double? lastBeltKmh;
    private double lastDistanceKmh = double.PositiveInfinity;
    private int steadyTicks;

    // The reading the history above last took in, and what Decide made of it. The keep-alive asks
    // every 2s whether or not a new reading arrived, so a repeat must not count as another tick.
    private DateTime? lastReadingAt;
    private KeepAliveAction lastAction = KeepAliveAction.SendTarget;

    // Set once the belt is seen off target outside this app's own change, so a later move towards
    // the old target (a console restart, a console reversal) isn't mistaken for this app's ramp.
    private bool consoleChangeSeen;

    /// <summary>
    /// Call once connected, before the first keep-alive: forgets the last connection's history
    /// without a grace period, since connecting seeds the target from the belt rather than sending one.
    /// </summary>
    public void OnConnected()
    {
        ownChangeUntil = DateTime.MinValue;
        consoleChangeSeen = false;
        ResetTracking();
    }

    /// <summary>Call whenever this app sends a new target (an accepted nudge), so the belt's ramp to it isn't mistaken for a console change.</summary>
    public void OnOwnTargetSet(DateTime now)
    {
        ownChangeUntil = now + OwnChangeGrace;
        consoleChangeSeen = false;
        ResetTracking();
    }

    /// <summary>
    /// Whether the belt is somewhere this app didn't send it: off target, past the grace period,
    /// and running. A nudge then starts from the belt's speed instead of the stale target.
    /// </summary>
    /// <param name="beltKmh">The latest reported belt speed, or null if none is recent.</param>
    public bool BeltOverridesTarget(double targetKmh, double? beltKmh, double minKmh, double stepKmh, DateTime now) =>
        beltKmh is { } belt
        && now >= ownChangeUntil
        && belt >= minKmh
        && Math.Abs(belt - targetKmh) > stepKmh / 2;

    /// <summary>Decides what the keep-alive does this tick, and updates the belt history it compares against.</summary>
    /// <param name="targetKmh">The target speed the keep-alive would re-send.</param>
    /// <param name="reading">The latest belt reading, or null if none is recent (then the target is always sent, as before).</param>
    /// <param name="minKmh">The treadmill's minimum speed; below it the belt counts as stopped.</param>
    /// <param name="stepKmh">The treadmill's speed step; differences within half a step are noise.</param>
    /// <param name="now">The current time.</param>
    public KeepAliveAction Decide(double targetKmh, BeltReading? reading, double minKmh, double stepKmh, DateTime now)
    {
        if (reading is null)
        {
            ResetTracking();
            return KeepAliveAction.SendTarget;
        }

        if (reading.At == lastReadingAt)
        {
            // No new reading since the last tick: nothing has changed, so neither the history nor the
            // decision moves on (an adopted speed is the target now, so it's sent).
            return Math.Abs(reading.Kmh - targetKmh) <= stepKmh / 2 || lastAction == KeepAliveAction.SendTarget
                ? KeepAliveAction.SendTarget
                : KeepAliveAction.Skip;
        }

        lastReadingAt = reading.At;
        lastAction = DecideOnNewReading(targetKmh, reading.Kmh, minKmh, stepKmh, now);
        return lastAction;
    }

    /// <summary>Takes a newly arrived belt speed into the history and decides on it (see <see cref="Decide"/>).</summary>
    private KeepAliveAction DecideOnNewReading(double targetKmh, double belt, double minKmh, double stepKmh, DateTime now)
    {
        double tolerance = stepKmh / 2;
        double distance = Math.Abs(belt - targetKmh);
        // Only a belt already being tracked, with no console change seen since, can be converging:
        // the first reading after a gap has nothing to compare against.
        bool converging = !consoleChangeSeen && lastBeltKmh is not null && distance < lastDistanceKmh - tolerance;
        steadyTicks = lastBeltKmh is { } previous && Math.Abs(belt - previous) <= tolerance ? steadyTicks + 1 : 0;
        lastBeltKmh = belt;
        lastDistanceKmh = distance;

        if (distance <= tolerance)
        {
            // Back at the target, however it got there.
            consoleChangeSeen = false;
            return KeepAliveAction.SendTarget;
        }

        if (now < ownChangeUntil || converging)
        {
            // Still ramping to a target this app set.
            return KeepAliveAction.SendTarget;
        }

        consoleChangeSeen = true;
        if (belt < minKmh)
        {
            // Stopped (or slowed below range) on the treadmill: don't fight it.
            return KeepAliveAction.Skip;
        }

        return steadyTicks >= SteadyTicksToAdopt ? KeepAliveAction.AdoptBeltSpeed : KeepAliveAction.Skip;
    }

    /// <summary>Call after adopting the belt's speed as the target, so tracking starts again from it.</summary>
    public void OnBeltSpeedAdopted()
    {
        consoleChangeSeen = false;
        ResetTracking();
    }

    /// <summary>Forgets the belt history, so the next reading starts tracking afresh (it can't count as converging or steady).</summary>
    private void ResetTracking()
    {
        lastReadingAt = null;
        lastAction = KeepAliveAction.SendTarget;
        lastBeltKmh = null;
        lastDistanceKmh = double.PositiveInfinity;
        steadyTicks = 0;
    }
}
