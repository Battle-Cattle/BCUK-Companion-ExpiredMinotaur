using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

public readonly struct ControlPointWriteResult
{
    public byte OpCode { get; }
    public bool WriteSucceeded { get; }
    public bool IndicationReceived { get; }
    public byte? ResultCode { get; }

    public ControlPointWriteResult(byte opCode, bool writeSucceeded, bool indicationReceived, byte? resultCode)
    {
        OpCode = opCode;
        WriteSucceeded = writeSucceeded;
        IndicationReceived = indicationReceived;
        ResultCode = resultCode;
    }

    public bool Accepted => IndicationReceived && ResultCode == FtmsConstants.ResultSuccess;

    public override string ToString()
    {
        if (!WriteSucceeded) return $"0x{OpCode:X2} -> GATT write failed";
        if (!IndicationReceived) return $"0x{OpCode:X2} -> sent, no indication (timeout)";
        return ResultCode == FtmsConstants.ResultSuccess
            ? $"0x{OpCode:X2} -> OK"
            : $"0x{OpCode:X2} -> FAILED (0x{ResultCode:X2})";
    }
}

public class FtmsDevice : IDisposable
{
    private BluetoothLEDevice? _device;
    private GattDeviceService? _fitnessService;

    public GattCharacteristic? ControlPoint { get; private set; }
    public GattCharacteristic? TreadmillData { get; private set; }
    public GattCharacteristic? FitnessMachineStatus { get; private set; }
    public GattCharacteristic? SupportedSpeedRange { get; private set; }

    // Populated during ConnectAsync so callers can surface setup problems
    // (missing characteristic, rejected CCCD write) that would otherwise
    // fail silently as "nothing ever happens" with no diagnostic.
    public List<string> LastConnectWarnings { get; } = new();

    public event EventHandler<byte[]>? TreadmillDataReceived;
    public event EventHandler<byte[]>? ControlPointIndicationReceived;
    public event EventHandler<byte[]>? FitnessMachineStatusReceived;

    // Named so Dispose can detach them — anonymous lambdas passed directly
    // to += can't be removed with -=, which left GATT callbacks firing
    // into a torn-down ViewModel on window close.
    private TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? _controlPointHandler;
    private TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? _treadmillDataHandler;
    private TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? _statusHandler;
    private bool _disposed;

    public bool IsConnected => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;

    /// <summary>
    /// Scans for BLE advertisements and returns the address of the first
    /// device advertising the Fitness Machine service (0x1826).
    /// </summary>
    public static Task<ulong?> FindFitnessMachineAddressAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<ulong?>();
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        watcher.Received += (sender, args) =>
        {
            if (args.Advertisement.ServiceUuids.Contains(FtmsConstants.FitnessMachineService))
            {
                watcher.Stop();
                tcs.TrySetResult(args.BluetoothAddress);
            }
        };

        watcher.Start();

        _ = Task.Delay(timeout).ContinueWith(_ =>
        {
            if (!tcs.Task.IsCompleted)
            {
                watcher.Stop();
                tcs.TrySetResult(null);
            }
        });

        return tcs.Task;
    }

    public async Task<bool> ConnectAsync(ulong bluetoothAddress)
    {
        LastConnectWarnings.Clear();

        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
        if (_device == null)
        {
            LastConnectWarnings.Add("FromBluetoothAddressAsync returned null.");
            return false;
        }

        var servicesResult = await _device.GetGattServicesForUuidAsync(FtmsConstants.FitnessMachineService);
        if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
        {
            LastConnectWarnings.Add($"Fitness Machine service (0x1826) not found (status: {servicesResult.Status}).");
            return false;
        }

        _fitnessService = servicesResult.Services[0];

        ControlPoint = await GetCharacteristicAsync(FtmsConstants.FitnessMachineControlPoint);
        TreadmillData = await GetCharacteristicAsync(FtmsConstants.TreadmillData);
        FitnessMachineStatus = await GetCharacteristicAsync(FtmsConstants.FitnessMachineStatus);
        SupportedSpeedRange = await GetCharacteristicAsync(FtmsConstants.SupportedSpeedRange);

        if (ControlPoint != null)
        {
            _controlPointHandler = (s, args) =>
            {
                if (_disposed) return;
                ControlPointIndicationReceived?.Invoke(this, ReadBuffer(args.CharacteristicValue));
            };
            ControlPoint.ValueChanged += _controlPointHandler;
            var cccdStatus = await ControlPoint.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Indicate);
            if (cccdStatus != GattCommunicationStatus.Success)
                LastConnectWarnings.Add($"Control Point indicate subscription failed: {cccdStatus}. Control responses will never arrive.");
        }
        else
        {
            LastConnectWarnings.Add("Control Point (0x2AD9) characteristic not found — speed/start/stop writes are impossible.");
        }

        if (TreadmillData != null)
        {
            _treadmillDataHandler = (s, args) =>
            {
                if (_disposed) return;
                TreadmillDataReceived?.Invoke(this, ReadBuffer(args.CharacteristicValue));
            };
            TreadmillData.ValueChanged += _treadmillDataHandler;
            var cccdStatus = await TreadmillData.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (cccdStatus != GattCommunicationStatus.Success)
                LastConnectWarnings.Add($"Treadmill Data notify subscription failed: {cccdStatus}. Current speed readback will never update.");
        }
        else
        {
            LastConnectWarnings.Add("Treadmill Data (0x2ACD) characteristic not found — current speed readback disabled.");
        }

        if (FitnessMachineStatus != null)
        {
            _statusHandler = (s, args) =>
            {
                if (_disposed) return;
                FitnessMachineStatusReceived?.Invoke(this, ReadBuffer(args.CharacteristicValue));
            };
            FitnessMachineStatus.ValueChanged += _statusHandler;
            var cccdStatus = await FitnessMachineStatus.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (cccdStatus != GattCommunicationStatus.Success)
                LastConnectWarnings.Add($"Fitness Machine Status notify subscription failed: {cccdStatus}.");
        }

        return true;
    }

    private async Task<GattCharacteristic?> GetCharacteristicAsync(Guid characteristicUuid)
    {
        if (_fitnessService == null) return null;
        var result = await _fitnessService.GetCharacteristicsForUuidAsync(characteristicUuid);
        return result.Status == GattCommunicationStatus.Success && result.Characteristics.Count > 0
            ? result.Characteristics[0]
            : null;
    }

    // BLE indications should normally arrive within a few hundred ms; this
    // is generous headroom before we give up and report "no indication".
    private static readonly TimeSpan IndicationTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Writes raw bytes to the Control Point using WriteWithoutResponse,
    /// matching the properties confirmed on T-TREADMILLY04
    /// (INDICATE, WRITE NO RESPONSE), then waits for the matching
    /// indication (Response Code 0x80, echoed opcode) rather than a
    /// fixed delay, so callers get the actual accept/reject result.
    /// </summary>
    public async Task<ControlPointWriteResult> WriteControlPointAndAwaitAsync(byte[] data)
    {
        byte opcode = data[0];
        if (ControlPoint == null)
            return new ControlPointWriteResult(opcode, writeSucceeded: false, indicationReceived: false, resultCode: null);

        var tcs = new TaskCompletionSource<byte[]>();
        EventHandler<byte[]> handler = (s, resp) =>
        {
            if (resp.Length >= 3 && resp[0] == FtmsConstants.ResponseCode && resp[1] == opcode)
                tcs.TrySetResult(resp);
        };
        ControlPointIndicationReceived += handler;
        try
        {
            var writer = new DataWriter();
            writer.WriteBytes(data);
            var status = await ControlPoint.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            if (status != GattCommunicationStatus.Success)
                return new ControlPointWriteResult(opcode, writeSucceeded: false, indicationReceived: false, resultCode: null);

            var winner = await Task.WhenAny(tcs.Task, Task.Delay(IndicationTimeout));
            if (winner == tcs.Task)
            {
                byte[] resp = tcs.Task.Result;
                return new ControlPointWriteResult(opcode, writeSucceeded: true, indicationReceived: true, resultCode: resp[2]);
            }
            return new ControlPointWriteResult(opcode, writeSucceeded: true, indicationReceived: false, resultCode: null);
        }
        finally
        {
            ControlPointIndicationReceived -= handler;
        }
    }

    /// <summary>
    /// Reads Supported Speed Range (0x2AD4): Minimum Speed, Maximum Speed,
    /// Minimum Increment, each a uint16 in units of 0.01 km/h, per FTMS
    /// spec section 4.4. Returns null if the characteristic is absent or
    /// unreadable — callers should fall back to a hardcoded default range.
    /// </summary>
    public async Task<(double MinKmh, double MaxKmh, double IncrementKmh)?> ReadSupportedSpeedRangeAsync()
    {
        if (SupportedSpeedRange == null) return null;

        var result = await SupportedSpeedRange.ReadValueAsync();
        if (result.Status != GattCommunicationStatus.Success) return null;

        var bytes = ReadBuffer(result.Value);
        if (bytes.Length < 6) return null;

        ushort min = (ushort)(bytes[0] | (bytes[1] << 8));
        ushort max = (ushort)(bytes[2] | (bytes[3] << 8));
        ushort increment = (ushort)(bytes[4] | (bytes[5] << 8));
        return (min / 100.0, max / 100.0, increment / 100.0);
    }

    private static byte[] ReadBuffer(IBuffer buffer)
    {
        var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);
        return bytes;
    }

    public void Dispose()
    {
        _disposed = true;

        if (ControlPoint != null && _controlPointHandler != null)
            ControlPoint.ValueChanged -= _controlPointHandler;
        if (TreadmillData != null && _treadmillDataHandler != null)
            TreadmillData.ValueChanged -= _treadmillDataHandler;
        if (FitnessMachineStatus != null && _statusHandler != null)
            FitnessMachineStatus.ValueChanged -= _statusHandler;

        // Drop external subscribers too, so a stray in-flight callback
        // that slips through can't reach a disposed ViewModel.
        TreadmillDataReceived = null;
        ControlPointIndicationReceived = null;
        FitnessMachineStatusReceived = null;

        _device?.Dispose();
    }
}
