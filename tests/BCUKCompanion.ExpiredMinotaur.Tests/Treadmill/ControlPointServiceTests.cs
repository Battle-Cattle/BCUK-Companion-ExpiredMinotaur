using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class ControlPointServiceTests
{
    private sealed class FakeControlPoint : IControlPointWriter
    {
        private readonly Queue<byte?> resultCodes = new();

        public List<byte[]> Writes { get; } = [];

        public void Answer(params byte?[] codes)
        {
            foreach (var code in codes)
            {
                resultCodes.Enqueue(code);
            }
        }

        public Task<ControlPointWriteResult> WriteControlPointAndAwaitAsync(byte[] data)
        {
            Writes.Add(data);
            var code = resultCodes.Count > 0 ? resultCodes.Dequeue() : FtmsConstants.ResultSuccess;
            return Task.FromResult(new ControlPointWriteResult(data[0], true, code is not null, code));
        }
    }

    private readonly FakeControlPoint controlPoint = new();
    private readonly ControlPointService service;

    public ControlPointServiceTests()
    {
        service = new ControlPointService(controlPoint);
    }

    [Fact]
    public async Task RequestControlAsync_WritesRequestControlOpcode()
    {
        await service.RequestControlAsync();

        Assert.Equal(new byte[] { FtmsConstants.Opcode.RequestControl }, Assert.Single(controlPoint.Writes));
    }

    [Fact]
    public async Task StartOrResumeAsync_WritesStartOpcode()
    {
        await service.StartOrResumeAsync();

        Assert.Equal(new byte[] { FtmsConstants.Opcode.StartOrResume }, Assert.Single(controlPoint.Writes));
    }

    [Fact]
    public async Task StopAsync_WritesStopOrPauseWithStopParameter()
    {
        await service.StopAsync();

        Assert.Equal(new byte[] { FtmsConstants.Opcode.StopOrPause, 0x01 }, Assert.Single(controlPoint.Writes));
    }

    [Theory]
    [InlineData(5.5, 0x26, 0x02)]
    [InlineData(1.0, 0x64, 0x00)]
    [InlineData(12.0, 0xB0, 0x04)]
    public async Task SetTargetSpeedAsync_EncodesHundredthsOfKmhLittleEndian(double kmh, byte lo, byte hi)
    {
        var result = await service.SetTargetSpeedAsync(kmh);

        Assert.True(result.Accepted);
        Assert.Equal(new byte[] { FtmsConstants.Opcode.SetTargetSpeed, lo, hi }, Assert.Single(controlPoint.Writes));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(12.5)]
    public async Task SetTargetSpeedAsync_OutsideRange_Throws(double kmh)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetTargetSpeedAsync(kmh));
        Assert.Empty(controlPoint.Writes);
    }

    [Fact]
    public async Task SetTargetSpeedAsync_UsesTheDevicesOwnRange()
    {
        service.MaxSpeedKmh = 16.0;

        var result = await service.SetTargetSpeedAsync(15.0);

        Assert.True(result.Accepted);
    }

    [Fact]
    public async Task SetTargetSpeedAsync_ControlLost_RequestsControlAndRetriesOnce()
    {
        controlPoint.Answer(FtmsConstants.ResultControlNotPermitted, FtmsConstants.ResultSuccess, FtmsConstants.ResultSuccess);

        var result = await service.SetTargetSpeedAsync(5.0);

        Assert.True(result.Accepted);
        Assert.Equal(
            new[] { FtmsConstants.Opcode.SetTargetSpeed, FtmsConstants.Opcode.RequestControl, FtmsConstants.Opcode.SetTargetSpeed },
            controlPoint.Writes.Select(w => w[0]));
    }

    [Fact]
    public async Task SetTargetSpeedAsync_ControlRequestRefused_DoesNotRetry()
    {
        controlPoint.Answer(FtmsConstants.ResultControlNotPermitted, FtmsConstants.ResultControlNotPermitted);

        var result = await service.SetTargetSpeedAsync(5.0);

        Assert.Equal(FtmsConstants.ResultControlNotPermitted, result.ResultCode);
        Assert.Equal(2, controlPoint.Writes.Count);
    }

    [Theory]
    [InlineData(FtmsConstants.ResultInvalidParameter)]
    [InlineData(null)] // no indication
    public async Task SetTargetSpeedAsync_OtherFailure_DoesNotRetry(byte? code)
    {
        controlPoint.Answer(code);

        var result = await service.SetTargetSpeedAsync(5.0);

        Assert.False(result.Accepted);
        Assert.Single(controlPoint.Writes);
    }
}
