using BCUKCompanion.ExpiredMinotaur.Treadmill;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillSpeedFollowerTests
{
    private const double Min = 1.0;
    private const double Step = 0.5;

    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly TreadmillSpeedFollower follower = new();

    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    private FollowAction Tick(double targetKmh, double beltKmh, double seconds) =>
        follower.Decide(targetKmh, new BeltReading(beltKmh, At(seconds)), Min, Step, At(seconds));

    [Fact]
    public void Decide_NoReading_Holds()
    {
        follower.OnConnected();

        Assert.Equal(FollowAction.Hold, follower.Decide(5.0, null, Min, Step, T0));
    }

    [Fact]
    public void Decide_BeltAtTarget_Holds()
    {
        follower.OnConnected();

        Assert.Equal(FollowAction.Hold, Tick(5.0, 5.0, 0));
        Assert.Equal(FollowAction.Hold, Tick(5.0, 5.2, 2));
        Assert.Equal(FollowAction.Hold, Tick(5.0, 4.8, 4));
    }

    [Fact]
    public void Decide_ConsoleChangeHeldSteady_AdoptsBeltSpeedAfterSteadyTicks()
    {
        follower.OnConnected();
        Tick(5.0, 5.0, 0);

        Assert.Equal(FollowAction.Hold, Tick(5.0, 7.0, 2));
        Assert.Equal(FollowAction.Hold, Tick(5.0, 7.0, 4));
        Assert.Equal(FollowAction.AdoptBeltSpeed, Tick(5.0, 7.0, 6));
    }

    [Fact]
    public void Decide_ConsoleChangeStillRamping_IsNotCaughtPartWay()
    {
        follower.OnConnected();
        Tick(5.0, 5.0, 0);

        Assert.Equal(FollowAction.Hold, Tick(5.0, 6.0, 2));
        Assert.Equal(FollowAction.Hold, Tick(5.0, 7.0, 4));
        Assert.Equal(FollowAction.Hold, Tick(5.0, 8.0, 6));
        Assert.Equal(FollowAction.Hold, Tick(5.0, 8.0, 8));
        Assert.Equal(FollowAction.AdoptBeltSpeed, Tick(5.0, 8.0, 10));
    }

    [Fact]
    public void Decide_WithinOwnChangeGrace_Holds()
    {
        follower.OnConnected();
        follower.OnOwnTargetSet(T0);

        Assert.Equal(FollowAction.Hold, Tick(7.0, 5.0, 2));
        Assert.Equal(FollowAction.Hold, Tick(7.0, 5.0, 4));
        Assert.Equal(FollowAction.Hold, Tick(7.0, 5.0, 6));
    }

    [Fact]
    public void Decide_BeltNeverMovesTowardsOwnTarget_AdoptsItAfterGrace()
    {
        follower.OnConnected();
        follower.OnOwnTargetSet(T0);
        Tick(7.0, 5.0, 2);
        Tick(7.0, 5.0, 4);

        var afterGrace = TreadmillSpeedFollower.OwnChangeGrace.TotalSeconds + 1;
        Assert.Equal(FollowAction.AdoptBeltSpeed, Tick(7.0, 5.0, afterGrace));
    }

    [Fact]
    public void Decide_BeltRampingToOwnTargetPastGrace_Holds()
    {
        follower.OnConnected();
        follower.OnOwnTargetSet(T0);

        // A slow treadmill still closing in on the target after the grace period is this app's ramp.
        var seconds = 0.0;
        for (var belt = 3.0; belt <= 7.0; belt += 0.5)
        {
            seconds += 2;
            Assert.Equal(FollowAction.Hold, Tick(7.0, belt, seconds));
        }
    }

    [Fact]
    public void Decide_BeltStoppedOnConsole_Holds()
    {
        follower.OnConnected();
        Tick(5.0, 5.0, 0);

        Assert.Equal(FollowAction.Hold, Tick(5.0, 0.0, 2));
        Assert.Equal(FollowAction.Hold, Tick(5.0, 0.0, 4));
        Assert.Equal(FollowAction.Hold, Tick(5.0, 0.0, 6));
        Assert.Equal(FollowAction.Hold, Tick(5.0, 0.0, 8));
    }

    [Fact]
    public void Decide_RepeatedReading_DoesNotCountAsAnotherSteadyTick()
    {
        follower.OnConnected();
        Tick(5.0, 5.0, 0);
        var reading = new BeltReading(7.0, At(2));

        for (var tick = 1; tick <= 5; tick++)
        {
            Assert.Equal(FollowAction.Hold, follower.Decide(5.0, reading, Min, Step, At(2 * tick)));
        }

        Assert.Equal(FollowAction.Hold, Tick(5.0, 7.0, 12));
        Assert.Equal(FollowAction.AdoptBeltSpeed, Tick(5.0, 7.0, 14));
    }

    [Fact]
    public void Decide_GapInReadings_RestartsSteadyCount()
    {
        follower.OnConnected();
        Tick(5.0, 5.0, 0);
        Tick(5.0, 7.0, 2);
        Tick(5.0, 7.0, 4);

        Assert.Equal(FollowAction.Hold, follower.Decide(5.0, null, Min, Step, At(6)));
        Assert.Equal(FollowAction.Hold, Tick(5.0, 7.0, 8));
        Assert.Equal(FollowAction.Hold, Tick(5.0, 7.0, 10));
        Assert.Equal(FollowAction.AdoptBeltSpeed, Tick(5.0, 7.0, 12));
    }

    [Fact]
    public void OnBeltSpeedAdopted_NewTargetAtBelt_Holds()
    {
        follower.OnConnected();
        Tick(5.0, 5.0, 0);
        Tick(5.0, 7.0, 2);
        Tick(5.0, 7.0, 4);
        Assert.Equal(FollowAction.AdoptBeltSpeed, Tick(5.0, 7.0, 6));

        follower.OnBeltSpeedAdopted();

        Assert.Equal(FollowAction.Hold, Tick(7.0, 7.0, 8));
        Assert.Equal(FollowAction.Hold, Tick(7.0, 7.0, 10));
        Assert.Equal(FollowAction.Hold, Tick(7.0, 7.0, 12));
    }

    [Theory]
    [InlineData(null, false)] // no recent reading
    [InlineData(5.2, false)] // within half a step of the target
    [InlineData(0.5, false)] // below the minimum: stopped, nothing to start from
    [InlineData(6.0, true)]
    [InlineData(4.0, true)]
    public void BeltOverridesTarget_AfterGrace(double? beltKmh, bool expected)
    {
        follower.OnConnected();

        Assert.Equal(expected, follower.BeltOverridesTarget(5.0, beltKmh, Min, Step, T0));
    }

    [Fact]
    public void BeltOverridesTarget_WithinOwnChangeGrace_IsFalse()
    {
        follower.OnConnected();
        follower.OnOwnTargetSet(T0);

        Assert.False(follower.BeltOverridesTarget(7.0, 5.0, Min, Step, At(5)));
        Assert.True(follower.BeltOverridesTarget(7.0, 5.0, Min, Step, At(TreadmillSpeedFollower.OwnChangeGrace.TotalSeconds)));
    }
}
