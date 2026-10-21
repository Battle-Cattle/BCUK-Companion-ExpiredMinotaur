using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

/// <summary>The outcome of one Control Point write: whether it went out, and what the treadmill answered.</summary>
public readonly struct ControlPointWriteResult
{
    /// <summary>The opcode that was written.</summary>
    public byte OpCode { get; }

    /// <summary>Whether the GATT write itself succeeded.</summary>
    public bool WriteSucceeded { get; }

    /// <summary>Whether the treadmill sent the matching indication before the timeout.</summary>
    public bool IndicationReceived { get; }

    /// <summary>The indication's result code (e.g. <see cref="FtmsConstants.ResultSuccess"/>), or null if none arrived.</summary>
    public byte? ResultCode { get; }

    /// <summary>Creates a result.</summary>
    /// <param name="opCode">The opcode that was written.</param>
    /// <param name="writeSucceeded">Whether the GATT write succeeded.</param>
    /// <param name="indicationReceived">Whether the matching indication arrived.</param>
    /// <param name="resultCode">The indication's result code, or null if none arrived.</param>
    public ControlPointWriteResult(byte opCode, bool writeSucceeded, bool indicationReceived, byte? resultCode)
    {
        OpCode = opCode;
        WriteSucceeded = writeSucceeded;
        IndicationReceived = indicationReceived;
        ResultCode = resultCode;
    }

    /// <summary>Whether the treadmill confirmed the command with <see cref="FtmsConstants.ResultSuccess"/>.</summary>
    public bool Accepted => IndicationReceived && ResultCode == FtmsConstants.ResultSuccess;

    /// <summary>A one-line summary for the log, e.g. <c>0x02 -> OK</c>.</summary>
    /// <returns>The opcode and whether the write failed, timed out, succeeded or was rejected with a result code.</returns>
    public override string ToString()
    {
        if (!WriteSucceeded) return $"0x{OpCode:X2} -> GATT write failed";
        if (!IndicationReceived) return $"0x{OpCode:X2} -> sent, no indication (timeout)";
        return ResultCode == FtmsConstants.ResultSuccess
            ? $"0x{OpCode:X2} -> OK"
            : $"0x{OpCode:X2} -> FAILED (0x{ResultCode:X2})";
    }
}

/// <summary>
/// A connected FTMS treadmill over Windows' BLE APIs: finds and connects to it, subscribes to its
/// data, status and Control Point characteristics, and raises their values as events.
/// </summary>
public class FtmsDevice : IControlPointWriter, IDisposable
{
    private BluetoothLEDevice? _device;
    private GattDeviceService? _fitnessService;

    /// <summary>The Fitness Machine Control Point (0x2AD9), or null if not connected or not found.</summary>
    public GattCharacteristic? ControlPoint { get; private set; }

    /// <summary>The Treadmill Data characteristic (0x2ACD), or null if not connected or not found.</summary>
    public GattCharacteristic? TreadmillData { get; private set; }

    /// <summary>The Fitness Machine Status characteristic (0x2ADA), or null if not connected or not found.</summary>
    public GattCharacteristic? FitnessMachineStatus { get; private set; }

    /// <summary>The Supported Speed Range characteristic (0x2AD4), or null if not connected or not found.</summary>
    public GattCharacteristic? SupportedSpeedRange { get; private set; }

    /// <summary>
    /// Setup problems from the last <see cref="ConnectAsync"/> (missing characteristic, rejected CCCD
    /// write), so callers can surface what would otherwise fail silently as "nothing ever happens".
    /// </summary>
    public List<string> LastConnectWarnings { get; } = new();

    /// <summary>Raised (on a BLE callback thread) with each Treadmill Data notification's raw bytes.</summary>
    public event EventHandler<byte[]>? TreadmillDataReceived;

    /// <summary>Raised (on a BLE callback thread) with each Control Point indication's raw bytes.</summary>
    public event EventHandler<byte[]>? ControlPointIndicationReceived;

    /// <summary>Raised (on a BLE callback thread) with each Fitness Machine Status notification's raw bytes.</summary>
    public event EventHandler<byte[]>? FitnessMachineStatusReceived;

    /// <summary>
    /// Raised (on a BLE callback thread) when the connected treadmill drops the connection: it was
    /// powered off, went out of range, or Windows lost the link.
    /// </summary>
    public event EventHandler? ConnectionLost;

    // Named so Dispose can detach them — anonymous lambdas passed directly
    // to += can't be removed with -=, which left GATT callbacks firing
    // into a torn-down ViewModel on window close.
    private TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? _controlPointHandler;
    private TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? _treadmillDataHandler;
    private TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? _statusHandler;
    private TypedEventHandler<BluetoothLEDevice, object>? _connectionStatusHandler;
    private bool _disposed;

    /// <summary>Whether Windows reports the treadmill as connected right now.</summary>
    public bool IsConnected => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;

    /// <summary>
    /// Scans for BLE advertisements and returns the address of the first
    /// device advertising the Fitness Machine service (0x1826), or null if none
    /// is found before <paramref name="timeout"/> or <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    /// <param name="timeout">How long to scan before giving up.</param>
    /// <param name="cancellationToken">Stops the scan early, completing with null.</param>
    /// <returns>A task completing with the treadmill's 48-bit Bluetooth address, or null if none was found.</returns>
    public static Task<ulong?> FindFitnessMachineAddressAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<ulong?>();
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        // The Received callback and the timeout continuation can both observe an
        // incomplete tcs at the same time (the callback runs on a background
        // advertisement-watcher thread, the timeout on the thread pool), so a plain
        // `if (!tcs.Task.IsCompleted)` guard on either side is a race: a device found
        // right at the timeout boundary could have its address discarded in favor of a
        // `null` result. This gate makes "who gets to stop the watcher, detach the
        // handler, and complete tcs" atomic — only the first caller to flip it wins.
        var completionGate = 0;

        TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs>? handler = null;
        handler = (sender, args) =>
        {
            if (args.Advertisement.ServiceUuids.Contains(FtmsConstants.FitnessMachineService)
                && Interlocked.CompareExchange(ref completionGate, 1, 0) == 0)
            {
                watcher.Stop();
                watcher.Received -= handler;
                tcs.TrySetResult(args.BluetoothAddress);
            }
        };
        watcher.Received += handler;

        watcher.Start();

        // Timeout and cancellation both end the scan with no result, through the same gate.
        void StopWithoutResult()
        {
            if (Interlocked.CompareExchange(ref completionGate, 1, 0) == 0)
            {
                watcher.Stop();
                watcher.Received -= handler;
                tcs.TrySetResult(null);
            }
        }

        var cancellationRegistration = cancellationToken.Register(StopWithoutResult);
        _ = Task.Delay(timeout).ContinueWith(_ => StopWithoutResult());
        _ = tcs.Task.ContinueWith(_ => cancellationRegistration.Dispose(), TaskScheduler.Default);

        return tcs.Task;
    }

    /// <summary>
    /// Connects to the treadmill at <paramref name="bluetoothAddress"/>, finds its FTMS characteristics
    /// and subscribes to them. Problems along the way are added to <see cref="LastConnectWarnings"/>.
    /// </summary>
    /// <param name="bluetoothAddress">The address from <see cref="FindFitnessMachineAddressAsync"/>.</param>
    /// <returns>Whether the device and its Fitness Machine service were found.</returns>
    public async Task<bool> ConnectAsync(ulong bluetoothAddress)
    {
        LastConnectWarnings.Clear();

        // A prior failed attempt (scan/connect ok but service lookup failed, say) may have
        // left _device/_fitnessService and subscriptions set — release them before reusing
        // this instance for a new attempt instead of leaking the old handles.
        ReleaseCurrentDevice();

        try
        {
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

            _connectionStatusHandler = (sender, _) =>
            {
                if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                {
                    ConnectionLost?.Invoke(this, EventArgs.Empty);
                }
            };
            _device.ConnectionStatusChanged += _connectionStatusHandler;

            ControlPoint = await GetCharacteristicAsync(FtmsConstants.FitnessMachineControlPoint);
            TreadmillData = await GetCharacteristicAsync(FtmsConstants.TreadmillData);
            FitnessMachineStatus = await GetCharacteristicAsync(FtmsConstants.FitnessMachineStatus);
            SupportedSpeedRange = await GetCharacteristicAsync(FtmsConstants.SupportedSpeedRange);

            await SubscribeAsync(ControlPoint, new SubscriptionSpec(
                GattClientCharacteristicConfigurationDescriptorValue.Indicate,
                h => _controlPointHandler = h,
                bytes => ControlPointIndicationReceived?.Invoke(this, bytes),
                NotFoundWarning: "Control Point (0x2AD9) characteristic not found — speed/start/stop writes are impossible.",
                SubscribeFailWarningFormat: "Control Point indicate subscription failed: {0}. Control responses will never arrive."));

            await SubscribeAsync(TreadmillData, new SubscriptionSpec(
                GattClientCharacteristicConfigurationDescriptorValue.Notify,
                h => _treadmillDataHandler = h,
                bytes => TreadmillDataReceived?.Invoke(this, bytes),
                NotFoundWarning: "Treadmill Data (0x2ACD) characteristic not found — current speed readback disabled.",
                SubscribeFailWarningFormat: "Treadmill Data notify subscription failed: {0}. Current speed readback will never update."));

            await SubscribeAsync(FitnessMachineStatus, new SubscriptionSpec(
                GattClientCharacteristicConfigurationDescriptorValue.Notify,
                h => _statusHandler = h,
                bytes => FitnessMachineStatusReceived?.Invoke(this, bytes),
                NotFoundWarning: null,
                SubscribeFailWarningFormat: "Fitness Machine Status notify subscription failed: {0}."));

            return true;
        }
        catch (Exception ex)
        {
            LastConnectWarnings.Add($"Connect failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Looks up one characteristic of the Fitness Machine service, or null if it's missing or the lookup fails.</summary>
    /// <param name="characteristicUuid">The characteristic's full 128-bit UUID (see <see cref="FtmsConstants"/>).</param>
    /// <returns>A task completing with the first matching characteristic, or null if not connected, not found or the lookup failed.</returns>
    private async Task<GattCharacteristic?> GetCharacteristicAsync(Guid characteristicUuid)
    {
        if (_fitnessService == null) return null;
        var result = await _fitnessService.GetCharacteristicsForUuidAsync(characteristicUuid);
        return result.Status == GattCommunicationStatus.Success && result.Characteristics.Count > 0
            ? result.Characteristics[0]
            : null;
    }

    /// <summary>How <see cref="SubscribeAsync"/> subscribes to one characteristic and what it warns about.</summary>
    /// <param name="CccdMode">Notify or Indicate.</param>
    /// <param name="StoreHandler">Keeps the handler so <see cref="ReleaseCurrentDevice"/> can detach it.</param>
    /// <param name="RaiseEvent">Raises this class's event with the value's bytes.</param>
    /// <param name="NotFoundWarning">The warning to add when the characteristic is missing, or null for none.</param>
    /// <param name="SubscribeFailWarningFormat">The warning to add when the CCCD write fails; <c>{0}</c> is the status.</param>
    private sealed record SubscriptionSpec(
        GattClientCharacteristicConfigurationDescriptorValue CccdMode,
        Action<TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>> StoreHandler,
        Action<byte[]> RaiseEvent,
        string? NotFoundWarning,
        string SubscribeFailWarningFormat);

    /// <summary>Attaches a value handler to <paramref name="characteristic"/> and enables its notifications or indications.</summary>
    /// <param name="characteristic">The characteristic to subscribe to, or null if it wasn't found (only adds the not-found warning).</param>
    /// <param name="spec">The CCCD mode, handler storage, event to raise and warnings to use.</param>
    /// <returns>A task that completes once the CCCD write has finished; a missing characteristic or a failed CCCD write is recorded in <see cref="LastConnectWarnings"/> rather than thrown.</returns>
    private async Task SubscribeAsync(GattCharacteristic? characteristic, SubscriptionSpec spec)
    {
        if (characteristic == null)
        {
            if (spec.NotFoundWarning != null)
                LastConnectWarnings.Add(spec.NotFoundWarning);
            return;
        }

        TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler = (_, args) =>
        {
            if (_disposed) return;
            spec.RaiseEvent(ReadBuffer(args.CharacteristicValue));
        };
        spec.StoreHandler(handler);
        characteristic.ValueChanged += handler;

        var cccdStatus = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(spec.CccdMode);
        if (cccdStatus != GattCommunicationStatus.Success)
            LastConnectWarnings.Add(string.Format(spec.SubscribeFailWarningFormat, cccdStatus));
    }

    // BLE indications should normally arrive within a few hundred ms; this
    // is generous headroom before we give up and report "no indication".
    private static readonly TimeSpan IndicationTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Writes raw bytes to the Control Point, then waits for the matching
    /// indication (Response Code 0x80, echoed opcode) rather than a
    /// fixed delay, so callers get the actual accept/reject result.
    /// FTMS specifies a Write (with response), but T-TREADMILLY04 only
    /// exposes INDICATE, WRITE NO RESPONSE — so use a write with response
    /// when the characteristic supports it, and fall back otherwise.
    /// </summary>
    /// <param name="data">The opcode followed by its parameters.</param>
    /// <returns>Whether the write went through and, if an indication arrived, its result code.</returns>
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
            var writeOption = ControlPoint.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
                ? GattWriteOption.WriteWithResponse
                : GattWriteOption.WriteWithoutResponse;
            var status = await ControlPoint.WriteValueAsync(writer.DetachBuffer(), writeOption);
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
        catch (Exception)
        {
            return new ControlPointWriteResult(opcode, writeSucceeded: false, indicationReceived: false, resultCode: null);
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
    /// <returns>A task completing with the minimum, maximum and increment in km/h, or null if the range couldn't be read.</returns>
    public async Task<(double MinKmh, double MaxKmh, double IncrementKmh)?> ReadSupportedSpeedRangeAsync()
    {
        if (SupportedSpeedRange == null) return null;

        try
        {
            var result = await SupportedSpeedRange.ReadValueAsync();
            if (result.Status != GattCommunicationStatus.Success) return null;

            var bytes = ReadBuffer(result.Value);
            if (bytes.Length < 6) return null;

            ushort min = (ushort)(bytes[0] | (bytes[1] << 8));
            ushort max = (ushort)(bytes[2] | (bytes[3] << 8));
            ushort increment = (ushort)(bytes[4] | (bytes[5] << 8));
            return (min / 100.0, max / 100.0, increment / 100.0);
        }
        catch (Exception)
        {
            return null;
        }
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

    /// <summary>Detaches every handler and disposes the current device and service, so the instance can connect again.</summary>
    private void ReleaseCurrentDevice()
    {
        if (ControlPoint != null && _controlPointHandler != null)
            ControlPoint.ValueChanged -= _controlPointHandler;
        if (TreadmillData != null && _treadmillDataHandler != null)
            TreadmillData.ValueChanged -= _treadmillDataHandler;
        if (FitnessMachineStatus != null && _statusHandler != null)
            FitnessMachineStatus.ValueChanged -= _statusHandler;
        if (_device != null && _connectionStatusHandler != null)
            _device.ConnectionStatusChanged -= _connectionStatusHandler;
        _connectionStatusHandler = null;

        _fitnessService?.Dispose();
        _fitnessService = null;
        _device?.Dispose();
        _device = null;
        ControlPoint = TreadmillData = FitnessMachineStatus = SupportedSpeedRange = null;
    }

    /// <summary>Disconnects and drops every subscriber, so a late BLE callback can't reach a torn-down caller.</summary>
    public void Dispose()
    {
        _disposed = true;
        ReleaseCurrentDevice();

        // Drop external subscribers too, so a stray in-flight callback
        // that slips through can't reach a disposed ViewModel.
        TreadmillDataReceived = null;
        ControlPointIndicationReceived = null;
        FitnessMachineStatusReceived = null;
        ConnectionLost = null;
    }
}
