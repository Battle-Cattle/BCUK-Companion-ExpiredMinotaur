using BCUKCompanion.ExpiredMinotaur.Treadmill;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillSpeedControlTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly TreadmillSpeedControl speed = new() { Range = new TreadmillSpeedRange(0.5, 10.0, 0.1) };

    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    [Fact]
    public void StartsWithTheDefaultRangeAndNoSpeed()
    {
        var fresh = new TreadmillSpeedControl();

        Assert.Equal(TreadmillSpeedRange.Default, fresh.Range);
        Assert.Equal(0, fresh.TargetSpeedKmh);
        Assert.Equal(0, fresh.CurrentSpeedKmh);
    }

    [Fact]
    public void RecordBeltSpeed_UpdatesTheCurrentSpeed()
    {
        speed.RecordBeltSpeed(4.2, T0);

        Assert.Equal(4.2, speed.CurrentSpeedKmh);
    }

    [Fact]
    public async Task ExpectFirstReading_CompletesWithTheNextReading()
    {
        var first = speed.ExpectFirstReading();
        Assert.False(first.IsCompleted);

        speed.RecordBeltSpeed(3.3, T0);
        speed.RecordBeltSpeed(4.4, At(1));

        Assert.Equal(3.3, await first);
    }

    [Fact]
    public void StopExpectingFirstReading_LeavesTheWaitUnfinished()
    {
        var first = speed.ExpectFirstReading();

        speed.StopExpectingFirstReading();
        speed.RecordBeltSpeed(3.3, T0);

        Assert.False(first.IsCompleted);
    }

    [Fact]
    public void SeedTarget_FromARunningBelt_SnapsItWithoutAWarning()
    {
        Assert.Null(speed.SeedTarget(3.04));

        Assert.Equal(3.0, speed.TargetSpeedKmh, 6);
    }

    [Fact]
    public void SeedTarget_FromAStoppedBelt_StartsFromTheMinimum()
    {
        var warning = speed.SeedTarget(0);

        Assert.Equal(0.5, speed.TargetSpeedKmh);
        Assert.Equal("Belt reports 0.0 km/h (stopped) — the first Nudge starts from the minimum, 0.5 km/h.", warning);
    }

    [Fact]
    public void SeedTarget_WithoutAReading_StartsFromTheMinimum()
    {
        var warning = speed.SeedTarget(null);

        Assert.Equal(0.5, speed.TargetSpeedKmh);
        Assert.Equal("No speed reading within 3s of connecting — the first Nudge may change speed unexpectedly.", warning);
    }

    [Fact]
    public void PlanNudge_StartsFromTheTarget_AndDoesNotChangeIt()
    {
        speed.SeedTarget(3.0);

        Assert.Equal(4.0, speed.PlanNudge(1.0, T0), 6);
        Assert.Equal(3.0, speed.TargetSpeedKmh, 6);
    }

    [Fact]
    public void PlanNudge_ClampsToTheRange()
    {
        speed.SeedTarget(3.0);

        Assert.Equal(0.5, speed.PlanNudge(-10, T0));
        Assert.Equal(10.0, speed.PlanNudge(+10, T0));
    }

    [Fact]
    public void PlanNudge_StartsFromASpeedSetOnTheConsole()
    {
        speed.SeedTarget(3.0);
        speed.RecordBeltSpeed(6.0, T0);

        Assert.Equal(7.0, speed.PlanNudge(1.0, T0), 6);
    }

    [Fact]
    public void PlanNudge_IgnoresABeltReadingOlderThanTheMaxAge()
    {
        speed.SeedTarget(3.0);
        speed.RecordBeltSpeed(6.0, T0);

        Assert.Equal(4.0, speed.PlanNudge(1.0, T0 + TreadmillSpeedControl.SpeedReadingMaxAge + TimeSpan.FromSeconds(1)), 6);
    }

    [Fact]
    public void OnTargetAccepted_StoresTheTarget_AndGivesTheBeltTimeToRampToIt()
    {
        speed.SeedTarget(3.0);
        speed.OnTargetAccepted(5.0, T0);
        speed.RecordBeltSpeed(3.2, At(1));

        Assert.Equal(5.0, speed.TargetSpeedKmh);
        // Still ramping to this app's own target, so a nudge starts from it rather than the belt.
        Assert.Equal(5.5, speed.PlanNudge(0.5, At(1)), 6);
    }

    [Fact]
    public void FollowBelt_AdoptsASpeedSetOnTheConsoleOnceItSettles()
    {
        speed.SeedTarget(3.0);
        var adopted = new List<bool>();

        for (var tick = 0; tick < 3; tick++)
        {
            speed.RecordBeltSpeed(6.0, At(tick * 2));
            adopted.Add(speed.FollowBelt(At(tick * 2)));
        }

        Assert.Equal([false, false, true], adopted);
        Assert.Equal(6.0, speed.TargetSpeedKmh, 6);
    }

    [Fact]
    public void FollowBelt_HoldsTheTargetWithoutARecentReading()
    {
        speed.SeedTarget(3.0);
        speed.RecordBeltSpeed(6.0, T0);

        Assert.False(speed.FollowBelt(At(10)));
        Assert.Equal(3.0, speed.TargetSpeedKmh);
    }

    [Fact]
    public void OnDisconnected_ForgetsTheBeltSpeed()
    {
        speed.SeedTarget(3.0);
        speed.RecordBeltSpeed(6.0, T0);

        speed.OnDisconnected();

        Assert.Equal(0, speed.CurrentSpeedKmh);
        Assert.Equal(4.0, speed.PlanNudge(1.0, T0), 6);
    }
}
