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

    /// <summary>Call whenever this app sets the target (connect, an accepted nudge), so the belt's ramp to it isn't mistaken for a console change.</summary>
    public void OnOwnTargetSet(DateTime now)
    {
        ownChangeUntil = now + OwnChangeGrace;
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
    /// <param name="beltKmh">The latest reported belt speed, or null if none is recent (then the target is always sent, as before).</param>
    /// <param name="minKmh">The treadmill's minimum speed; below it the belt counts as stopped.</param>
    /// <param name="stepKmh">The treadmill's speed step; differences within half a step are noise.</param>
    /// <param name="now">The current time.</param>
    public KeepAliveAction Decide(double targetKmh, double? beltKmh, double minKmh, double stepKmh, DateTime now)
    {
        if (beltKmh is not { } belt)
        {
            ResetTracking();
            return KeepAliveAction.SendTarget;
        }

        double tolerance = stepKmh / 2;
        double distance = Math.Abs(belt - targetKmh);
        bool converging = distance < lastDistanceKmh - tolerance;
        steadyTicks = lastBeltKmh is { } previous && Math.Abs(belt - previous) <= tolerance ? steadyTicks + 1 : 0;
        lastBeltKmh = belt;
        lastDistanceKmh = distance;

        if (distance <= tolerance || now < ownChangeUntil || converging)
        {
            // At the target, or still ramping to a target this app set.
            return KeepAliveAction.SendTarget;
        }

        if (belt < minKmh)
        {
            // Stopped (or slowed below range) on the treadmill: don't fight it.
            return KeepAliveAction.Skip;
        }

        return steadyTicks >= SteadyTicksToAdopt ? KeepAliveAction.AdoptBeltSpeed : KeepAliveAction.Skip;
    }

    /// <summary>Call after adopting the belt's speed as the target, so tracking starts again from it.</summary>
    public void OnBeltSpeedAdopted() => ResetTracking();

    private void ResetTracking()
    {
        lastBeltKmh = null;
        lastDistanceKmh = double.PositiveInfinity;
        steadyTicks = 0;
    }
}
