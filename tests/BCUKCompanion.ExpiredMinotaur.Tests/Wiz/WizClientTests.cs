using System.Net;
using System.Net.Sockets;
using System.Text;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz;

public sealed class WizClientTests
{
    private const string Success = """{"method":"setPilot","result":{"success":true}}""";

    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(600);

    [Fact]
    public async Task SetPilotAsync_SendsSetPilotWithTheParams_AndReportsSuccess()
    {
        using var device = new FakeWizDevice((_, _) => Success);

        // A long timeout gives each attempt time for the reply, so a slow runner doesn't resend.
        var ok = await device.CreateClient().SetPilotAsync(device.IpAddress, new { state = true }, TimeSpan.FromSeconds(15));

        Assert.True(ok);
        Assert.Equal("""{"method":"setPilot","params":{"state":true}}""", Assert.Single(device.Requests));
    }

    [Fact]
    public async Task SetPilotAsync_ReportsAnErrorReplyAsFailure()
    {
        using var device = new FakeWizDevice((_, _) => """{"error":{"code":-32600}}""");

        Assert.False(await device.CreateClient().SetPilotAsync(device.IpAddress, new { state = true }));
    }

    [Fact]
    public async Task SetPilotAsync_ResendsUntilTheDeviceReplies()
    {
        using var device = new FakeWizDevice((_, number) => number < 3 ? null : Success);

        var ok = await device.CreateClient().SetPilotAsync(device.IpAddress, new { state = false }, TimeSpan.FromSeconds(3));

        Assert.True(ok);
        Assert.Equal(3, device.Requests.Count);
    }

    [Fact]
    public async Task SetPilotAsync_FailsAfterThreeUnansweredAttempts()
    {
        using var device = new FakeWizDevice((_, _) => null);

        var ok = await device.CreateClient().SetPilotAsync(device.IpAddress, new { state = true }, ShortTimeout);

        Assert.False(ok);
        Assert.Equal(3, device.Requests.Count);
    }

    [Fact]
    public async Task SetPilotAsync_RejectsAnInvalidAddressWithoutSending()
    {
        using var device = new FakeWizDevice((_, _) => Success);

        Assert.False(await device.CreateClient().SetPilotAsync("not an ip", new { state = true }, ShortTimeout));
        Assert.Empty(device.Requests);
    }

    [Fact]
    public async Task SetPilotAsync_ReturnsFalseWhenCancelled()
    {
        using var device = new FakeWizDevice((_, _) => null);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ok = await device.CreateClient().SetPilotAsync(device.IpAddress, new { state = true }, TimeSpan.FromSeconds(30), cts.Token);

        Assert.False(ok);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed} to stop; the timeout was 30s.");
    }

    [Fact]
    public async Task SetPilotAsync_SendsWhileTheTokenIsNotCancelled()
    {
        using var device = new FakeWizDevice((_, _) => Success);
        using var cts = new CancellationTokenSource();

        Assert.True(await device.CreateClient().SetPilotAsync(device.IpAddress, new { state = true }, TimeSpan.FromSeconds(15), cts.Token));
        Assert.NotEmpty(device.Requests);
        Assert.All(device.Requests, r => Assert.Contains("setPilot", r));
    }

    [Fact]
    public async Task SetPilotAsync_IgnoresADatagramFromAnotherHost()
    {
        // The stray reply comes from 127.0.0.2, another loopback address, before the device's own.
        using var stray = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
        using var device = new FakeWizDevice((_, _) => null);
        device.AfterReply = client =>
        {
            stray.Send(Encoding.UTF8.GetBytes("""{"error":{}}"""), client);
            Thread.Sleep(50);
            device.Send(Success, client);
        };

        Assert.True(await device.CreateClient().SetPilotAsync(device.IpAddress, new { state = true }, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task GetPilotAsync_ReturnsTheDevicesState()
    {
        using var device = new FakeWizDevice((_, _) => """{"result":{"state":true,"dimming":40}}""");

        var status = await device.CreateClient().GetPilotAsync(device.IpAddress, TimeSpan.FromSeconds(15));

        Assert.Equal(new WizPilotStatus(true, 40, null, null, null, null), status);
        Assert.Equal("""{"method":"getPilot","params":{}}""", Assert.Single(device.Requests));
    }

    [Fact]
    public async Task GetPilotAsync_ReturnsNullWhenTheDeviceNeverReplies()
    {
        using var device = new FakeWizDevice((_, _) => null);

        Assert.Null(await device.CreateClient().GetPilotAsync(device.IpAddress, ShortTimeout));
    }

    [Fact]
    public async Task DiscoverAsync_YieldsEachDeviceOnce_SkippingUnreadableReplies()
    {
        using var device = new FakeWizDevice((_, _) => """{"result":{"moduleName":"ESP25_SHRGB_01"}}""");
        device.AfterReply = client =>
        {
            device.Send("""{"result":{"moduleName":"ESP25_SHRGB_01"}}""", client); // a repeat from the same address
        };
        using var other = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.3"), 0));
        using var noModule = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.4"), 0));
        var previous = device.AfterReply;
        device.AfterReply = client =>
        {
            previous(client);
            other.Send(Encoding.UTF8.GetBytes("not json"), client);
            noModule.Send(Encoding.UTF8.GetBytes("""{"result":{}}"""), client);
        };

        var found = new List<WizDiscoveredDevice>();
        await foreach (var d in device.CreateClient().DiscoverAsync(TimeSpan.FromMilliseconds(800)))
        {
            found.Add(d);
        }

        Assert.Equal([new WizDiscoveredDevice("127.0.0.1", "ESP25_SHRGB_01")], found);
        Assert.Equal("""{"method":"getSystemConfig","params":{}}""", Assert.Single(device.Requests));
    }

    [Fact]
    public async Task DiscoverAsync_EndsEarlyWhenCancelled()
    {
        using var device = new FakeWizDevice((_, _) => null);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var found = new List<WizDiscoveredDevice>();
        await foreach (var d in device.CreateClient().DiscoverAsync(TimeSpan.FromSeconds(30), cts.Token))
        {
            found.Add(d);
        }

        Assert.Empty(found);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed} to stop; the window was 30s.");
    }

    [Fact]
    public async Task DiscoverAsync_YieldsNothingWhenTheRequestCantBeSent()
    {
        // An IPv6 address can't be reached from the IPv4 socket discovery sends from.
        var client = new WizClient(38899, IPAddress.IPv6Loopback);

        var found = new List<WizDiscoveredDevice>();
        await foreach (var d in client.DiscoverAsync(TimeSpan.FromMilliseconds(200)))
        {
            found.Add(d);
        }

        Assert.Empty(found);
    }
}
