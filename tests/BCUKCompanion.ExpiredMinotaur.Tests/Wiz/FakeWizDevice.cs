using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using BCUKCompanion.ExpiredMinotaur.Wiz;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz;

/// <summary>
/// A WiZ device on a loopback UDP port: answers each request with whatever <see cref="Reply"/>
/// returns (null drops it), recording every request it gets.
/// </summary>
internal sealed class FakeWizDevice : IDisposable
{
    private readonly UdpClient socket = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;

    public FakeWizDevice(Func<string, int, string?> reply)
    {
        Reply = reply;
        loop = Task.Run(ListenAsync);
    }

    /// <summary>Picks the reply to a request, given its text and its 1-based number.</summary>
    public Func<string, int, string?> Reply { get; set; }

    /// <summary>Runs after each reply, with the sender's endpoint, so a test can send extra datagrams.</summary>
    public Action<IPEndPoint>? AfterReply { get; set; }

    public ConcurrentQueue<string> Requests { get; } = new();

    public int Port => (socket.Client.LocalEndPoint as IPEndPoint ?? throw new InvalidOperationException("The socket isn't bound.")).Port;

    public string IpAddress => IPAddress.Loopback.ToString();

    public WizClient CreateClient() => new(Port, IPAddress.Loopback);

    public void Send(string text, IPEndPoint to) => socket.Send(Encoding.UTF8.GetBytes(text), to);

    private async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await socket.ReceiveAsync(stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            var request = Encoding.UTF8.GetString(received.Buffer);
            Requests.Enqueue(request);
            var reply = Reply(request, Requests.Count);
            if (reply is not null)
            {
                Send(reply, received.RemoteEndPoint);
            }

            AfterReply?.Invoke(received.RemoteEndPoint);
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        socket.Dispose();
        loop.Wait(TimeSpan.FromSeconds(5));
    }
}
