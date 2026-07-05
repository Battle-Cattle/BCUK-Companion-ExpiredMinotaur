namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

/// <summary>
/// Wraps the FTMS Control Point write sequence: request control,
/// start/resume, and set target speed. Speed is sent as km/h * 100,
/// little-endian uint16, per FTMS spec section 4.16.
/// </summary>
public class ControlPointService
{
    private readonly FtmsDevice _device;

    // Defaults match the range confirmed via nRF Connect; TreadmillClient
    // overwrites these after reading the device's actual Supported Speed
    // Range (0x2AD4) on connect, since that range can vary by setup/unit.
    public double MinSpeedKmh { get; set; } = FtmsConstants.MinSpeedKmh;
    public double MaxSpeedKmh { get; set; } = FtmsConstants.MaxSpeedKmh;

    public ControlPointService(FtmsDevice device)
    {
        _device = device;
    }

    public Task<ControlPointWriteResult> RequestControlAsync() =>
        _device.WriteControlPointAndAwaitAsync(new[] { FtmsConstants.Opcode.RequestControl });

    public Task<ControlPointWriteResult> StartOrResumeAsync() =>
        _device.WriteControlPointAndAwaitAsync(new[] { FtmsConstants.Opcode.StartOrResume });

    // 0x01 = Stop, 0x02 = Pause, per spec parameter for opcode 0x08.
    public Task<ControlPointWriteResult> StopAsync() =>
        _device.WriteControlPointAndAwaitAsync(new byte[] { FtmsConstants.Opcode.StopOrPause, 0x01 });

    public Task<ControlPointWriteResult> SetTargetSpeedAsync(double speedKmh)
    {
        if (speedKmh < MinSpeedKmh || speedKmh > MaxSpeedKmh)
            throw new ArgumentOutOfRangeException(nameof(speedKmh),
                $"Speed must be between {MinSpeedKmh} and {MaxSpeedKmh} km/h");

        ushort raw = (ushort)Math.Round(speedKmh * 100);
        byte lo = (byte)(raw & 0xFF);
        byte hi = (byte)((raw >> 8) & 0xFF);

        return _device.WriteControlPointAndAwaitAsync(new[] { FtmsConstants.Opcode.SetTargetSpeed, lo, hi });
    }
}
