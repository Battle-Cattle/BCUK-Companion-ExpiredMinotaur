using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillSpeedRangeTests
{
    [Fact]
    public void Default_IsTheHardcodedRange()
    {
        Assert.Equal(
            new TreadmillSpeedRange(FtmsConstants.MinSpeedKmh, FtmsConstants.MaxSpeedKmh, FtmsConstants.SpeedStepKmh),
            TreadmillSpeedRange.Default);
    }

    [Fact]
    public void FromDevice_UsesAValidReportedRange()
    {
        var (range, warning) = TreadmillSpeedRange.FromDevice((2.0, 8.0, 0.5));

        Assert.Equal(new TreadmillSpeedRange(2.0, 8.0, 0.5), range);
        Assert.Null(warning);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    public void FromDevice_FallsBackToTheDefaultOnAnInvalidIncrement(double increment)
    {
        var (range, warning) = TreadmillSpeedRange.FromDevice((2.0, 8.0, increment));

        Assert.Equal(TreadmillSpeedRange.Default, range);
        Assert.NotNull(warning);
        Assert.Contains("invalid increment", warning);
        Assert.Contains($"using default step {FtmsConstants.SpeedStepKmh:0.0} km/h", warning);
    }

    [Fact]
    public void FromDevice_FallsBackToTheDefaultWhenTheMinimumIsAboveTheMaximum()
    {
        var (range, warning) = TreadmillSpeedRange.FromDevice((8.0, 2.0, 0.5));

        Assert.Equal(TreadmillSpeedRange.Default, range);
        Assert.NotNull(warning);
        Assert.Contains("minimum above its maximum (8.0-2.0 km/h)", warning);

        // Without the fallback, Math.Clamp(…, 8.0, 2.0) throws ArgumentException on every nudge.
        Assert.Equal(range.SnapAndClamp(5.0), Math.Clamp(5.0, range.MinKmh, range.MaxKmh));
    }

    [Fact]
    public void FromDevice_AcceptsARangeWhoseMinimumEqualsItsMaximum()
    {
        var (range, warning) = TreadmillSpeedRange.FromDevice((4.0, 4.0, 0.5));

        Assert.Equal(new TreadmillSpeedRange(4.0, 4.0, 0.5), range);
        Assert.Null(warning);
    }

    [Fact]
    public void FromDevice_FallsBackToTheDefaultWhenTheRangeCouldNotBeRead()
    {
        var (range, warning) = TreadmillSpeedRange.FromDevice(null);

        Assert.Equal(TreadmillSpeedRange.Default, range);
        Assert.Equal(
            $"Could not read Supported Speed Range (0x2AD4) — using defaults {FtmsConstants.MinSpeedKmh:0.0}-{FtmsConstants.MaxSpeedKmh:0.0} km/h.",
            warning);
    }

    [Theory]
    [InlineData(3.14, 3.0)]
    [InlineData(3.26, 3.5)]
    [InlineData(0.1, 1.0)]
    [InlineData(12.0, 8.0)]
    public void SnapAndClamp_RoundsToTheStepAndClampsToTheRange(double speed, double expected)
    {
        var range = new TreadmillSpeedRange(1.0, 8.0, 0.5);

        Assert.Equal(expected, range.SnapAndClamp(speed), 6);
    }
}
