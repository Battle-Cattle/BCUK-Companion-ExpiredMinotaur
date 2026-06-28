using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

public sealed record WizDiscoveredDevice(string IpAddress, string ModuleName);

public sealed record WizPilotStatus(bool State, int? Dimming, byte? R, byte? G, byte? B, int? Temp);

public sealed class WizClient
{
    private const int WizPort = 38899;

    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultDiscoveryWindow = TimeSpan.FromSeconds(3);

    // Lowercase parameter names so System.Text.Json emits the exact "method"/"params"
    // wire format the Wiz LAN protocol expects, with no naming-policy configuration needed.
    private sealed record WizRequest(string method, object @params);

    public async Task<bool> SetPilotAsync(
        string ipAddress, object paramsPayload, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var response = await SendAndReceiveAsync(
            ipAddress, new WizRequest("setPilot", paramsPayload), timeout ?? DefaultCommandTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (response is null)
        {
            return false;
        }

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
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<WizPilotStatus?> GetPilotAsync(
        string ipAddress, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var response = await SendAndReceiveAsync(
            ipAddress, new WizRequest("getPilot", new { }), timeout ?? DefaultCommandTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("result", out var result))
            {
                return null;
            }

            var state = result.TryGetProperty("state", out var stateEl) && stateEl.GetBoolean();
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

    public async IAsyncEnumerable<WizDiscoveredDevice> DiscoverAsync(
        TimeSpan? listenWindow = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient(0)
        {
            EnableBroadcast = true,
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new WizRequest("getSystemConfig", new { })));
        await udp.SendAsync(requestBytes, new IPEndPoint(IPAddress.Broadcast, WizPort), cancellationToken).ConfigureAwait(false);

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

    private static async Task<string?> SendAndReceiveAsync(
        string ipAddress, WizRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var udp = new UdpClient();
        var requestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

        try
        {
            await udp.SendAsync(requestBytes, new IPEndPoint(IPAddress.Parse(ipAddress), WizPort), cancellationToken)
                .ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
            return Encoding.UTF8.GetString(result.Buffer);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
