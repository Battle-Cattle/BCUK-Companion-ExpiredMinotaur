using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

/// <summary>A device that answered discovery.</summary>
/// <param name="IpAddress">The address it replied from.</param>
/// <param name="ModuleName">Its reported module name, which identifies the model (and so its capabilities).</param>
public sealed record WizDiscoveredDevice(string IpAddress, string ModuleName);

/// <summary>A device's state from <c>getPilot</c>; each field is null when the reply omits it.</summary>
/// <param name="State">
/// Whether it's on. Null when the reply omits the "state" field (seen on some plug firmware):
/// callers must treat that as unknown, not silently assume off.
/// </param>
/// <param name="Dimming">Brightness, 1–100%.</param>
/// <param name="R">Red component of the colour.</param>
/// <param name="G">Green component of the colour.</param>
/// <param name="B">Blue component of the colour.</param>
/// <param name="Temp">Colour temperature, in kelvin.</param>
public sealed record WizPilotStatus(bool? State, int? Dimming, byte? R, byte? G, byte? B, int? Temp);

/// <summary>
/// Talks to WiZ lights and plugs over their LAN protocol: JSON commands in UDP datagrams to port
/// 38899, resent until the device replies, plus broadcast discovery.
/// </summary>
public sealed class WizClient
{
    /// <summary>The UDP port every WiZ device listens on.</summary>
    private const int WizPort = 38899;

    /// <summary>How long a command keeps being resent before it fails, unless the caller passes its own timeout.</summary>
    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(2);
    /// <summary>How long discovery listens for replies, unless the caller passes its own window.</summary>
    public static readonly TimeSpan DefaultDiscoveryWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// One request on the wire. Lowercase parameter names so System.Text.Json emits the exact
    /// "method"/"params" format the Wiz LAN protocol expects, with no naming-policy configuration needed.
    /// </summary>
    private sealed record WizRequest(string method, object @params);

    /// <summary>Sends a <c>setPilot</c> command (turn on/off, brightness, colour, …) to one device.</summary>
    /// <param name="ipAddress">The device's IP address.</param>
    /// <param name="paramsPayload">The command's <c>params</c> object, e.g. <c>new { state = true }</c>.</param>
    /// <param name="timeout">How long to keep resending before giving up; defaults to <see cref="DefaultCommandTimeout"/>.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>Whether the device replied and reported success.</returns>
    public async Task<bool> SetPilotAsync(
        string ipAddress, object paramsPayload, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var response = await SendAndReceiveAsync(
            ipAddress, new WizRequest("setPilot", paramsPayload), timeout ?? DefaultCommandTimeout, cancellationToken)
            .ConfigureAwait(false);
        return response is not null && ParseSetPilotResponse(response);
    }

    /// <summary>
    /// Whether a <c>setPilot</c> reply means success: no <c>error</c>, and <c>result.success</c> true
    /// (a reply with no <c>success</c> field counts as success). Unparsable replies count as failure.
    /// </summary>
    /// <param name="response">The reply's JSON text.</param>
    internal static bool ParseSetPilotResponse(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("error", out _))
            {
                return false;
            }
            if (doc.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("success", out var success))
            {
                return success.GetBoolean();
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Asks one device for its current state with <c>getPilot</c>.</summary>
    /// <param name="ipAddress">The device's IP address.</param>
    /// <param name="timeout">How long to keep resending before giving up; defaults to <see cref="DefaultCommandTimeout"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The device's state, or null if it didn't reply or the reply couldn't be read.</returns>
    public async Task<WizPilotStatus?> GetPilotAsync(
        string ipAddress, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var response = await SendAndReceiveAsync(
            ipAddress, new WizRequest("getPilot", new { }), timeout ?? DefaultCommandTimeout, cancellationToken)
            .ConfigureAwait(false);
        return response is null ? null : ParsePilotStatus(response);
    }

    /// <summary>Reads a <c>getPilot</c> reply's <c>result</c>; fields the reply omits stay null.</summary>
    /// <param name="response">The reply's JSON text.</param>
    /// <returns>The state, or null if the reply has no <c>result</c> or can't be parsed.</returns>
    internal static WizPilotStatus? ParsePilotStatus(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("result", out var result))
            {
                return null;
            }

            bool? state = result.TryGetProperty("state", out var stateEl) ? stateEl.GetBoolean() : null;
            int? dimming = result.TryGetProperty("dimming", out var dimmingEl) ? dimmingEl.GetInt32() : null;
            byte? r = result.TryGetProperty("r", out var rEl) ? rEl.GetByte() : null;
            byte? g = result.TryGetProperty("g", out var gEl) ? gEl.GetByte() : null;
            byte? b = result.TryGetProperty("b", out var bEl) ? bEl.GetByte() : null;
            int? temp = result.TryGetProperty("temp", out var tempEl) ? tempEl.GetInt32() : null;

            return new WizPilotStatus(state, dimming, r, g, b, temp);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Broadcasts <c>getSystemConfig</c> and yields each device that answers within the window,
    /// once per address.
    /// </summary>
    /// <param name="listenWindow">How long to listen; defaults to <see cref="DefaultDiscoveryWindow"/>.</param>
    /// <param name="cancellationToken">Ends discovery early.</param>
    public async IAsyncEnumerable<WizDiscoveredDevice> DiscoverAsync(
        TimeSpan? listenWindow = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient(0)
        {
            EnableBroadcast = true,
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new WizRequest("getSystemConfig", new { })));
        try
        {
            await udp.SendAsync(requestBytes, new IPEndPoint(IPAddress.Broadcast, WizPort), cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            yield break;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(listenWindow ?? DefaultDiscoveryWindow);

        var seenAddresses = new HashSet<string>();

        while (!cts.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // A broadcast send routinely provokes an ICMP "port unreachable" from some
                // other host on the LAN that isn't listening on the Wiz port; on Windows this
                // surfaces as a SocketException (WSAECONNRESET) on the shared socket's next
                // ReceiveAsync. That's not a reason to give up on discovery — it says nothing
                // about whether real Wiz devices are still about to reply — so skip it and keep
                // listening until the overall discovery window (cts) elapses. Any other socket
                // error means the socket itself is no longer usable, so let it end discovery
                // rather than spin retrying it.
                continue;
            }

            var ipAddress = received.RemoteEndPoint.Address.ToString();
            if (!seenAddresses.Add(ipAddress))
            {
                continue;
            }

            string? moduleName = null;
            try
            {
                using var doc = JsonDocument.Parse(received.Buffer);
                if (doc.RootElement.TryGetProperty("result", out var result)
                    && result.TryGetProperty("moduleName", out var moduleNameEl))
                {
                    moduleName = moduleNameEl.GetString();
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                continue;
            }

            if (moduleName is not null)
            {
                yield return new WizDiscoveredDevice(ipAddress, moduleName);
            }
        }
    }

    /// <summary>
    /// How many times a command is sent within its timeout. WiZ speaks plain UDP, so a single lost
    /// request or reply (ordinary on Wi-Fi) would otherwise fail the action outright; every command
    /// sent here is safe to repeat (setPilot sets absolute values, getPilot only reads).
    /// </summary>
    private const int SendAttempts = 3;

    /// <summary>
    /// Sends <paramref name="request"/> to the device and returns its reply, resending up to
    /// <see cref="SendAttempts"/> times, evenly spaced across <paramref name="timeout"/>, until one
    /// arrives. All attempts share one socket, so a late reply to an earlier attempt still counts.
    /// </summary>
    /// <returns>The reply's text, or null if none arrived in time (or the address is invalid).</returns>
    private static async Task<string?> SendAndReceiveAsync(
        string ipAddress, WizRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var udp = new UdpClient();
        var requestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
        var attemptWindow = TimeSpan.FromTicks(timeout.Ticks / SendAttempts);

        try
        {
            var targetAddress = IPAddress.Parse(ipAddress);
            for (var attempt = 1; attempt <= SendAttempts; attempt++)
            {
                await udp.SendAsync(requestBytes, new IPEndPoint(targetAddress, WizPort), cancellationToken)
                    .ConfigureAwait(false);

                var reply = await ReceiveFromAsync(udp, targetAddress, attemptWindow, cancellationToken).ConfigureAwait(false);
                if (reply is not null)
                {
                    return reply;
                }
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (FormatException ex)
        {
            // Defense in depth: ActionsConfigStore.Load() already filters out devices with an
            // unparsable IpAddress, but this guards any other caller that hands in a raw string.
            Debug.WriteLine($"Wiz command to '{ipAddress}' failed: '{ipAddress}' is not a valid IP address ({ex.Message}).");
            return null;
        }
    }

    /// <summary>
    /// Waits up to <paramref name="window"/> for a datagram from <paramref name="targetAddress"/>.
    /// Loops rather than a single ReceiveAsync: a stray datagram from another host on the LAN (e.g.
    /// another Wiz device's own reply) can land on this ephemeral socket and must not be mistaken
    /// for the intended device's response.
    /// </summary>
    /// <returns>The reply's text, or null if the window passed without one.</returns>
    /// <exception cref="OperationCanceledException">The caller's token was canceled.</exception>
    private static async Task<string?> ReceiveFromAsync(
        UdpClient udp, IPAddress targetAddress, TimeSpan window, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(window);
        try
        {
            while (true)
            {
                var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                if (result.RemoteEndPoint.Address.Equals(targetAddress))
                {
                    return Encoding.UTF8.GetString(result.Buffer);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
