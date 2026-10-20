using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class ControlPointWriteResultTests
{
    [Fact]
    public void Accepted_SuccessIndication_IsTrue()
    {
        var result = new ControlPointWriteResult(0x02, true, true, FtmsConstants.ResultSuccess);

        Assert.True(result.Accepted);
        Assert.Equal("0x02 -> OK", result.ToString());
    }

    [Fact]
    public void Accepted_FailureIndication_IsFalse()
    {
        var result = new ControlPointWriteResult(0x02, true, true, FtmsConstants.ResultControlNotPermitted);

        Assert.False(result.Accepted);
        Assert.Equal("0x02 -> FAILED (0x05)", result.ToString());
    }

    [Fact]
    public void Accepted_NoIndication_IsFalse()
    {
        var result = new ControlPointWriteResult(0x07, true, false, null);

        Assert.False(result.Accepted);
        Assert.Equal("0x07 -> sent, no indication (timeout)", result.ToString());
    }

    [Fact]
    public void ToString_WriteFailed_SaysSo()
    {
        var result = new ControlPointWriteResult(0x00, false, false, null);

        Assert.False(result.Accepted);
        Assert.Equal("0x00 -> GATT write failed", result.ToString());
    }
}
