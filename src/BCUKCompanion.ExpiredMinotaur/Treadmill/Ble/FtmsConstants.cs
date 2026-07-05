namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

/// <summary>
/// FTMS (Fitness Machine Service) UUIDs and Control Point opcodes, per Bluetooth SIG FTMS
/// spec. Confirmed present on T-TREADMILLY04 via nRF Connect on 2026-07-04.
/// </summary>
public static class FtmsConstants
{
    // --- Service ---
    public static readonly Guid FitnessMachineService = ToGuid(0x1826);

    // --- Characteristics ---
    public static readonly Guid FitnessMachineFeature = ToGuid(0x2ACC);
    public static readonly Guid TreadmillData = ToGuid(0x2ACD);
    public static readonly Guid TrainingStatus = ToGuid(0x2AD3);
    public static readonly Guid SupportedSpeedRange = ToGuid(0x2AD4);
    public static readonly Guid SupportedInclinationRange = ToGuid(0x2AD5);
    public static readonly Guid SupportedResistanceLevelRange = ToGuid(0x2AD6);
    public static readonly Guid SupportedHeartRateRange = ToGuid(0x2AD7);
    public static readonly Guid SupportedPowerRange = ToGuid(0x2AD8);
    public static readonly Guid FitnessMachineControlPoint = ToGuid(0x2AD9);
    public static readonly Guid FitnessMachineStatus = ToGuid(0x2ADA);

    // --- Control Point Opcodes (write to FitnessMachineControlPoint) ---
    public static class Opcode
    {
        public const byte RequestControl = 0x00;
        public const byte Reset = 0x01;
        public const byte SetTargetSpeed = 0x02;
        public const byte SetTargetInclination = 0x03;
        public const byte StartOrResume = 0x07;
        public const byte StopOrPause = 0x08;
    }

    // --- Control Point Response (indication payload) ---
    public const byte ResponseCode = 0x80;
    public const byte ResultSuccess = 0x01;
    public const byte ResultOpCodeNotSupported = 0x02;
    public const byte ResultInvalidParameter = 0x03;
    public const byte ResultOperationFailed = 0x04;
    public const byte ResultControlNotPermitted = 0x05;

    // --- Known device limits (T-TREADMILLY04, read from 0x2AD4) ---
    public const double MinSpeedKmh = 1.0;
    public const double MaxSpeedKmh = 12.0;
    public const double SpeedStepKmh = 0.5;

    private static Guid ToGuid(ushort shortUuid)
    {
        // Bluetooth Base UUID: 0000xxxx-0000-1000-8000-00805F9B34FB
        return new Guid($"0000{shortUuid:X4}-0000-1000-8000-00805f9b34fb");
    }
}
