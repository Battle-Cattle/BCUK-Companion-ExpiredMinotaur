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
/// A connected FTMS treadmill: connects to it, subscribes to its data, status and Control Point
/// characteristics, and raises their values as events. Talks to the radio through
/// <see cref="IBleConnector"/> (Windows' BLE APIs by default).
/// </summary>
public class FtmsDevice : ITreadmillDevice
{
    // BLE indications should normally arrive within a few hundred ms; this
    // is generous headroom before we give up and report "no indication".
    private static readonly TimeSpan DefaultIndicationTimeout = TimeSpan.FromSeconds(2);

    private readonly IBleConnector connector;
    private readonly TimeSpan indicationTimeout;
    private IBleLink? link;

    // The characteristics subscribed to, so ReleaseCurrentDevice can detach them all.
    private readonly List<IGattCharacteristicPort> subscriptions = [];
    private bool disposed;

    /// <summary>Creates a disconnected device that connects over Windows' BLE APIs.</summary>
    public FtmsDevice()
        : this(new WinRtBleConnector(), DefaultIndicationTimeout)
    {
    }

    /// <summary>Creates a disconnected device over another connector, so tests can stand in for the radio.</summary>
    /// <param name="connector">Opens the BLE link on <see cref="ConnectAsync"/>.</param>
    /// <param name="indicationTimeout">How long a Control Point write waits for its indication.</param>
    internal FtmsDevice(IBleConnector connector, TimeSpan indicationTimeout)
    {
        this.connector = connector;
        this.indicationTimeout = indicationTimeout;
    }

    /// <summary>The Fitness Machine Control Point (0x2AD9), or null if not connected or not found.</summary>
    private IGattCharacteristicPort? ControlPoint { get; set; }

    /// <summary>The Supported Speed Range characteristic (0x2AD4), or null if not connected or not found.</summary>
    private IGattCharacteristicPort? SupportedSpeedRange { get; set; }

    /// <summary>
    /// Setup problems from the last <see cref="ConnectAsync"/> (missing characteristic, rejected CCCD
    /// write), so callers can surface what would otherwise fail silently as "nothing ever happens".
    /// </summary>
    public IReadOnlyList<string> LastConnectWarnings => warnings;

    private readonly List<string> warnings = [];

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

    /// <summary>Whether the treadmill reports itself connected right now.</summary>
    public bool IsConnected => link?.IsConnected == true;

    /// <summary>
    /// Connects to the treadmill at <paramref name="bluetoothAddress"/>, finds its FTMS characteristics
    /// and subscribes to them. Problems along the way are added to <see cref="LastConnectWarnings"/>.
    /// </summary>
    /// <param name="bluetoothAddress">The address from <see cref="FitnessMachineScanner.FindFitnessMachineAddressAsync"/>.</param>
    /// <returns>Whether the device and its Fitness Machine service were found.</returns>
    public async Task<bool> ConnectAsync(ulong bluetoothAddress)
    {
        warnings.Clear();

        // A prior failed attempt (scan/connect ok but service lookup failed, say) may have
        // left the link and subscriptions set — release them before reusing
        // this instance for a new attempt instead of leaking the old handles.
        ReleaseCurrentDevice();

        try
        {
            var (connected, failure) = await connector.ConnectAsync(bluetoothAddress);
            if (connected is null)
            {
                warnings.Add(failure ?? "Connect failed.");
                return false;
            }

            link = connected;
            link.Disconnected += OnLinkDisconnected;

            ControlPoint = await link.GetCharacteristicAsync(FtmsConstants.FitnessMachineControlPoint);
            var treadmillData = await link.GetCharacteristicAsync(FtmsConstants.TreadmillData);
            var status = await link.GetCharacteristicAsync(FtmsConstants.FitnessMachineStatus);
            SupportedSpeedRange = await link.GetCharacteristicAsync(FtmsConstants.SupportedSpeedRange);

            await SubscribeAsync(ControlPoint, new SubscriptionSpec(
                Indicate: true,
                bytes => ControlPointIndicationReceived?.Invoke(this, bytes),
                NotFoundWarning: "Control Point (0x2AD9) characteristic not found — speed/start/stop writes are impossible.",
                SubscribeFailWarningFormat: "Control Point indicate subscription failed: {0}. Control responses will never arrive."));

            await SubscribeAsync(treadmillData, new SubscriptionSpec(
                Indicate: false,
                bytes => TreadmillDataReceived?.Invoke(this, bytes),
                NotFoundWarning: "Treadmill Data (0x2ACD) characteristic not found — current speed readback disabled.",
                SubscribeFailWarningFormat: "Treadmill Data notify subscription failed: {0}. Current speed readback will never update."));

            await SubscribeAsync(status, new SubscriptionSpec(
                Indicate: false,
                bytes => FitnessMachineStatusReceived?.Invoke(this, bytes),
                NotFoundWarning: null,
                SubscribeFailWarningFormat: "Fitness Machine Status notify subscription failed: {0}."));

            return true;
        }
        catch (Exception ex)
        {
            // A failure after the link opened (a characteristic lookup or subscription) would
            // otherwise leave the device connected while reporting the attempt as failed.
            ReleaseCurrentDevice();
            warnings.Add($"Connect failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Raises <see cref="ConnectionLost"/> when the link reports the device disconnected.</summary>
    /// <param name="sender">The link that dropped.</param>
    /// <param name="e">Unused.</param>
    private void OnLinkDisconnected(object? sender, EventArgs e) => ConnectionLost?.Invoke(this, EventArgs.Empty);

    /// <summary>How <see cref="SubscribeAsync"/> subscribes to one characteristic and what it warns about.</summary>
    /// <param name="Indicate">True for indications, false for notifications.</param>
    /// <param name="RaiseEvent">Raises this class's event with the value's bytes.</param>
    /// <param name="NotFoundWarning">The warning to add when the characteristic is missing, or null for none.</param>
    /// <param name="SubscribeFailWarningFormat">The warning to add when the CCCD write fails; <c>{0}</c> is the status.</param>
    private sealed record SubscriptionSpec(
        bool Indicate,
        Action<byte[]> RaiseEvent,
        string? NotFoundWarning,
        string SubscribeFailWarningFormat);

    /// <summary>Attaches a value handler to <paramref name="characteristic"/> and enables its notifications or indications.</summary>
    /// <param name="characteristic">The characteristic to subscribe to, or null if it wasn't found (then only the not-found warning is added, if <paramref name="spec"/> has one).</param>
    /// <param name="spec">The CCCD mode, event to raise and warnings to use.</param>
    /// <returns>A task that completes once the CCCD write has finished. A non-success CCCD status is recorded in <see cref="LastConnectWarnings"/>; an exception from the write propagates.</returns>
    private async Task SubscribeAsync(IGattCharacteristicPort? characteristic, SubscriptionSpec spec)
    {
        if (characteristic == null)
        {
            if (spec.NotFoundWarning != null)
                warnings.Add(spec.NotFoundWarning);
            return;
        }

        characteristic.ValueChanged += (_, bytes) =>
        {
            if (disposed) return;
            spec.RaiseEvent(bytes);
        };
        subscriptions.Add(characteristic);

        var failedStatus = await characteristic.SubscribeAsync(spec.Indicate);
        if (failedStatus != null)
            warnings.Add(string.Format(spec.SubscribeFailWarningFormat, failedStatus));
    }

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
        var controlPoint = ControlPoint;
        if (controlPoint == null)
            return new ControlPointWriteResult(opcode, writeSucceeded: false, indicationReceived: false, resultCode: null);

        var tcs = new TaskCompletionSource<byte[]>();
        EventHandler<byte[]> handler = (s, resp) =>
        {
            if (IsResponseTo(resp, opcode))
                tcs.TrySetResult(resp);
        };
        ControlPointIndicationReceived += handler;
        try
        {
            if (!await controlPoint.WriteAsync(data, controlPoint.SupportsWriteWithResponse))
                return new ControlPointWriteResult(opcode, writeSucceeded: false, indicationReceived: false, resultCode: null);

            var winner = await Task.WhenAny(tcs.Task, Task.Delay(indicationTimeout));
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

    /// <summary>Whether a Control Point indication answers a write of <paramref name="opcode"/>.</summary>
    /// <param name="indication">The indication's bytes: response code, echoed opcode, result code.</param>
    /// <param name="opcode">The opcode that was written.</param>
    /// <returns>True if it's a response (0x80) to that opcode with a result code.</returns>
    internal static bool IsResponseTo(byte[] indication, byte opcode) =>
        indication.Length >= 3 && indication[0] == FtmsConstants.ResponseCode && indication[1] == opcode;

    /// <summary>
    /// Reads Supported Speed Range (0x2AD4): Minimum Speed, Maximum Speed,
    /// Minimum Increment, each a uint16 in units of 0.01 km/h, per FTMS
    /// spec section 4.4. Returns null if the characteristic is absent or
    /// unreadable — callers should fall back to a hardcoded default range.
    /// </summary>
    /// <returns>A task completing with the minimum, maximum and increment in km/h, or null if the range couldn't be read.</returns>
    public async Task<(double MinKmh, double MaxKmh, double IncrementKmh)?> ReadSupportedSpeedRangeAsync()
    {
        var characteristic = SupportedSpeedRange;
        if (characteristic == null) return null;

        try
        {
            var bytes = await characteristic.ReadAsync();
            return bytes is null ? null : ParseSupportedSpeedRange(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Decodes a Supported Speed Range value: three little-endian uint16s in units of 0.01 km/h.</summary>
    /// <param name="bytes">The characteristic's value.</param>
    /// <returns>The minimum, maximum and increment in km/h, or null if the value is shorter than 6 bytes.</returns>
    internal static (double MinKmh, double MaxKmh, double IncrementKmh)? ParseSupportedSpeedRange(byte[] bytes)
    {
        if (bytes.Length < 6) return null;

        ushort min = (ushort)(bytes[0] | (bytes[1] << 8));
        ushort max = (ushort)(bytes[2] | (bytes[3] << 8));
        ushort increment = (ushort)(bytes[4] | (bytes[5] << 8));
        return (min / 100.0, max / 100.0, increment / 100.0);
    }

    /// <summary>Detaches every handler and disposes the current link, so the instance can connect again.</summary>
    private void ReleaseCurrentDevice()
    {
        foreach (var characteristic in subscriptions)
            characteristic.Detach();
        subscriptions.Clear();

        if (link != null)
        {
            link.Disconnected -= OnLinkDisconnected;
            link.Dispose();
            link = null;
        }

        ControlPoint = SupportedSpeedRange = null;
    }

    /// <summary>Disconnects and drops every subscriber, so a late BLE callback can't reach a torn-down caller.</summary>
    public void Dispose()
    {
        disposed = true;
        ReleaseCurrentDevice();

        // Drop external subscribers too, so a stray in-flight callback
        // that slips through can't reach a disposed ViewModel.
        TreadmillDataReceived = null;
        ControlPointIndicationReceived = null;
        FitnessMachineStatusReceived = null;
        ConnectionLost = null;
    }
}
