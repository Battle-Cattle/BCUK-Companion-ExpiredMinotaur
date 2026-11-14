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

    [Fact]
    public async Task AnAttemptThatTimesOut_IsRetriedLikeAnyOtherFailure()
    {
        outcomes.Enqueue(() => throw new TaskCanceledException("BLE connect timed out"));
        outcomes.Enqueue(() => true);
        using var reconnector = Create();
        int reconnected = 0;
        reconnector.Reconnected += (_, _) => reconnected++;

        Task run = reconnector.Start(immediately: true);
        await WaitUntilAsync(() => attempts == 1);
        clock.Advance(TimeSpan.FromSeconds(10));
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, attempts);
        Assert.Equal(1, reconnected);
        Assert.False(reconnector.IsRunning);
    }

    [Fact]
    public async Task ADropReportedDuringASuccessfulAttempt_ChecksTheConnectionAgain()
    {
        TreadmillReconnector? reconnector = null;
        bool stillConnected = false;
        reconnector = new TreadmillReconnector(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                // The attempt connects, then the treadmill drops before it returns.
                reconnector!.Start();
                return Task.FromResult(true);
            }

            stillConnected = true;
            return Task.FromResult(true);
        }, clock);
        using (reconnector)
        {
            Task run = reconnector.Start(immediately: true);
            await run.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, attempts);
            Assert.True(stillConnected);
            Assert.False(reconnector.IsRunning);
        }
    }

    [Fact]
    public async Task AStartRacingTheEndOfASuccessfulRun_AlwaysGetsAnotherCheck()
    {
        // Whenever the second Start lands (during the attempt, as the run finishes, or after it),
        // some attempt must begin after it. Racing them many times catches a gap between the
        // run's last "check again?" and its finish, where a Start used to be lost.
        for (int i = 0; i < 20000; i++)
        {
            int made = 0;
            using var reconnector = new TreadmillReconnector(_ =>
            {
                Interlocked.Increment(ref made);
                return Task.FromResult(true);
            }, clock);
            using var go = new ManualResetEventSlim();
            Task race = Task.Run(() =>
            {
                go.Wait();
                while (Volatile.Read(ref made) == 0)
                {
                }

                int before = Volatile.Read(ref made);
                return (before, reconnector.Start(immediately: true));
            });

            Task run = Task.Run(() => { go.Set(); return reconnector.Start(immediately: true); });
            (int beforeSecond, Task second) = await ((Task<(int, Task)>)race);
            await Task.WhenAll(run, second).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(Volatile.Read(ref made) > beforeSecond, $"Iteration {i}: the second Start got no attempt after it.");
        }
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
