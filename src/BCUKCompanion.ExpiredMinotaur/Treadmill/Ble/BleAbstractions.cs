namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

/// <summary>
/// A connected FTMS treadmill as <see cref="TreadmillClient"/> uses it. <see cref="FtmsDevice"/>
/// implements it; the client depends only on this, so its connection lifecycle can be tested
/// without a treadmill.
/// </summary>
public interface ITreadmillDevice : IControlPointWriter, IDisposable
{
    /// <summary>Whether the treadmill is connected right now.</summary>
    bool IsConnected { get; }

    /// <summary>Setup problems from the last <see cref="ConnectAsync"/>, for the status line.</summary>
    IReadOnlyList<string> LastConnectWarnings { get; }

    /// <summary>Raised with each Treadmill Data notification's raw bytes.</summary>
    event EventHandler<byte[]>? TreadmillDataReceived;

    /// <summary>Raised when the connected treadmill drops the connection.</summary>
    event EventHandler? ConnectionLost;

    /// <summary>Connects to the treadmill at <paramref name="bluetoothAddress"/> and subscribes to its data.</summary>
    /// <param name="bluetoothAddress">The address a scan found.</param>
    /// <returns>A task completing with whether the device and its Fitness Machine service were found.</returns>
    Task<bool> ConnectAsync(ulong bluetoothAddress);

    /// <summary>Reads the treadmill's Supported Speed Range.</summary>
    /// <returns>A task completing with the minimum, maximum and increment in km/h, or null if the range couldn't be read.</returns>
    Task<(double MinKmh, double MaxKmh, double IncrementKmh)?> ReadSupportedSpeedRangeAsync();
}

/// <summary>One GATT characteristic: subscribe to it, write it and read it, in plain bytes.</summary>
public interface IGattCharacteristicPort
{
    /// <summary>Whether the characteristic accepts a write with response (otherwise only without).</summary>
    bool SupportsWriteWithResponse { get; }

    /// <summary>Raised with each notification's or indication's bytes, once subscribed.</summary>
    event EventHandler<byte[]>? ValueChanged;

    /// <summary>Writes the characteristic's CCCD to turn on notifications or indications.</summary>
    /// <param name="indicate">True for indications, false for notifications.</param>
    /// <returns>A task completing with null on success, or the failing GATT status's name.</returns>
    Task<string?> SubscribeAsync(bool indicate);

    /// <summary>Writes a value to the characteristic.</summary>
    /// <param name="data">The bytes to write.</param>
    /// <param name="withResponse">Whether to write with response.</param>
    /// <returns>A task completing with whether the GATT write succeeded.</returns>
    Task<bool> WriteAsync(byte[] data, bool withResponse);

    /// <summary>Reads the characteristic's value.</summary>
    /// <returns>A task completing with the value's bytes, or null if the read didn't succeed.</returns>
    Task<byte[]?> ReadAsync();

    /// <summary>Stops raising <see cref="ValueChanged"/> and detaches from the underlying characteristic.</summary>
    void Detach();
}

/// <summary>A BLE connection to one device's Fitness Machine service.</summary>
public interface IBleLink : IDisposable
{
    /// <summary>Whether the device reports itself connected right now.</summary>
    bool IsConnected { get; }

    /// <summary>Raised when the device's connection drops.</summary>
    event EventHandler? Disconnected;

    /// <summary>Finds one characteristic of the Fitness Machine service.</summary>
    /// <param name="characteristicUuid">The characteristic's full 128-bit UUID.</param>
    /// <returns>A task completing with the characteristic, or null if it's missing.</returns>
    Task<IGattCharacteristicPort?> GetCharacteristicAsync(Guid characteristicUuid);
}

/// <summary>Opens a <see cref="IBleLink"/> to a device's Fitness Machine service.</summary>
public interface IBleConnector
{
    /// <summary>Connects to the device and finds its Fitness Machine service.</summary>
    /// <param name="bluetoothAddress">The device's 48-bit Bluetooth address.</param>
    /// <returns>A task completing with the link, or with null and the reason it failed.</returns>
    Task<(IBleLink? Link, string? Failure)> ConnectAsync(ulong bluetoothAddress);
}

/// <summary>One BLE advertisement: who sent it and which services it lists.</summary>
/// <param name="BluetoothAddress">The advertiser's 48-bit Bluetooth address.</param>
/// <param name="ServiceUuids">The service UUIDs the advertisement lists.</param>
public sealed record BleAdvertisement(ulong BluetoothAddress, IReadOnlyCollection<Guid> ServiceUuids);

/// <summary>Watches for BLE advertisements between <see cref="Start"/> and <see cref="Stop"/>.</summary>
public interface IAdvertisementWatcher
{
    /// <summary>Raised (on a background thread) for each advertisement received.</summary>
    event EventHandler<BleAdvertisement>? Received;

    /// <summary>Starts an active scan.</summary>
    void Start();

    /// <summary>Stops scanning.</summary>
    void Stop();
}
