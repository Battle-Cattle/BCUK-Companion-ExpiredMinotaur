using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillDataParserTests
{
    [Fact]
    public void Parse_NoFlags_ReadsOnlyInstantaneousSpeed()
    {
        var snapshot = TreadmillDataParser.Parse([0x00, 0x00, 0x26, 0x02]);

        Assert.Equal(5.5, snapshot.InstantaneousSpeedKmh);
        Assert.Null(snapshot.AverageSpeedKmh);
        Assert.Null(snapshot.TotalDistanceMeters);
        Assert.Null(snapshot.TotalEnergyKcal);
        Assert.Null(snapshot.HeartRateBpm);
        Assert.Null(snapshot.ElapsedTimeSeconds);
        Assert.Null(snapshot.RemainingTimeSeconds);
    }

    [Fact]
    public void Parse_MoreDataFlag_HasNoInstantaneousSpeed()
    {
        // Bit 0 ("More Data") means this notification carries no speed: the next field is the average.
        var snapshot = TreadmillDataParser.Parse([0x03, 0x00, 0xF4, 0x01]);

        Assert.Null(snapshot.InstantaneousSpeedKmh);
        Assert.Equal(5.0, snapshot.AverageSpeedKmh);
    }

    [Fact]
    public void Parse_AverageSpeedAndDistance_ReadsThemInOrder()
    {
        var snapshot = TreadmillDataParser.Parse([0x06, 0x00, 0x26, 0x02, 0xF4, 0x01, 0x10, 0x27, 0x01]);

        Assert.Equal(5.5, snapshot.InstantaneousSpeedKmh);
        Assert.Equal(5.0, snapshot.AverageSpeedKmh);
        Assert.Equal((double)0x012710, snapshot.TotalDistanceMeters);
    }

    [Fact]
    public void Parse_UntrackedFields_AreSkipped()
    {
        // Inclination + ramp angle (bit 3), elevation gain (bit 4), both paces (bits 5, 6), then elapsed time (bit 10).
        byte[] data =
        [
            0x78, 0x04,
            0x26, 0x02,
            0xAA, 0xAA, 0xAA, 0xAA,
            0xBB, 0xBB, 0xBB, 0xBB,
            0xCC,
            0xDD,
            0x3C, 0x00,
        ];

        var snapshot = TreadmillDataParser.Parse(data);

        Assert.Equal(5.5, snapshot.InstantaneousSpeedKmh);
        Assert.Equal(60, snapshot.ElapsedTimeSeconds);
    }

    [Fact]
    public void Parse_ExpendedEnergyAndHeartRate_TreatsNotAvailableValuesAsNull()
    {
        byte[] data = [0x80, 0x01, 0x26, 0x02, 0x64, 0x00, 0xFF, 0xFF, 0x05, 0x8C];

        var snapshot = TreadmillDataParser.Parse(data);

        Assert.Equal(100, snapshot.TotalEnergyKcal);
        Assert.Null(snapshot.EnergyPerHourKcal);
        Assert.Equal(5, snapshot.EnergyPerMinuteKcal);
        Assert.Equal(140, snapshot.HeartRateBpm);
    }

    [Fact]
    public void Parse_AllEnergyNotAvailable_LeavesEveryEnergyFieldNull()
    {
        var snapshot = TreadmillDataParser.Parse([0x80, 0x00, 0x26, 0x02, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);

        Assert.Null(snapshot.TotalEnergyKcal);
        Assert.Null(snapshot.EnergyPerHourKcal);
        Assert.Null(snapshot.EnergyPerMinuteKcal);
    }

    [Fact]
    public void Parse_MetabolicEquivalentThenTimes_ReadsElapsedAndRemaining()
    {
        // Metabolic equivalent (bit 9, skipped), elapsed (bit 10) and remaining (bit 11) time.
        var snapshot = TreadmillDataParser.Parse([0x00, 0x0E, 0x26, 0x02, 0x0A, 0x2C, 0x01, 0x58, 0x02]);

        Assert.Equal(300, snapshot.ElapsedTimeSeconds);
        Assert.Equal(600, snapshot.RemainingTimeSeconds);
    }
}
