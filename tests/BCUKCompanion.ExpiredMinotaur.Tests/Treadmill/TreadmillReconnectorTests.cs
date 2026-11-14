using BCUKCompanion.ExpiredMinotaur.Treadmill;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillReconnectorTests
{
    private readonly FakeTimeProvider clock = new();
    private readonly Queue<Func<bool>> outcomes = new();
    private volatile int attempts;

    private TreadmillReconnector Create() => new(_ =>
    {
        attempts++;
        return Task.FromResult(outcomes.Count > 0 && outcomes.Dequeue()());
    }, clock);

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 60)]
    [InlineData(50, 60)]
    public void DelayBefore_DoublesUpToAMinute(int attempt, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), TreadmillReconnector.DelayBefore(attempt));
    }

    [Fact]
    public async Task AfterADrop_WaitsThenRetriesUntilItConnects()
    {
        outcomes.Enqueue(() => false);
        outcomes.Enqueue(() => throw new InvalidOperationException("radio off"));
        outcomes.Enqueue(() => true);
        using var reconnector = Create();
        int reconnected = 0;
        reconnector.Reconnected += (_, _) => reconnected++;

        Task run = reconnector.Start();
        Assert.Equal(0, attempts);
        Assert.True(reconnector.IsRunning);

        // Each attempt finishes (and the next wait starts) before the clock moves on.
        clock.Advance(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => attempts == 1);
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, attempts);
        clock.Advance(TimeSpan.FromSeconds(6));
        await WaitUntilAsync(() => attempts == 2);
        clock.Advance(TimeSpan.FromSeconds(20));
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, attempts);
        Assert.Equal(1, reconnected);
        Assert.False(reconnector.IsRunning);
    }

    [Fact]
    public async Task AtLaunch_TriesAtOnce()
    {
        outcomes.Enqueue(() => true);
        using var reconnector = Create();

        await reconnector.Start(immediately: true).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Stop_EndsTheRetries_AndAStartWhileRunningDoesNothing()
    {
        using var reconnector = Create();

        Task run = reconnector.Start();
        Assert.True(reconnector.Start().IsCompleted);
        reconnector.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(0, attempts);
        Assert.False(reconnector.IsRunning);
    }

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }

        // Let the loop reach its next wait, so the next Advance is seen by it.
        await Task.Delay(50);
    }
}
