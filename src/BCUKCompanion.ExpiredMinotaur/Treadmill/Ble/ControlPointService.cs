namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

/// <summary>
/// Wraps the FTMS Control Point write sequence: request control,
/// start/resume, and set target speed. Speed is sent as km/h * 100,
/// little-endian uint16, per FTMS spec section 4.16.
/// </summary>
public class ControlPointService
{
    private readonly IControlPointWriter _device;

    // Defaults match the range confirmed via nRF Connect; TreadmillClient
    // overwrites these after reading the device's actual Supported Speed
    // Range (0x2AD4) on connect, since that range can vary by setup/unit.

    /// <summary>The lowest speed <see cref="SetTargetSpeedAsync"/> accepts, in km/h.</summary>
    public double MinSpeedKmh { get; set; } = FtmsConstants.MinSpeedKmh;

    /// <summary>The highest speed <see cref="SetTargetSpeedAsync"/> accepts, in km/h.</summary>
    public double MaxSpeedKmh { get; set; } = FtmsConstants.MaxSpeedKmh;

    /// <summary>Creates a service that sends its commands through <paramref name="device"/>.</summary>
    /// <param name="device">The Control Point to write to (the connected <see cref="FtmsDevice"/>).</param>
    public ControlPointService(IControlPointWriter device)
    {
        _device = device;
    }

    /// <summary>Asks the treadmill for control, which it must accept before any other command.</summary>
    public Task<ControlPointWriteResult> RequestControlAsync() =>
        _device.WriteControlPointAndAwaitAsync(new[] { FtmsConstants.Opcode.RequestControl });

    /// <summary>Starts or resumes the belt.</summary>
    public Task<ControlPointWriteResult> StartOrResumeAsync() =>
        _device.WriteControlPointAndAwaitAsync(new[] { FtmsConstants.Opcode.StartOrResume });

    /// <summary>Stops the belt (opcode 0x08 with parameter 0x01 = Stop; 0x02 would be Pause).</summary>
    public Task<ControlPointWriteResult> StopAsync() =>
        _device.WriteControlPointAndAwaitAsync(new byte[] { FtmsConstants.Opcode.StopOrPause, 0x01 });

    /// <summary>
    /// Sets the target speed, re-requesting control and retrying once if the treadmill reports
    /// that control was lost.
    /// </summary>
    /// <param name="speedKmh">The new target, between <see cref="MinSpeedKmh"/> and <see cref="MaxSpeedKmh"/>.</param>
    /// <returns>The result of the last write sent.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="speedKmh"/> is outside the supported range.</exception>
    public async Task<ControlPointWriteResult> SetTargetSpeedAsync(double speedKmh)
    {
        if (speedKmh < MinSpeedKmh || speedKmh > MaxSpeedKmh)
            throw new ArgumentOutOfRangeException(nameof(speedKmh),
                $"Speed must be between {MinSpeedKmh} and {MaxSpeedKmh} km/h");

        ushort raw = (ushort)Math.Round(speedKmh * 100);
        byte lo = (byte)(raw & 0xFF);
        byte hi = (byte)((raw >> 8) & 0xFF);
        var command = new[] { FtmsConstants.Opcode.SetTargetSpeed, lo, hi };

        var result = await _device.WriteControlPointAndAwaitAsync(command);

        // Per FTMS, the server revokes control after a Reset or when it sends Control
        // Permission Lost (Fitness Machine Status 0xFF), e.g. once its own console takes
        // over; every later write then fails with Control Not Permitted until the client
        // requests control again. Re-request once and retry rather than failing forever.
        if (result.IndicationReceived && result.ResultCode == FtmsConstants.ResultControlNotPermitted
            && (await RequestControlAsync()).Accepted)
        {
            result = await _device.WriteControlPointAndAwaitAsync(command);
        }

        return result;
    }
}
