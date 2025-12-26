using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using YukariConnect.Minecraft.Models;

namespace YukariConnect.Minecraft.Services;

/// <summary>
/// Manages the current state of all discovered and verified Minecraft servers.
/// </summary>
public sealed class MinecraftLanState
{
    // Key: server IP address
    private readonly ConcurrentDictionary<IPAddress, MinecraftServerInfo> _servers
        = new();

    /// <summary>
    /// Gets all servers (both discovered and verified).
    /// </summary>
    public IReadOnlyCollection<MinecraftServerInfo> AllServers => _servers.Values.ToList().AsReadOnly();

    /// <summary>
    /// Gets only verified servers (successfully pinged).
    /// </summary>
    public IReadOnlyCollection<MinecraftServerInfo> VerifiedServers =>
        _servers.Values.Where(s => s.IsVerified).ToList().AsReadOnly();

    /// <summary>
    /// Gets total server count.
    /// </summary>
    public int TotalCount => _servers.Count;

    /// <summary>
    /// Gets verified server count.
    /// </summary>
    public int VerifiedCount => _servers.Values.Count(s => s.IsVerified);

    /// <summary>
    /// Gets a server by IP address, or null if not found.
    /// </summary>
    public MinecraftServerInfo? GetServer(IPAddress address) =>
        _servers.TryGetValue(address, out var server) ? server : null;

    /// <summary>
    /// Finds servers with a specific pattern in their MOTD.
    /// </summary>
    public IReadOnlyCollection<MinecraftServerInfo> FindServersByMotdPattern(string pattern)
    {
        return _servers.Values
            .Where(s => s.Motd.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Adds or updates a server. Called by listener on discovery/verification.
    /// </summary>
    internal void AddOrUpdate(MinecraftServerInfo server)
    {
        _servers[server.EndPoint.Address] = server;
    }

    /// <summary>
    /// Removes a server. Called by listener when server goes offline/stale.
    /// </summary>
    internal bool Remove(IPAddress address) => _servers.TryRemove(address, out _);
}
