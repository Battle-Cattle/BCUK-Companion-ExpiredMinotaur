namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

/// <summary>
/// Writes a command to the FTMS Control Point and waits for its indication. <see cref="FtmsDevice"/>
/// implements it over BLE; <see cref="ControlPointService"/> depends only on this, so its command
/// encoding and retry logic can be tested without a treadmill.
/// </summary>
public interface IControlPointWriter
{
    /// <summary>Writes <paramref name="data"/> (opcode first) and waits for the matching indication.</summary>
    /// <param name="data">The opcode followed by its parameters.</param>
    /// <returns>Whether the write went through and, if an indication arrived, its result code.</returns>
    Task<ControlPointWriteResult> WriteControlPointAndAwaitAsync(byte[] data);
}
