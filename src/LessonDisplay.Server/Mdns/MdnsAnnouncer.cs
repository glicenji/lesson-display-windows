using System.Net;
using System.Net.Sockets;

namespace LessonDisplay.Server.Mdns;

/// <summary>
/// Periodically broadcasts "&lt;hostname&gt;.local is at &lt;this machine's LAN IP&gt;"
/// over multicast DNS, so other computers on the same network can (best
/// effort) reach this PC by a friendly name instead of typing its IP. This
/// is a convenience layered on top of a guaranteed fallback — the tray app
/// always shows the plain IP address too — because multicast is exactly the
/// kind of thing a school's Wi-Fi/VLAN setup can silently block, and older
/// Windows machines without Bonjour installed have no ".local" resolver at
/// all. Nothing here breaks anything if it simply doesn't reach anyone.
/// </summary>
public sealed class MdnsAnnouncer : IDisposable
{
    private const int MdnsPort = 5353;
    private static readonly IPAddress MdnsGroup = IPAddress.Parse("224.0.0.251");

    private readonly string[] _hostNames;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cts;

    public MdnsAnnouncer(IEnumerable<string> hostNames, ILogger logger)
    {
        _hostNames = hostNames.Select(h => h.EndsWith('.') ? h : h + ".").ToArray();
        _logger = logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var client = new UdpClient(AddressFamily.InterNetwork);
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                var target = new IPEndPoint(MdnsGroup, MdnsPort);

                var ip = LocalNetwork.GetPrimaryIPv4();
                if (ip is not null)
                {
                    foreach (var name in _hostNames)
                    {
                        var packet = DnsWriter.BuildAAnnouncement(name, ip);
                        await client.SendAsync(packet, packet.Length, target);
                    }
                    _logger.LogDebug("mDNS: announced {Names} -> {Ip}", string.Join(", ", _hostNames), ip);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "mDNS announce attempt failed (non-fatal, will retry)");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(30), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose() => _cts?.Cancel();
}
