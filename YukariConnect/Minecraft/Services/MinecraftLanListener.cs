using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using YukariConnect.Minecraft.Models;

namespace YukariConnect.Minecraft.Services;

/// <summary>
/// Listens for Minecraft LAN broadcasts (UDP 4445, multicast 224.0.2.60).
/// Maintains online/offline state based on timeout.
/// </summary>
public sealed partial class MinecraftLanListener : IHostedService, IAsyncDisposable
{
    // MC LAN: [MOTD]...[/MOTD][AD]12345[/AD]
    [GeneratedRegex(@"\[MOTD\](?<motd>.*)\[/MOTD\]\[AD\](?<port>\d{1,5})\[/AD\]",
        RegexOptions.Singleline)]
    private static partial Regex LanRegex();

    private readonly IPAddress _multicastAddr = IPAddress.Parse("224.0.2.60");
    private const int Port = 4445;

    private readonly TimeSpan _staleTimeout;
    private readonly TimeSpan _sweepInterval;
    private readonly ILogger<MinecraftLanListener> _logger;
    private readonly MinecraftLanState _state;

    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _recvLoop;
    private Task? _sweepLoop;

    // Key: sender IP address
    private readonly ConcurrentDictionary<IPAddress, (MinecraftLanAnnounce Ann, DateTimeOffset LastSeen)> _alive
        = new();

    /// <summary>
    /// Fired when any announcement is received (heartbeat).
    /// </summary>
    public event Action<MinecraftLanAnnounce>? OnAnnounce;

    /// <summary>
    /// Fired when a server comes online (first announcement).
    /// </summary>
    public event Action<MinecraftLanAnnounce>? OnOnline;

    /// <summary>
    /// Fired when a server goes offline (timeout).
    /// </summary>
    public event Action<MinecraftLanAnnounce>? OnOffline;

    /// <summary>
    /// Gets the shared state service for querying servers via API.
    /// </summary>
    public MinecraftLanState State => _state;

    public MinecraftLanListener(
        MinecraftLanState? state = null,
        TimeSpan? staleTimeout = null,
        TimeSpan? sweepInterval = null,
        ILogger<MinecraftLanListener>? logger = null)
    {
        _state = state ?? new MinecraftLanState();
        _staleTimeout = staleTimeout ?? TimeSpan.FromSeconds(6);
        _sweepInterval = sweepInterval ?? TimeSpan.FromSeconds(2);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MinecraftLanListener>.Instance;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cts != null) throw new InvalidOperationException("Already started.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // UDP socket
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // Bind to 0.0.0.0:4445 to receive multicast
            _socket.Bind(new IPEndPoint(IPAddress.Any, Port));

            // Join multicast group 224.0.2.60 on ALL available IPv4 interfaces
            var interfaces = GetAllLocalIPv4();
            if (interfaces.Count == 0)
            {
                throw new InvalidOperationException("No usable IPv4 interface found for multicast membership.");
            }

            foreach (var iface in interfaces)
            {
                try
                {
                    _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                        new MulticastOption(_multicastAddr, iface));
                    _logger.LogInformation("Joined multicast group on interface {Interface}", iface);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to join multicast group on {Interface}", iface);
                }
            }
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

            _recvLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
            _sweepLoop = Task.Run(() => SweepLoopAsync(_cts.Token), _cts.Token);

            _logger.LogInformation("Minecraft LAN listener started on {MulticastAddr}:{Port}",
                _multicastAddr, Port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Minecraft LAN listener");
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts == null) return;

        try { _cts.Cancel(); } catch { /* ignore */ }

        if (_recvLoop != null) await SafeAwait(_recvLoop);
        if (_sweepLoop != null) await SafeAwait(_sweepLoop);

        try { _socket?.Close(); } catch { /* ignore */ }
        try { _socket?.Dispose(); } catch { /* ignore */ }

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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error receiving Minecraft LAN broadcast");
                continue;
            }

            if (len <= 0) continue;

            var sender = (IPEndPoint)remote;
            var payload = System.Text.Encoding.UTF8.GetString(buf, 0, len);

            // Only log at debug level for heartbeat packets (to avoid log spam)
            _logger.LogDebug("Received {Length} bytes from {Sender}: {Payload}",
                len, sender, payload.Replace("\n", "\\n").Replace("\r", "\\r"));

            var ann = TryParse(sender, payload);
            if (ann == null)
            {
                _logger.LogDebug("Payload did not match MC LAN format from {Sender}", sender);
                continue;
            }

            OnAnnounce?.Invoke(ann);

            var now = DateTimeOffset.UtcNow;
            var ip = sender.Address;

            // First time seeing this server => Online
            if (_alive.TryAdd(ip, (ann, now)))
            {
                _logger.LogInformation("New Minecraft LAN server: {Address}:{Port} MOTD='{Motd}'",
                    sender.Address, ann.Port, ann.Motd);
                _state.AddOrUpdate(ann);
                OnOnline?.Invoke(ann);
            }
            else
            {
                // Update last seen + latest announce
                _alive[ip] = (ann, now);
                _state.AddOrUpdate(ann);
            }
        }
    }

    private async Task SweepLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_sweepInterval, ct);
            }
            catch (OperationCanceledException) { break; }

            var now = DateTimeOffset.UtcNow;
            foreach (var kv in _alive)
            {
                var last = kv.Value.LastSeen;
                if (now - last <= _staleTimeout) continue;

                // Timeout: offline
                if (_alive.TryRemove(kv.Key, out var removed))
                {
                    _logger.LogInformation("Minecraft LAN server offline: {Address}", kv.Key);
                    _state.Remove(kv.Key);
                    OnOffline?.Invoke(removed.Ann);
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

    /// <summary>
    /// Gets all local IPv4 interfaces for multicast membership.
    /// This ensures we receive broadcasts on all network interfaces.
    /// </summary>
    private static List<IPAddress> GetAllLocalIPv4()
    {
        var result = new List<IPAddress>();
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

            var ipProps = nic.GetIPProperties();
            foreach (var ua in ipProps.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ua.Address)) continue;
                result.Add(ua.Address);
            }
        }
        return result;
    }

    private static async Task SafeAwait(Task t)
    {
        try { await t.ConfigureAwait(false); } catch { /* ignore */ }
    }
}
