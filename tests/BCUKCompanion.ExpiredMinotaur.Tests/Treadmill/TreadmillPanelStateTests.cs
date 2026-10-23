using BCUKCompanion.ExpiredMinotaur.Treadmill;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillPanelStateTests
{
    [Fact]
    public void FormatSpeed_ShowsBothSpeedsToOneDecimalPlace()
    {
        Assert.Equal("Current: 4.5 km/h   Target: 5.0 km/h", TreadmillPanelState.FormatSpeed(4.49, 5));
    }

    [Theory]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    public void ConnectionButtons_EnableTheOneThatMatchesTheConnection_AndNeitherDuringAnOperation(
        bool inProgress, bool connected, bool connectEnabled, bool disconnectEnabled)
    {
        Assert.Equal((connectEnabled, disconnectEnabled), TreadmillPanelState.ConnectionButtons(inProgress, connected));
    }
}
