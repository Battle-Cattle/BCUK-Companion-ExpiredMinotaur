using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>
/// The target speeds a treadmill accepts: its minimum, maximum and step. Read fresh from the device on
/// every connect (see <see cref="FromDevice"/>), since it varies by how the treadmill is set up.
/// </summary>
/// <param name="MinKmh">The lowest target speed, in km/h.</param>
/// <param name="MaxKmh">The highest target speed, in km/h.</param>
/// <param name="StepKmh">The speed step in km/h; targets are rounded to a multiple of it.</param>
internal readonly record struct TreadmillSpeedRange(double MinKmh, double MaxKmh, double StepKmh)
{
    /// <summary>Gets the hardcoded range used before connecting, and whenever the device's own can't be used.</summary>
    public static TreadmillSpeedRange Default { get; } =
        new(FtmsConstants.MinSpeedKmh, FtmsConstants.MaxSpeedKmh, FtmsConstants.SpeedStepKmh);

    /// <summary>
    /// Picks the range to use from what the device reported for Supported Speed Range (0x2AD4),
    /// falling back to <see cref="Default"/> if the read failed or the range is unusable.
    /// </summary>
    /// <param name="reported">The range the device reported, or null if it couldn't be read.</param>
    /// <returns>The range to use, and a status line saying why the default was used instead (null when the device's range is used).</returns>
    public static (TreadmillSpeedRange Range, string? Warning) FromDevice((double MinKmh, double MaxKmh, double IncrementKmh)? reported)
    {
        if (reported is { IncrementKmh: > 0 } range)
        {
            return (new TreadmillSpeedRange(range.MinKmh, range.MaxKmh, range.IncrementKmh), null);
        }

        if (reported is { } malformedRange)
        {
            // A zero (or negative) increment would make SnapAndClamp divide by zero and
            // produce NaN, which silently slips past range checks (NaN comparisons are always
            // false) and gets truncated to 0 km/h — so treat it the same as a failed read and
            // keep the hardcoded default step instead.
            return (Default, $"Supported Speed Range (0x2AD4) reported an invalid increment ({malformedRange.IncrementKmh:0.0}) — using default step {Default.StepKmh:0.0} km/h.");
        }

        return (Default, $"Could not read Supported Speed Range (0x2AD4) — using defaults {Default.MinKmh:0.0}-{Default.MaxKmh:0.0} km/h.");
    }

    /// <summary>Rounds a speed to the treadmill's step and clamps it to its supported range.</summary>
    /// <param name="speedKmh">The speed to snap, in km/h.</param>
    /// <returns>The nearest multiple of <see cref="StepKmh"/>, clamped to [<see cref="MinKmh"/>, <see cref="MaxKmh"/>].</returns>
    public double SnapAndClamp(double speedKmh)
    {
        var snapped = Math.Round(speedKmh / StepKmh) * StepKmh;
        return Math.Clamp(snapped, MinKmh, MaxKmh);
    }
}
