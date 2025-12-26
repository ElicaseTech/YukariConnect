using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using YukariConnect.Minecraft.Models;

namespace YukariConnect.Minecraft.Services;

/// <summary>
/// Listens for Minecraft LAN broadcasts and verifies servers via ping.
/// </summary>
public sealed partial class MinecraftLanListener : IHostedService, IAsyncDisposable
{
    // MC LAN: [MOTD]...[/MOTD][AD]12345[/AD]
    [GeneratedRegex(@"\[MOTD\](?<motd>.*)\[/MOTD\]\[AD\](?<port>\d{1,5})\[/AD\]",
        RegexOptions.Singleline)]
    private static partial Regex LanRegex();

    private const int MulticastPort = 4445;
    private static readonly IPAddress MulticastAddr = IPAddress.Parse("224.0.2.60");

    private readonly ILogger<MinecraftLanListener> _logger;
    private readonly MinecraftLanState _state;
    private readonly MinecraftPingService _pingService;
    private readonly TimeSpan _pingInterval;
    private readonly TimeSpan _broadcastTimeout;

    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _recvLoop;
    private Task? _pingLoop;
    private Task? _cleanupLoop;

    // Key: server IP
    private readonly ConcurrentDictionary<IPAddress, MinecraftServerInfo> _discovered
        = new();

    public MinecraftLanListener(
        MinecraftLanState? state = null,
        MinecraftPingService? pingService = null,
        TimeSpan? pingInterval = null,
        TimeSpan? broadcastTimeout = null,
        ILogger<MinecraftLanListener>? logger = null)
    {
        _state = state ?? new MinecraftLanState();
        _pingService = pingService ?? new MinecraftPingService();
        _pingInterval = pingInterval ?? TimeSpan.FromSeconds(10);
        _broadcastTimeout = broadcastTimeout ?? TimeSpan.FromSeconds(30);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MinecraftLanListener>.Instance;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cts != null) throw new InvalidOperationException("Already started.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));

            // Try to join multicast on all interfaces
            JoinMulticastOnAllInterfaces();

            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

            _recvLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
            _pingLoop = Task.Run(() => PingLoopAsync(_cts.Token), _cts.Token);
            _cleanupLoop = Task.Run(() => CleanupLoopAsync(_cts.Token), _cts.Token);

            _logger.LogInformation("Minecraft LAN listener started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Minecraft LAN listener");
            throw;
        }

        return Task.CompletedTask;
    }

    private void JoinMulticastOnAllInterfaces()
    {
        var interfaces = GetAllLocalIPv4();
        int joined = 0;

        foreach (var iface in interfaces)
        {
            try
            {
                _socket!.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                    new MulticastOption(MulticastAddr, iface));
                joined++;
            }
            catch
            {
                // Silently skip interfaces that don't support multicast
            }
        }

        _logger.LogInformation("Joined multicast on {Count}/{Total} interfaces", joined, interfaces.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts == null) return;

        _cts.Cancel();

        await Task.WhenAll(
            SafeAwait(_recvLoop),
            SafeAwait(_pingLoop),
            SafeAwait(_cleanupLoop)
        );

        try { _socket?.Close(); } catch { }
        try { _socket?.Dispose(); } catch { }

        _cts.Dispose();
        _cts = null;
        _socket = null;

        _logger.LogInformation("Minecraft LAN listener stopped");
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts != null)
        {
            await StopAsync(default);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (_socket == null) return;

        var buf = new byte[2048];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested)
        {
            int len;
            try
            {
                len = await Task.Run(() => _socket.ReceiveFrom(buf, ref remote), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch
            {
                continue;
            }

            if (len <= 0) continue;

            var sender = (IPEndPoint)remote;
            var payload = System.Text.Encoding.UTF8.GetString(buf, 0, len);

            var ann = TryParse(sender, payload);
            if (ann == null) continue;

            var endPoint = new IPEndPoint(sender.Address, ann.Port);
            var ip = sender.Address;

            // New discovery or update
            var isNew = !_discovered.ContainsKey(ip);
            _discovered.AddOrUpdate(ip,
                _ => new MinecraftServerInfo
                {
                    EndPoint = endPoint,
                    Motd = ann.Motd,
                    RawMotd = ann.RawPayload,
                    BroadcastSeenAt = DateTimeOffset.UtcNow
                },
                (_, existing) =>
                {
                    existing.BroadcastSeenAt = DateTimeOffset.UtcNow;
                    return existing;
                });

            if (isNew)
            {
                _logger.LogInformation("Discovered MC server: {EndPoint} MOTD='{Motd}'",
                    endPoint, ann.Motd);
            }
        }
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pingInterval, ct);
            }
            catch (OperationCanceledException) { break; }

            foreach (var kv in _discovered)
            {
                var server = kv.Value;
                var result = await _pingService.PingAsync(server.EndPoint, ct);

                if (result != null)
                {
                    if (!server.IsVerified)
                    {
                        _logger.LogInformation("Verified MC server: {EndPoint} Version={Version} Players={Online}/{Max}",
                            server.EndPoint, result.Version, result.OnlinePlayers, result.MaxPlayers);
                    }
                    server.LastPingAt = DateTimeOffset.UtcNow;
                    server.PingResult = result;
                    _state.AddOrUpdate(server);
                }
            }
        }
    }

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException) { break; }

            var now = DateTimeOffset.UtcNow;
            foreach (var kv in _discovered)
            {
                var server = kv.Value;

                // Remove if no broadcast for a long time AND not verified
                if (now - server.BroadcastSeenAt > _broadcastTimeout && !server.IsVerified)
                {
                    if (_discovered.TryRemove(kv.Key, out _))
                    {
                        _logger.LogInformation("Removed stale MC server: {EndPoint}", server.EndPoint);
                        _state.Remove(kv.Key);
                    }
                }

                // Remove verified servers that haven't responded to ping in a while
                if (server.IsVerified && server.LastPingAt.HasValue &&
                    now - server.LastPingAt.Value > TimeSpan.FromMinutes(2))
                {
                    if (_discovered.TryRemove(kv.Key, out _))
                    {
                        _logger.LogInformation("Verified MC server went offline: {EndPoint}", server.EndPoint);
                        _state.Remove(kv.Key);
                    }
                }
            }
        }
    }

    private static MinecraftLanAnnounce? TryParse(IPEndPoint sender, string payload)
    {
        var m = LanRegex().Match(payload);
        if (!m.Success) return null;

        var motd = m.Groups["motd"].Value;
        if (!ushort.TryParse(m.Groups["port"].Value, out var port)) return null;
        if (port == 0 || port > 65535) return null;

        return new MinecraftLanAnnounce
        {
            Sender = sender,
            Motd = motd,
            Port = port,
            RawPayload = payload,
            SeenAt = DateTimeOffset.UtcNow,
        };
    }

    private static List<IPAddress> GetAllLocalIPv4()
    {
        var result = new List<IPAddress>();
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;

            var nicType = nic.NetworkInterfaceType;
            if (nicType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

            var ipProps = nic.GetIPProperties();
            foreach (var ua in ipProps.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ua.Address)) continue;

                // Skip link-local
                var bytes = ua.Address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254) continue;

                result.Add(ua.Address);
            }
        }
        return result;
    }

    private static async Task SafeAwait(Task? t)
    {
        try { await (t ?? Task.CompletedTask).ConfigureAwait(false); } catch { }
    }
}
