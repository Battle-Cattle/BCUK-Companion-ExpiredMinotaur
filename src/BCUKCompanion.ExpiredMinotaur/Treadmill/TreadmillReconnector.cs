namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>
/// Keeps trying to connect to the treadmill, waiting longer between attempts (5s, 10s, 20s, 40s,
/// then every minute) until one succeeds or <see cref="Stop"/> is called. Takes only a connect
/// delegate and a clock, so its timing can be tested without Bluetooth.
/// </summary>
public sealed class TreadmillReconnector : IDisposable
{
    /// <summary>The wait before the first retry after a drop.</summary>
    internal static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(5);

    /// <summary>The longest wait between attempts.</summary>
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);

    private readonly Func<CancellationToken, Task<bool>> connect;
    private readonly TimeProvider timeProvider;
    private readonly object gate = new();
    private CancellationTokenSource? running;

    /// <summary>Creates a reconnector that tries <paramref name="connect"/> on <paramref name="timeProvider"/>'s clock.</summary>
    /// <param name="connect">Tries once to connect; true when connected (including when it already was).</param>
    /// <param name="timeProvider">The clock the waits between attempts are measured with.</param>
    public TreadmillReconnector(Func<CancellationToken, Task<bool>> connect, TimeProvider timeProvider)
    {
        this.connect = connect;
        this.timeProvider = timeProvider;
    }

    /// <summary>Raised once an attempt succeeds.</summary>
    public event EventHandler? Reconnected;

    /// <summary>Whether it's currently trying.</summary>
    public bool IsRunning
    {
        get
        {
            lock (gate)
            {
                return running is not null;
            }
        }
    }

    /// <summary>Starts trying, unless it already is.</summary>
    /// <param name="immediately">True to make the first attempt now (at launch); false to wait <see cref="FirstDelay"/> first (after a drop).</param>
    /// <returns>A task that completes when it stops: connected, stopped, or already running (completes at once).</returns>
    public Task Start(bool immediately = false)
    {
        CancellationTokenSource cts;
        lock (gate)
        {
            if (running is not null)
            {
                return Task.CompletedTask;
            }

            cts = running = new CancellationTokenSource();
        }

        return RunAsync(immediately, cts);
    }

    /// <summary>Stops trying, e.g. because the user connected or disconnected by hand.</summary>
    public void Stop()
    {
        lock (gate)
        {
            running?.Cancel();
            running = null;
        }
    }

    /// <summary>Stops trying.</summary>
    public void Dispose() => Stop();

    /// <summary>The wait before attempt number <paramref name="attempt"/> (0 is the first).</summary>
    /// <param name="attempt">How many attempts have already been made.</param>
    /// <returns><see cref="FirstDelay"/> doubled per earlier attempt, capped at <see cref="MaxDelay"/>.</returns>
    internal static TimeSpan DelayBefore(int attempt) =>
        TimeSpan.FromTicks(Math.Min(MaxDelay.Ticks, FirstDelay.Ticks << Math.Min(attempt, 10)));

    /// <summary>Tries until connected or cancelled, then clears itself.</summary>
    /// <param name="immediately">Whether to skip the wait before the first attempt.</param>
    /// <param name="cts">Cancelled by <see cref="Stop"/>.</param>
    /// <returns>A task that completes when it's connected or stopped; it never faults.</returns>
    private async Task RunAsync(bool immediately, CancellationTokenSource cts)
    {
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                if (!(immediately && attempt == 0))
                {
                    await Task.Delay(DelayBefore(attempt), timeProvider, cts.Token).ConfigureAwait(false);
                }

                if (await TryConnectAsync(cts.Token).ConfigureAwait(false))
                {
                    Finish(cts);
                    Reconnected?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Stopped.
        }
        finally
        {
            Finish(cts);
            cts.Dispose();
        }
    }

    /// <summary>Makes one attempt, treating a failure to connect as "not yet".</summary>
    /// <param name="cancellationToken">Cancelled by <see cref="Stop"/>.</param>
    /// <returns>A task whose result is whether it's now connected.</returns>
    private async Task<bool> TryConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await connect(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Marks this run as over, unless a newer one has already replaced it.</summary>
    /// <param name="cts">This run's cancellation source.</param>
    private void Finish(CancellationTokenSource cts)
    {
        lock (gate)
        {
            if (ReferenceEquals(running, cts))
            {
                running = null;
            }
        }
    }
}
