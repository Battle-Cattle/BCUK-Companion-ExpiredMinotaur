using System.Diagnostics;
using System.IO;
using BCUKCompanion.Core;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>
/// Keeps the treadmill connected without the Actions window: reconnects (with
/// <see cref="TreadmillReconnector"/>) after the connection drops, shows a balloon when it drops
/// and when it comes back, and at launch reconnects if it was connected when the app last ran.
/// A Disconnect click stops all of it until the next Connect.
/// </summary>
public sealed class TreadmillAutoReconnect : IDisposable
{
    private readonly TreadmillReconnector reconnector;
    private readonly string memoryFile;

    /// <summary>Creates the coordinator; call <see cref="Attach"/> to start following a client.</summary>
    /// <param name="reconnector">Retries the connection after a drop, or at launch.</param>
    /// <param name="memoryFile">Where "it was connected when the app last ran" is remembered.</param>
    public TreadmillAutoReconnect(TreadmillReconnector reconnector, string memoryFile)
    {
        this.reconnector = reconnector;
        this.memoryFile = memoryFile;
        reconnector.Reconnected += (_, _) => ShowBalloon?.Invoke("Treadmill reconnected", "Speed actions work again.");
    }

    /// <summary>Shows a tray balloon (title, text); null until the tray icon is ready.</summary>
    public Action<string, string>? ShowBalloon { get; set; }

    /// <summary>Whether the treadmill was connected when the app last ran (and wasn't disconnected by hand since).</summary>
    public bool WasConnected => File.Exists(memoryFile);

    /// <summary>Follows <paramref name="client"/>'s connection changes.</summary>
    /// <param name="client">The treadmill client to keep connected.</param>
    public void Attach(TreadmillClient client) => client.ConnectionChanged += (_, change) => OnConnectionChanged(change);

    /// <summary>At launch, starts reconnecting at once if the treadmill was connected last time.</summary>
    public void StartAtLaunch()
    {
        if (WasConnected)
        {
            _ = reconnector.Start(immediately: true);
        }
    }

    /// <summary>Reacts to a connection change: remember it, and reconnect after a drop.</summary>
    /// <param name="change">How the connection changed.</param>
    internal void OnConnectionChanged(TreadmillConnectionChange change)
    {
        switch (change)
        {
            case TreadmillConnectionChange.Connected:
                reconnector.Stop();
                Remember(true);
                break;
            case TreadmillConnectionChange.Disconnected:
                reconnector.Stop();
                Remember(false);
                break;
            case TreadmillConnectionChange.Dropped:
                ShowBalloon?.Invoke("Treadmill disconnected", "It was powered off or went out of range. Reconnecting in the background…");
                _ = reconnector.Start();
                break;
        }
    }

    /// <summary>Stops reconnecting.</summary>
    public void Dispose() => reconnector.Dispose();

    /// <summary>Records, best-effort, whether to reconnect at the next launch.</summary>
    /// <param name="connected">True to reconnect at launch; false not to.</param>
    private void Remember(bool connected)
    {
        try
        {
            if (connected)
            {
                AtomicFile.WriteAllText(memoryFile, "connected");
            }
            else
            {
                File.Delete(memoryFile);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't remember the treadmill connection in '{memoryFile}': {ex.Message}");
        }
    }
}
