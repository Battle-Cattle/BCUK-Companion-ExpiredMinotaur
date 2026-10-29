using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class FtmsDeviceTests
{
    private sealed class FakeCharacteristic : IGattCharacteristicPort
    {
        public bool SupportsWriteWithResponse { get; set; } = true;

        public string? SubscribeFailure { get; set; }

        public bool? SubscribedForIndications { get; private set; }

        public bool Detached { get; private set; }

        public List<(byte[] Data, bool WithResponse)> Writes { get; } = [];

        public Func<byte[], bool> OnWrite { get; set; } = _ => true;

        public Func<byte[]?> OnRead { get; set; } = () => null;

        public event EventHandler<byte[]>? ValueChanged;

        public void Raise(params byte[] value) => ValueChanged?.Invoke(this, value);

        public Task<string?> SubscribeAsync(bool indicate)
        {
            SubscribedForIndications = indicate;
            return Task.FromResult(SubscribeFailure);
        }

        public Task<bool> WriteAsync(byte[] data, bool withResponse)
        {
            Writes.Add((data, withResponse));
            return Task.FromResult(OnWrite(data));
        }

        public Task<byte[]?> ReadAsync() => Task.FromResult(OnRead());

        public void Detach()
        {
            Detached = true;
            ValueChanged = null;
        }
    }

    private sealed class FakeLink : IBleLink
    {
        public Dictionary<Guid, FakeCharacteristic> Characteristics { get; } = new()
        {
            [FtmsConstants.FitnessMachineControlPoint] = new(),
            [FtmsConstants.TreadmillData] = new(),
            [FtmsConstants.FitnessMachineStatus] = new(),
            [FtmsConstants.SupportedSpeedRange] = new(),
        };

        public bool IsConnected { get; set; } = true;

        public bool Disposed { get; private set; }

        public event EventHandler? Disconnected;

        public FakeCharacteristic this[Guid uuid] => Characteristics[uuid];

        public void Drop() => Disconnected?.Invoke(this, EventArgs.Empty);

        public Task<IGattCharacteristicPort?> GetCharacteristicAsync(Guid characteristicUuid) =>
            Task.FromResult<IGattCharacteristicPort?>(Characteristics.GetValueOrDefault(characteristicUuid));

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeConnector : IBleConnector
    {
        public FakeLink Link { get; set; } = new();

        public string? Failure { get; set; }

        public Exception? Throws { get; set; }

        public List<ulong> Addresses { get; } = [];

        public Task<(IBleLink? Link, string? Failure)> ConnectAsync(ulong bluetoothAddress)
        {
            Addresses.Add(bluetoothAddress);
            if (Throws is not null)
            {
                throw Throws;
            }

            return Task.FromResult<(IBleLink?, string?)>(Failure is null ? (Link, null) : (null, Failure));
        }
    }

    private readonly FakeConnector connector = new();
    private readonly FtmsDevice device;

    public FtmsDeviceTests()
    {
        device = new FtmsDevice(connector, TimeSpan.FromMilliseconds(200));
    }

    private FakeCharacteristic ControlPoint => connector.Link[FtmsConstants.FitnessMachineControlPoint];

    [Fact]
    public async Task ConnectAsync_SubscribesToTheControlPointDataAndStatus()
    {
        Assert.True(await device.ConnectAsync(42));

        Assert.Equal([42UL], connector.Addresses);
        Assert.True(device.IsConnected);
        Assert.Empty(device.LastConnectWarnings);
        Assert.True(ControlPoint.SubscribedForIndications);
        Assert.False(connector.Link[FtmsConstants.TreadmillData].SubscribedForIndications);
        Assert.False(connector.Link[FtmsConstants.FitnessMachineStatus].SubscribedForIndications);
    }

    [Fact]
    public async Task ConnectAsync_ReportsWhyTheConnectorFailed()
    {
        connector.Failure = "no service";

        Assert.False(await device.ConnectAsync(42));

        Assert.Equal(["no service"], device.LastConnectWarnings);
        Assert.False(device.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_ReportsAnExceptionAsAWarning()
    {
        connector.Throws = new InvalidOperationException("radio off");

        Assert.False(await device.ConnectAsync(42));

        Assert.Equal(["Connect failed: radio off"], device.LastConnectWarnings);
    }

    [Fact]
    public async Task ConnectAsync_WarnsAboutMissingCharacteristics()
    {
        connector.Link.Characteristics.Remove(FtmsConstants.FitnessMachineControlPoint);
        connector.Link.Characteristics.Remove(FtmsConstants.TreadmillData);
        connector.Link.Characteristics.Remove(FtmsConstants.FitnessMachineStatus);

        Assert.True(await device.ConnectAsync(42));

        Assert.Collection(
            device.LastConnectWarnings,
            w => Assert.StartsWith("Control Point (0x2AD9) characteristic not found", w),
            w => Assert.StartsWith("Treadmill Data (0x2ACD) characteristic not found", w));
    }

    [Fact]
    public async Task ConnectAsync_WarnsAboutRejectedSubscriptions()
    {
        foreach (var characteristic in connector.Link.Characteristics.Values)
        {
            characteristic.SubscribeFailure = "Unreachable";
        }

        await device.ConnectAsync(42);

        Assert.Equal(
            [
                "Control Point indicate subscription failed: Unreachable. Control responses will never arrive.",
                "Treadmill Data notify subscription failed: Unreachable. Current speed readback will never update.",
                "Fitness Machine Status notify subscription failed: Unreachable.",
            ],
            device.LastConnectWarnings);
    }

    [Fact]
    public async Task Notifications_AreRaisedAsEvents()
    {
        await device.ConnectAsync(42);
        byte[]? data = null, status = null, indication = null;
        device.TreadmillDataReceived += (_, bytes) => data = bytes;
        device.FitnessMachineStatusReceived += (_, bytes) => status = bytes;
        device.ControlPointIndicationReceived += (_, bytes) => indication = bytes;

        connector.Link[FtmsConstants.TreadmillData].Raise(1, 2);
        connector.Link[FtmsConstants.FitnessMachineStatus].Raise(3);
        ControlPoint.Raise(4);

        Assert.Equal(new byte[] { 1, 2 }, data);
        Assert.Equal(new byte[] { 3 }, status);
        Assert.Equal(new byte[] { 4 }, indication);
    }

    [Fact]
    public async Task ConnectionLost_IsRaisedWhenTheLinkDrops()
    {
        await device.ConnectAsync(42);
        var lost = 0;
        device.ConnectionLost += (_, _) => lost++;

        connector.Link.Drop();

        Assert.Equal(1, lost);
    }

    [Fact]
    public async Task ConnectAsync_Again_ReleasesThePreviousLink()
    {
        await device.ConnectAsync(42);
        var first = connector.Link;
        connector.Link = new FakeLink();

        await device.ConnectAsync(43);

        Assert.True(first.Disposed);
        Assert.All(first.Characteristics.Values.Take(3), c => Assert.True(c.Detached));
        var lost = 0;
        device.ConnectionLost += (_, _) => lost++;
        first.Drop();
        Assert.Equal(0, lost);
    }

    [Fact]
    public async Task Dispose_ReleasesTheLink_AndSilencesLateCallbacks()
    {
        await device.ConnectAsync(42);
        var data = connector.Link[FtmsConstants.TreadmillData];
        var raised = 0;
        device.TreadmillDataReceived += (_, _) => raised++;

        device.Dispose();
        data.Raise(1);

        Assert.True(connector.Link.Disposed);
        Assert.False(device.IsConnected);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task WriteControlPoint_ReturnsTheMatchingIndicationsResult()
    {
        await device.ConnectAsync(42);
        ControlPoint.OnWrite = data =>
        {
            // Another opcode's response first, then this one's.
            Task.Run(() =>
            {
                ControlPoint.Raise(FtmsConstants.ResponseCode, FtmsConstants.Opcode.RequestControl, FtmsConstants.ResultSuccess);
                ControlPoint.Raise(FtmsConstants.ResponseCode, data[0], FtmsConstants.ResultInvalidParameter);
            });
            return true;
        };

        var result = await device.WriteControlPointAndAwaitAsync([FtmsConstants.Opcode.SetTargetSpeed, 0x90, 0x01]);

        Assert.Equal(new ControlPointWriteResult(FtmsConstants.Opcode.SetTargetSpeed, true, true, FtmsConstants.ResultInvalidParameter), result);
        Assert.True(Assert.Single(ControlPoint.Writes).WithResponse);
    }

    [Fact]
    public async Task WriteControlPoint_WritesWithoutResponseWhenThatsAllItSupports()
    {
        await device.ConnectAsync(42);
        ControlPoint.SupportsWriteWithResponse = false;

        var result = await device.WriteControlPointAndAwaitAsync([FtmsConstants.Opcode.RequestControl]);

        Assert.False(Assert.Single(ControlPoint.Writes).WithResponse);
        Assert.Equal(new ControlPointWriteResult(FtmsConstants.Opcode.RequestControl, true, false, null), result);
    }

    [Fact]
    public async Task WriteControlPoint_ReportsAFailedWrite()
    {
        await device.ConnectAsync(42);
        ControlPoint.OnWrite = _ => false;

        var result = await device.WriteControlPointAndAwaitAsync([FtmsConstants.Opcode.RequestControl]);

        Assert.False(result.WriteSucceeded);
    }

    [Fact]
    public async Task WriteControlPoint_ReportsAThrowingWriteAsFailed()
    {
        await device.ConnectAsync(42);
        ControlPoint.OnWrite = _ => throw new InvalidOperationException();

        var result = await device.WriteControlPointAndAwaitAsync([FtmsConstants.Opcode.RequestControl]);

        Assert.False(result.WriteSucceeded);
    }

    [Fact]
    public async Task WriteControlPoint_FailsWithoutAControlPoint()
    {
        var result = await device.WriteControlPointAndAwaitAsync([FtmsConstants.Opcode.RequestControl]);

        Assert.Equal(new ControlPointWriteResult(FtmsConstants.Opcode.RequestControl, false, false, null), result);
    }

    [Fact]
    public async Task ReadSupportedSpeedRange_DecodesTheValue()
    {
        await device.ConnectAsync(42);
        connector.Link[FtmsConstants.SupportedSpeedRange].OnRead = () => [0x64, 0x00, 0xB0, 0x04, 0x0A, 0x00];

        Assert.Equal((1.0, 12.0, 0.1), await device.ReadSupportedSpeedRangeAsync());
    }

    [Fact]
    public async Task ReadSupportedSpeedRange_ReturnsNullWhenUnreadable()
    {
        Assert.Null(await device.ReadSupportedSpeedRangeAsync());

        await device.ConnectAsync(42);
        var range = connector.Link[FtmsConstants.SupportedSpeedRange];
        Assert.Null(await device.ReadSupportedSpeedRangeAsync());

        range.OnRead = () => [1, 2, 3];
        Assert.Null(await device.ReadSupportedSpeedRangeAsync());

        range.OnRead = () => throw new InvalidOperationException();
        Assert.Null(await device.ReadSupportedSpeedRangeAsync());
    }

    [Theory]
    [InlineData(new byte[] { 0x80, 0x02, 0x01 }, true)]
    [InlineData(new byte[] { 0x80, 0x02, 0x01, 0xFF }, true)]
    [InlineData(new byte[] { 0x80, 0x00, 0x01 }, false)]
    [InlineData(new byte[] { 0x81, 0x02, 0x01 }, false)]
    [InlineData(new byte[] { 0x80, 0x02 }, false)]
    public void IsResponseTo_MatchesOnlyAResponseToTheOpcode(byte[] indication, bool expected)
    {
        Assert.Equal(expected, FtmsDevice.IsResponseTo(indication, FtmsConstants.Opcode.SetTargetSpeed));
    }
}
