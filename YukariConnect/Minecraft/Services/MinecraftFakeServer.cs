using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace YukariConnect.Minecraft.Services;

/// <summary>
/// Broadcasts fake Minecraft LAN server announcements to the virtual network.
/// Allows guests to see the room in their MC "LAN Worlds" list.
/// </summary>
public sealed class MinecraftFakeServer : IAsyncDisposable
{
    // MC LAN multicast address: 224.0.2.60:4445
    private static readonly IPAddress MulticastAddr = IPAddress.Parse("224.0.2.60");
    private const int MulticastPort = 4445;

    private readonly ushort _port;
    private readonly string _motd;
    private readonly TimeSpan _broadcastInterval;
    private readonly ILogger<MinecraftFakeServer> _logger;

    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _broadcastLoop;

    /// <summary>
    /// Create a new fake MC server broadcaster.
    /// </summary>
    public MinecraftFakeServer(
        ushort port,
        string motd,
        TimeSpan? broadcastInterval = null,
        ILogger<MinecraftFakeServer>? logger = null)
    {
        _port = port;
        _motd = motd;
        _broadcastInterval = broadcastInterval ?? TimeSpan.FromMilliseconds(1500);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MinecraftFakeServer>.Instance;
    }

    /// <summary>
    /// Start broadcasting to the virtual network.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_socket != null)
            throw new InvalidOperationException("Already started.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Create UDP socket
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // Enable broadcast and multicast
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
        _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MulticastAddr, IPAddress.Any));

        // Bind to any available port
        _socket.Bind(new IPEndPoint(IPAddress.Any, 0));

        _logger.LogInformation("FakeServer broadcasting {Motd} on port {Port}", _motd, _port);

        // Start broadcast loop
        _broadcastLoop = Task.Run(() => BroadcastLoopAsync(_cts.Token));

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stop broadcasting.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts == null) return;

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;

        if (_broadcastLoop != null)
        {
            try { await _broadcastLoop; } catch { }
            _broadcastLoop = null;
        }

        _socket?.Close();
        _socket?.Dispose();
        _socket = null;

        _logger.LogInformation("FakeServer stopped");
    }

    /// <summary>
    /// Background broadcast loop.
    /// </summary>
    private async Task BroadcastLoopAsync(CancellationToken ct)
    {
        var endPoint = new IPEndPoint(MulticastAddr, MulticastPort);

        // Build message: [MOTD]{motd}[/MOTD][AD]{port}[/AD]
        var message = $"[MOTD]{_motd}[/MOTD][AD]{_port}[/AD]";
        var data = System.Text.Encoding.UTF8.GetBytes(message);

        _logger.LogDebug("Broadcast message: {Message}", message);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_socket != null)
                {
                    await _socket.SendToAsync(data, endPoint, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast");
            }

            try
            {
                await Task.Delay(_broadcastInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
