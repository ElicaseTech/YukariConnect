using System.Collections.Concurrent;
using System.Net;
using YukariConnect.Minecraft.Models;

namespace YukariConnect.Minecraft.Services;

/// <summary>
/// Manages the current state of all known Minecraft LAN servers.
/// Thread-safe for use from API endpoints and background listener events.
/// </summary>
public sealed class MinecraftLanState
{
    // Key: sender IP address
    private readonly ConcurrentDictionary<IPAddress, MinecraftLanAnnounce> _servers
        = new();

    /// <summary>
    /// Gets all currently online servers.
    /// </summary>
    public IReadOnlyCollection<MinecraftLanAnnounce> OnlineServers => _servers.Values.ToList().AsReadOnly();

    /// <summary>
    /// Gets the count of online servers.
    /// </summary>
    public int OnlineCount => _servers.Count;

    /// <summary>
    /// Gets a server by IP address, or null if not found.
    /// </summary>
    public MinecraftLanAnnounce? GetServer(IPAddress address) =>
        _servers.TryGetValue(address, out var server) ? server : null;

    /// <summary>
    /// Finds servers with a specific pattern in their MOTD.
    /// Useful for filtering Scaffolding rooms.
    /// </summary>
    public IReadOnlyCollection<MinecraftLanAnnounce> FindServersByMotdPattern(string pattern)
    {
        return _servers.Values
            .Where(s => s.Motd.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Adds or updates a server announcement.
    /// Called by the listener when receiving broadcasts.
    /// </summary>
    internal void AddOrUpdate(MinecraftLanAnnounce announce)
    {
        _servers[announce.Sender.Address] = announce;
    }

    /// <summary>
    /// Removes a server when it goes offline.
    /// Called by the listener sweep loop.
    /// </summary>
    internal bool Remove(IPAddress address) => _servers.TryRemove(address, out _);
}
