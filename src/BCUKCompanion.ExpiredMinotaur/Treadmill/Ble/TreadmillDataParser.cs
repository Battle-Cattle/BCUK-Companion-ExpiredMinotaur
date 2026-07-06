namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

public class TreadmillDataSnapshot
{
    public double? InstantaneousSpeedKmh { get; set; }
    public double? AverageSpeedKmh { get; set; }
    public double? TotalDistanceMeters { get; set; }
    public int? TotalEnergyKcal { get; set; }
    public int? EnergyPerHourKcal { get; set; }
    public int? EnergyPerMinuteKcal { get; set; }
    public int? HeartRateBpm { get; set; }
    public int? ElapsedTimeSeconds { get; set; }
    public int? RemainingTimeSeconds { get; set; }
}

/// <summary>
/// Parses Treadmill Data (0x2ACD) notifications per FTMS spec section 4.9.
/// Field presence is driven by the 2-byte flags field at the start of the
/// payload; present fields appear afterward in a fixed order.
/// </summary>
public static class TreadmillDataParser
{
    public static TreadmillDataSnapshot Parse(byte[] data)
    {
        var snapshot = new TreadmillDataSnapshot();
        int i = 0;
        ushort flags = ReadUInt16(data, ref i);

        bool moreDataMeansNoSpeed = (flags & 0x0001) != 0;
        bool avgSpeedPresent = (flags & 0x0002) != 0;
        bool totalDistancePresent = (flags & 0x0004) != 0;
        bool inclinationPresent = (flags & 0x0008) != 0;
        bool elevationGainPresent = (flags & 0x0010) != 0;
        bool instPacePresent = (flags & 0x0020) != 0;
        bool avgPacePresent = (flags & 0x0040) != 0;
        bool expendedEnergyPresent = (flags & 0x0080) != 0;
        bool heartRatePresent = (flags & 0x0100) != 0;
        bool metabolicEquivPresent = (flags & 0x0200) != 0;
        bool elapsedTimePresent = (flags & 0x0400) != 0;
        bool remainingTimePresent = (flags & 0x0800) != 0;

        if (!moreDataMeansNoSpeed)
            snapshot.InstantaneousSpeedKmh = ReadUInt16(data, ref i) / 100.0;

        if (avgSpeedPresent)
            snapshot.AverageSpeedKmh = ReadUInt16(data, ref i) / 100.0;

        if (totalDistancePresent)
            snapshot.TotalDistanceMeters = ReadUInt24(data, ref i);

        if (inclinationPresent)
            i += 4; // Inclination (2) + Ramp Angle (2) — not tracked yet

        if (elevationGainPresent)
            i += 4; // Positive (2) + Negative (2) — not tracked yet

        if (instPacePresent)
            i += 1; // Instantaneous Pace — not tracked yet

        if (avgPacePresent)
            i += 1; // Average Pace — not tracked yet

        if (expendedEnergyPresent)
            ParseExpendedEnergy(data, ref i, snapshot);

        if (heartRatePresent)
        {
            snapshot.HeartRateBpm = data[i]; i += 1;
        }

        if (metabolicEquivPresent)
            i += 1; // Metabolic Equivalent — not tracked yet

        if (elapsedTimePresent)
            snapshot.ElapsedTimeSeconds = ReadUInt16(data, ref i);

        if (remainingTimePresent)
            snapshot.RemainingTimeSeconds = ReadUInt16(data, ref i);

        // Force on Belt and Power Output (bit 12) intentionally not parsed yet.

        return snapshot;
    }

    // Per FTMS spec, a device that can't calculate one of these sends the sentinel
    // "data not available" value (0xFFFF / 0xFF) rather than omitting the field.
    private static void ParseExpendedEnergy(byte[] data, ref int i, TreadmillDataSnapshot snapshot)
    {
        var totalEnergy = ReadUInt16(data, ref i);
        var energyPerHour = ReadUInt16(data, ref i);
        var energyPerMinute = data[i]; i += 1;
        snapshot.TotalEnergyKcal = totalEnergy == 0xFFFF ? null : totalEnergy;
        snapshot.EnergyPerHourKcal = energyPerHour == 0xFFFF ? null : energyPerHour;
        snapshot.EnergyPerMinuteKcal = energyPerMinute == 0xFF ? null : energyPerMinute;
    }

    private static ushort ReadUInt16(byte[] data, ref int i)
    {
        ushort value = (ushort)(data[i] | (data[i + 1] << 8));
        i += 2;
        return value;
    }

    private static int ReadUInt24(byte[] data, ref int i)
    {
        int value = data[i] | (data[i + 1] << 8) | (data[i + 2] << 16);
        i += 3;
        return value;
    }
}
