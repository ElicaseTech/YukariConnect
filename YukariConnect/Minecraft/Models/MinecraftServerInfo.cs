using System.Net;

namespace YukariConnect.Minecraft.Models;

/// <summary>
/// Extended server info with verification status.
/// </summary>
public sealed class MinecraftServerInfo
{
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
