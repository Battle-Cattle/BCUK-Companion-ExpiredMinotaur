using BCUKCompanion.ExpiredMinotaur.Wiz;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz;

public sealed class WizClientParsingTests
{
    [Theory]
    [InlineData("""{"method":"setPilot","env":"pro","result":{"success":true}}""", true)]
    [InlineData("""{"method":"setPilot","result":{"success":false}}""", false)]
    [InlineData("""{"method":"setPilot","result":{}}""", true)] // no success field
    [InlineData("""{"method":"setPilot"}""", true)]
    [InlineData("""{"method":"setPilot","error":{"code":-32600,"message":"Invalid Request"}}""", false)]
    [InlineData("not json", false)]
    [InlineData("""{"result":{"success":"yes"}}""", false)] // not a boolean
    public void ParseSetPilotResponse(string response, bool expected)
    {
        Assert.Equal(expected, WizClient.ParseSetPilotResponse(response));
    }

    [Fact]
    public void ParsePilotStatus_FullReply_ReadsEveryField()
    {
        var status = WizClient.ParsePilotStatus(
            """{"method":"getPilot","result":{"mac":"a8bb50000000","state":true,"dimming":75,"r":255,"g":128,"b":0,"temp":2700}}""");

        Assert.Equal(new WizPilotStatus(true, 75, 255, 128, 0, 2700), status);
    }

    [Fact]
    public void ParsePilotStatus_PlugWithoutStateField_LeavesStateUnknown()
    {
        var status = WizClient.ParsePilotStatus("""{"method":"getPilot","result":{"mac":"a8bb50000001"}}""");

        Assert.NotNull(status);
        Assert.Null(status.State);
        Assert.Null(status.Dimming);
    }

    [Theory]
    [InlineData("""{"method":"getPilot","error":{"code":-32601}}""")] // no result
    [InlineData("garbage")]
    [InlineData("""{"result":{"state":"on"}}""")] // wrong type
    [InlineData("""{"result":{"r":300}}""")] // out of range for a byte
    public void ParsePilotStatus_UnreadableReply_IsNull(string response)
    {
        Assert.Null(WizClient.ParsePilotStatus(response));
    }
}
