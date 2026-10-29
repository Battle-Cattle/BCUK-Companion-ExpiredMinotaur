using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz;

public sealed class WizActionExecutionTests
{
    private const string Success = """{"result":{"success":true}}""";

    private static (WizActionContext Context, WizDevice Device) ContextFor(FakeWizDevice fake)
    {
        var device = new WizDevice(Guid.NewGuid(), "Desk", fake.IpAddress, WizDeviceType.Light, true, true, true);
        return (new WizActionContext(fake.CreateClient(), [device]), device);
    }

    [Fact]
    public async Task TurnOn_SendsStateTrueToTheDevice()
    {
        using var fake = new FakeWizDevice((_, _) => Success);
        var (context, device) = ContextFor(fake);

        Assert.True(await new WizTurnOnAction { DeviceId = device.Id }.ExecuteAsync(context, CancellationToken.None));
        // WizClient resends until a reply arrives, so a slow first reply can mean more than one request.
        Assert.NotEmpty(fake.Requests);
        Assert.All(fake.Requests, r => Assert.Equal("""{"method":"setPilot","params":{"state":true}}""", r));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Toggle_SendsTheOppositeOfTheCurrentState(bool current, bool sent)
    {
        using var fake = new FakeWizDevice((request, _) =>
            request.Contains("getPilot") ? "{\"result\":{\"state\":" + Json(current) + "}}" : Success);
        var (context, device) = ContextFor(fake);

        Assert.True(await new WizToggleAction { DeviceId = device.Id }.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(
            "{\"method\":\"setPilot\",\"params\":{\"state\":" + Json(sent) + "}}",
            fake.Requests.Last());
    }

    private static string Json(bool value) => value ? "true" : "false";

    [Fact]
    public async Task Toggle_FailsWithoutSendingWhenTheStateIsUnknown()
    {
        using var fake = new FakeWizDevice((_, _) => """{"result":{"dimming":10}}""");
        var (context, device) = ContextFor(fake);

        Assert.False(await new WizToggleAction { DeviceId = device.Id }.ExecuteAsync(context, CancellationToken.None));
        Assert.NotEmpty(fake.Requests);
        Assert.All(fake.Requests, r => Assert.Contains("getPilot", r));
    }

    [Fact]
    public async Task AnActionWithoutAPayload_ThrowsNotSupported()
    {
        using var fake = new FakeWizDevice((_, _) => Success);
        var (context, device) = ContextFor(fake);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => new NoPayloadAction { DeviceId = device.Id }.ExecuteAsync(context, CancellationToken.None));
    }

    private sealed class NoPayloadAction : WizDeviceActionBase
    {
        public override string Kind => "test.noPayload";

        protected override bool SupportsPlugs => true;

        protected override string DescribeAction() => "Nothing";

        protected override IReadOnlyList<string> ValidateDeviceSpecific(WizDevice device) => [];
    }
}
