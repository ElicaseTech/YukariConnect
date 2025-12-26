using System.Net;
using System.Net.Sockets;

namespace YukariConnect.Minecraft.Models;

/// <summary>
/// Extended server info with verification status.
/// </summary>
public sealed class MinecraftServerInfo
{
    // Cached local IPs (refreshed periodically)
    private static readonly object _localIpsLock = new();
    private static List<IPAddress>? _cachedLocalIps;
    private static DateTimeOffset _lastCacheRefresh = DateTimeOffset.MinValue;
    private static readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(30);

    public required IPEndPoint EndPoint { get; init; }
    public required string Motd { get; init; }
    public required string RawMotd { get; init; }

    /// <summary>
    /// When this server was last seen via broadcast.
    /// </summary>
    public DateTimeOffset BroadcastSeenAt { get; set; }

    /// <summary>
    /// When this server was last successfully pinged.
    /// </summary>
    public DateTimeOffset? LastPingAt { get; set; }

    /// <summary>
    /// Whether the server has been verified via ping.
    /// </summary>
    public bool IsVerified => LastPingAt.HasValue;

    /// <summary>
    /// Ping result data (version, players, etc.) if verified.
    /// </summary>
    public MinecraftPingResult? PingResult { get; set; }

    /// <summary>
    /// Whether the server is on localhost.
    /// </summary>
    public bool IsLocalHost => EndPoint.Address.Equals(IPAddress.Loopback) ||
                               EndPoint.Address.Equals(IPAddress.IPv6Loopback);

    /// <summary>
    /// Whether the server is on this machine (any local IP).
    /// This checks if the server's IP matches any of this machine's local IP addresses.
    /// </summary>
    public bool IsLocalNetwork
    {
        get
        {
            // Refresh cache if expired
            lock (_localIpsLock)
            {
                if (_cachedLocalIps == null || DateTimeOffset.UtcNow - _lastCacheRefresh > _cacheExpiration)
                {
                    _cachedLocalIps = GetAllLocalIPv4();
                    _lastCacheRefresh = DateTimeOffset.UtcNow;
                }
            }

            // Check if server IP matches any local IP (from same subnet)
            // Or if it's localhost
            if (IsLocalHost) return true;

            var serverBytes = EndPoint.Address.GetAddressBytes();
            if (serverBytes.Length != 4) return false; // IPv4 only

            lock (_localIpsLock)
            {
                foreach (var localIp in _cachedLocalIps!)
                {
                    var localBytes = localIp.GetAddressBytes();

                    // Check if same subnet (first 3 octets match)
                    // This handles cases like 192.168.1.x matching 192.168.1.5
                    if (localBytes[0] == serverBytes[0] &&
                        localBytes[1] == serverBytes[1] &&
                        localBytes[2] == serverBytes[2])
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Get all local IPv4 addresses (excluding loopback and link-local).
    /// </summary>
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

                // Skip link-local (169.254.x.x) and virtual network (10.144.x.x)
                var bytes = ua.Address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254) continue;
                if (bytes[0] == 10 && bytes[1] == 144) continue;

                result.Add(ua.Address);
            }
        }
        return result;
    }
}

/// <summary>
/// Result of a Minecraft server ping.
/// </summary>
public sealed class MinecraftPingResult
{
    public required string Version { get; init; }
    public required int Protocol { get; init; }
    public required int MaxPlayers { get; init; }
    public required int OnlinePlayers { get; init; }
    public required string Description { get; init; }
    public DateTimeOffset PingedAt { get; init; } = DateTimeOffset.UtcNow;
}
