using BCUKCompanion.ExpiredMinotaur.Treadmill;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillAutoReconnectTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"bcuk-treadmill-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider clock = new();
    private readonly List<(string Title, string Text)> balloons = [];
    private int attempts;
    private long sequence;
    private bool connectSucceeds = true;

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private string MemoryFile => Path.Combine(folder, "treadmill-autoconnect");

    private (TreadmillAutoReconnect Auto, TreadmillReconnector Reconnector) Create()
    {
        var reconnector = new TreadmillReconnector(_ =>
        {
            attempts++;
            return Task.FromResult(connectSucceeds);
        }, clock);
        var auto = new TreadmillAutoReconnect(reconnector, MemoryFile) { ShowBalloon = (t, x) => balloons.Add((t, x)) };
        return (auto, reconnector);
    }

    [Fact]
    public void Connecting_IsRememberedForTheNextLaunch_AndDisconnectingByHandForgetsIt()
    {
        var (auto, _) = Create();
        using (auto)
        {
            auto.OnConnectionChanged(TreadmillConnectionChange.Connected, ++sequence);
            Assert.True(auto.WasConnected);

            auto.OnConnectionChanged(TreadmillConnectionChange.Disconnected, ++sequence);
            Assert.False(auto.WasConnected);
        }
    }

    [Fact]
    public void ADrop_ShowsABalloonAndStartsReconnecting_WithoutForgetting()
    {
        var (auto, reconnector) = Create();
        using (auto)
        {
            auto.OnConnectionChanged(TreadmillConnectionChange.Connected, ++sequence);
            auto.OnConnectionChanged(TreadmillConnectionChange.Dropped, ++sequence);

            Assert.Equal("Treadmill disconnected", Assert.Single(balloons).Title);
            Assert.True(reconnector.IsRunning);
            Assert.True(auto.WasConnected);

            // Connecting by hand meanwhile stops the retries.
            auto.OnConnectionChanged(TreadmillConnectionChange.Connected, ++sequence);
            Assert.False(reconnector.IsRunning);
        }
    }

    [Fact]
    public void ADropReportedAfterALaterDisconnect_IsIgnored()
    {
        var (auto, reconnector) = Create();
        using (auto)
        {
            auto.OnConnectionChanged(TreadmillConnectionChange.Connected, 1);
            // The drop (2) released the client's lock before a Disconnect click (3), but was raised after it.
            auto.OnConnectionChanged(TreadmillConnectionChange.Disconnected, 3);
            auto.OnConnectionChanged(TreadmillConnectionChange.Dropped, 2);

            Assert.False(reconnector.IsRunning);
            Assert.Empty(balloons);
            Assert.False(auto.WasConnected);
        }
    }

    [Fact]
    public async Task AReconnect_SaysSo()
    {
        var (auto, reconnector) = Create();
        using (auto)
        {
            Task run = reconnector.Start(immediately: true);
            await run.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("Treadmill reconnected", Assert.Single(balloons).Title);
        }
    }

    [Fact]
    public async Task StartAtLaunch_ReconnectsOnlyIfItWasConnectedLastTime()
    {
        var (first, _) = Create();
        using (first)
        {
            first.StartAtLaunch();
            Assert.Equal(0, attempts);

            first.OnConnectionChanged(TreadmillConnectionChange.Connected, ++sequence);
        }

        var (second, reconnector) = Create();
        using (second)
        {
            second.StartAtLaunch();
            await WaitUntilAsync(() => !reconnector.IsRunning);
            Assert.Equal(1, attempts);
        }
    }

    [Fact]
    public void AMemoryFileThatCantBeWritten_IsIgnored()
    {
        Directory.CreateDirectory(MemoryFile + ".tmp"); // Makes the save fail.
        var (auto, _) = Create();
        using (auto)
        {
            auto.OnConnectionChanged(TreadmillConnectionChange.Connected, ++sequence);

            Assert.False(auto.WasConnected);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
