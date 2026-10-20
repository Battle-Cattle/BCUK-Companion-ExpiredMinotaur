using System.Globalization;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillNudgeSpeedActionTests
{
    [Theory]
    [InlineData(1.0, "Speed up by 1.0 km/h")]
    [InlineData(0.5, "Speed up by 0.5 km/h")]
    [InlineData(-1.5, "Slow down by 1.5 km/h")]
    public void Describe(double delta, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, new TreadmillNudgeSpeedAction { DeltaKmh = delta }.Describe(new EmptyActionContext()));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Validate_ZeroDelta_IsRejected()
    {
        Assert.Equal(["Delta must be non-zero."], new TreadmillNudgeSpeedAction().Validate(new EmptyActionContext()));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(-0.5)]
    public void Validate_NonZeroDelta_IsValidWhileDisconnected(double delta)
    {
        Assert.Empty(new TreadmillNudgeSpeedAction { DeltaKmh = delta }.Validate(new EmptyActionContext()));
    }

    [Fact]
    public async Task ExecuteAsync_WithoutTreadmillContext_Throws()
    {
        var action = new TreadmillNudgeSpeedAction { DeltaKmh = 1.0 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => action.ExecuteAsync(new EmptyActionContext(), CancellationToken.None));
    }

    [Fact]
    public void Kind_MatchesActionKind()
    {
        Assert.Equal(TreadmillNudgeSpeedAction.ActionKind, new TreadmillNudgeSpeedAction().Kind);
        Assert.Equal("treadmill.nudgeSpeed", TreadmillNudgeSpeedAction.ActionKind);
    }
}
