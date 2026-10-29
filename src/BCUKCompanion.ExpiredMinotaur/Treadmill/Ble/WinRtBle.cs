using System.Diagnostics.CodeAnalysis;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

// The classes below only forward to Windows' Bluetooth APIs, which need a real radio and a
// treadmill to run, so no test can reach them; they're left out of coverage. Every decision made
// on top of them (FtmsDevice, FitnessMachineScanner) works through the interfaces in
// BleAbstractions.cs and is tested with fakes.
namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

/// <summary>Connects to a device over Windows' BLE APIs and finds its Fitness Machine service.</summary>
[ExcludeFromCodeCoverage(Justification = "Thin wrapper over Windows' Bluetooth APIs; needs real hardware.")]
internal sealed class WinRtBleConnector : IBleConnector
{
    /// <inheritdoc/>
    public async Task<(IBleLink? Link, string? Failure)> ConnectAsync(ulong bluetoothAddress)
    {
        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
        if (device == null)
        {
            return (null, "FromBluetoothAddressAsync returned null.");
        }

        var servicesResult = await device.GetGattServicesForUuidAsync(FtmsConstants.FitnessMachineService);
        if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
        {
            device.Dispose();
            return (null, $"Fitness Machine service (0x1826) not found (status: {servicesResult.Status}).");
        }

        return (new WinRtBleLink(device, servicesResult.Services[0]), null);
    }
}

/// <summary>A Windows BLE connection to one device's Fitness Machine service.</summary>
[ExcludeFromCodeCoverage(Justification = "Thin wrapper over Windows' Bluetooth APIs; needs real hardware.")]
internal sealed class WinRtBleLink : IBleLink
{
    private readonly BluetoothLEDevice device;
    private readonly GattDeviceService service;
    private readonly TypedEventHandler<BluetoothLEDevice, object> connectionStatusHandler;

    /// <summary>Wraps a connected device and its Fitness Machine service, watching for disconnects.</summary>
    /// <param name="device">The connected device.</param>
    /// <param name="service">Its Fitness Machine service.</param>
    public WinRtBleLink(BluetoothLEDevice device, GattDeviceService service)
    {
        this.device = device;
        this.service = service;
        connectionStatusHandler = (sender, _) =>
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        };
        device.ConnectionStatusChanged += connectionStatusHandler;
    }

    /// <inheritdoc/>
    public event EventHandler? Disconnected;

    /// <inheritdoc/>
    public bool IsConnected => device.ConnectionStatus == BluetoothConnectionStatus.Connected;

    /// <inheritdoc/>
    public async Task<IGattCharacteristicPort?> GetCharacteristicAsync(Guid characteristicUuid)
    {
        var result = await service.GetCharacteristicsForUuidAsync(characteristicUuid);
        return result.Status == GattCommunicationStatus.Success && result.Characteristics.Count > 0
            ? new WinRtGattCharacteristic(result.Characteristics[0])
            : null;
    }

    /// <summary>Stops watching the connection and releases the service and device.</summary>
    public void Dispose()
    {
        device.ConnectionStatusChanged -= connectionStatusHandler;
        service.Dispose();
        device.Dispose();
    }
}

/// <summary>A Windows GATT characteristic, read and written as plain bytes.</summary>
[ExcludeFromCodeCoverage(Justification = "Thin wrapper over Windows' Bluetooth APIs; needs real hardware.")]
internal sealed class WinRtGattCharacteristic : IGattCharacteristicPort
{
    private readonly GattCharacteristic characteristic;
    private readonly TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> valueHandler;
    private bool attached;

    /// <summary>Wraps <paramref name="characteristic"/>.</summary>
    /// <param name="characteristic">The characteristic to read, write and subscribe to.</param>
    public WinRtGattCharacteristic(GattCharacteristic characteristic)
    {
        this.characteristic = characteristic;
        valueHandler = (_, args) => ValueChanged?.Invoke(this, ReadBuffer(args.CharacteristicValue));
    }

    /// <inheritdoc/>
    public event EventHandler<byte[]>? ValueChanged;

    /// <inheritdoc/>
    public bool SupportsWriteWithResponse =>
        characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write);

    /// <inheritdoc/>
    public async Task<string?> SubscribeAsync(bool indicate)
    {
        characteristic.ValueChanged += valueHandler;
        attached = true;
        var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(indicate
            ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
            : GattClientCharacteristicConfigurationDescriptorValue.Notify);
        return status == GattCommunicationStatus.Success ? null : status.ToString();
    }

    /// <inheritdoc/>
    public async Task<bool> WriteAsync(byte[] data, bool withResponse)
    {
        var writer = new DataWriter();
        writer.WriteBytes(data);
        var status = await characteristic.WriteValueAsync(
            writer.DetachBuffer(), withResponse ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse);
        return status == GattCommunicationStatus.Success;
    }

    /// <inheritdoc/>
    public async Task<byte[]?> ReadAsync()
    {
        var result = await characteristic.ReadValueAsync();
        return result.Status == GattCommunicationStatus.Success ? ReadBuffer(result.Value) : null;
    }

    /// <inheritdoc/>
    public void Detach()
    {
        if (attached)
        {
            characteristic.ValueChanged -= valueHandler;
            attached = false;
        }

        ValueChanged = null;
    }

    /// <summary>Copies a WinRT buffer's bytes into an array.</summary>
    /// <param name="buffer">The characteristic value to copy.</param>
    /// <returns>A new array holding every unread byte of <paramref name="buffer"/>.</returns>
    private static byte[] ReadBuffer(IBuffer buffer)
    {
        var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);
        return bytes;
    }
}

/// <summary>Windows' BLE advertisement watcher, in active scanning mode.</summary>
[ExcludeFromCodeCoverage(Justification = "Thin wrapper over Windows' Bluetooth APIs; needs real hardware.")]
internal sealed class WinRtAdvertisementWatcher : IAdvertisementWatcher
{
    private readonly BluetoothLEAdvertisementWatcher watcher = new() { ScanningMode = BluetoothLEScanningMode.Active };

    /// <summary>Creates the watcher; it raises <see cref="Received"/> once started.</summary>
    public WinRtAdvertisementWatcher()
    {
        watcher.Received += (_, args) =>
            Received?.Invoke(this, new BleAdvertisement(args.BluetoothAddress, args.Advertisement.ServiceUuids.ToList()));
    }

    /// <inheritdoc/>
    public event EventHandler<BleAdvertisement>? Received;

    /// <inheritdoc/>
    public void Start() => watcher.Start();

    /// <inheritdoc/>
    public void Stop() => watcher.Stop();
}
