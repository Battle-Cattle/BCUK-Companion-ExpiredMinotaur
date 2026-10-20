namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

/// <summary>
/// FTMS (Fitness Machine Service) UUIDs and Control Point opcodes, per Bluetooth SIG FTMS
/// spec. Confirmed present on T-TREADMILLY04 via nRF Connect on 2026-07-04.
/// </summary>
public static class FtmsConstants
{
    // --- Service ---
    /// <summary>Fitness Machine Service (0x1826), the GATT service every FTMS characteristic below belongs to.</summary>
    public static readonly Guid FitnessMachineService = ToGuid(0x1826);

    // --- Characteristics ---
    /// <summary>Fitness Machine Feature (0x2ACC): which data fields and target settings the machine supports.</summary>
    public static readonly Guid FitnessMachineFeature = ToGuid(0x2ACC);
    /// <summary>Treadmill Data (0x2ACD): notified speed, distance, energy and time readings (see <see cref="TreadmillDataParser"/>).</summary>
    public static readonly Guid TreadmillData = ToGuid(0x2ACD);
    /// <summary>Training Status (0x2AD3): the machine's current training state (idle, warming up, …).</summary>
    public static readonly Guid TrainingStatus = ToGuid(0x2AD3);
    /// <summary>Supported Speed Range (0x2AD4): minimum, maximum and step speed, read on connect.</summary>
    public static readonly Guid SupportedSpeedRange = ToGuid(0x2AD4);
    /// <summary>Supported Inclination Range (0x2AD5).</summary>
    public static readonly Guid SupportedInclinationRange = ToGuid(0x2AD5);
    /// <summary>Supported Resistance Level Range (0x2AD6).</summary>
    public static readonly Guid SupportedResistanceLevelRange = ToGuid(0x2AD6);
    /// <summary>Supported Heart Rate Range (0x2AD7).</summary>
    public static readonly Guid SupportedHeartRateRange = ToGuid(0x2AD7);
    /// <summary>Supported Power Range (0x2AD8).</summary>
    public static readonly Guid SupportedPowerRange = ToGuid(0x2AD8);
    /// <summary>Fitness Machine Control Point (0x2AD9): written with an <see cref="Opcode"/>, answered by an indication.</summary>
    public static readonly Guid FitnessMachineControlPoint = ToGuid(0x2AD9);
    /// <summary>Fitness Machine Status (0x2ADA): notified state changes, such as Control Permission Lost.</summary>
    public static readonly Guid FitnessMachineStatus = ToGuid(0x2ADA);

    // --- Control Point Opcodes (write to FitnessMachineControlPoint) ---
    /// <summary>Control Point opcodes: the first byte of every write to <see cref="FitnessMachineControlPoint"/>.</summary>
    public static class Opcode
    {
        /// <summary>Asks the machine for control; every other opcode is rejected until this is accepted.</summary>
        public const byte RequestControl = 0x00;
        /// <summary>Resets the machine, which also revokes control.</summary>
        public const byte Reset = 0x01;
        /// <summary>Sets the target speed: followed by a little-endian uint16 in units of 0.01 km/h.</summary>
        public const byte SetTargetSpeed = 0x02;
        /// <summary>Sets the target inclination.</summary>
        public const byte SetTargetInclination = 0x03;
        /// <summary>Starts or resumes the belt.</summary>
        public const byte StartOrResume = 0x07;
        /// <summary>Stops (parameter 0x01) or pauses (parameter 0x02) the belt.</summary>
        public const byte StopOrPause = 0x08;
    }

    // --- Control Point Response (indication payload) ---
    /// <summary>First byte of every Control Point indication; the echoed opcode and a result code follow.</summary>
    public const byte ResponseCode = 0x80;
    /// <summary>Result code: the operation succeeded.</summary>
    public const byte ResultSuccess = 0x01;
    /// <summary>Result code: the machine doesn't support the opcode.</summary>
    public const byte ResultOpCodeNotSupported = 0x02;
    /// <summary>Result code: a parameter was out of range or malformed.</summary>
    public const byte ResultInvalidParameter = 0x03;
    /// <summary>Result code: the machine couldn't carry the operation out.</summary>
    public const byte ResultOperationFailed = 0x04;
    /// <summary>Result code: this client doesn't have control (never requested, or since revoked).</summary>
    public const byte ResultControlNotPermitted = 0x05;

    // --- Known device limits (T-TREADMILLY04, read from 0x2AD4) ---
    /// <summary>Default minimum speed, used until the device's own Supported Speed Range is read.</summary>
    public const double MinSpeedKmh = 1.0;
    /// <summary>Default maximum speed, used until the device's own Supported Speed Range is read.</summary>
    public const double MaxSpeedKmh = 12.0;
    /// <summary>Default speed step, used until the device's own Supported Speed Range is read.</summary>
    public const double SpeedStepKmh = 0.5;

    /// <summary>Expands a 16-bit Bluetooth SIG UUID into the full 128-bit form on the Bluetooth Base UUID.</summary>
    private static Guid ToGuid(ushort shortUuid)
    {
        // Bluetooth Base UUID: 0000xxxx-0000-1000-8000-00805F9B34FB
        return new Guid($"0000{shortUuid:X4}-0000-1000-8000-00805f9b34fb");
    }
}
