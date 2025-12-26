using System.Net;

namespace YukariConnect.Minecraft.Models;

/// <summary>
/// Represents a Minecraft LAN broadcast announcement.
/// </summary>
public sealed class MinecraftLanAnnounce
{
    /// <summary>
    /// The sender endpoint (IP and port) of the broadcast.
    /// </summary>
    public required IPEndPoint Sender { get; init; }

    /// <summary>
    /// The MOTD (Message of the Day) from the broadcast.
    /// May contain custom room code information.
    /// </summary>
    public required string Motd { get; init; }

    /// <summary>
    /// The server port announced.
    /// </summary>
    public required ushort Port { get; init; }

    /// <summary>
    /// Raw payload for debugging or custom parsing.
    /// </summary>
    public string RawPayload { get; init; } = string.Empty;

    /// <summary>
    /// When this announcement was last seen.
    /// </summary>
    public DateTimeOffset SeenAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether this announcement is from localhost.
    /// </summary>
    public bool IsLocalHost => Sender.Address.Equals(IPAddress.Loopback) ||
                               Sender.Address.Equals(IPAddress.IPv6Loopback);
}
