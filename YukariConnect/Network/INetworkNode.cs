using System.Text.Json;

namespace YukariConnect.Network;

/// <summary>
/// Abstract network node service for P2P networking.
/// Provides operations for peer discovery, port forwarding, and firewall management.
/// </summary>
public interface INetworkNode
{
    /// <summary>
    /// Gets the version of the network service.
    /// </summary>
    Task<string> GetVersionAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets information about this node.
    /// </summary>
    Task<JsonDocument?> GetNodeInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets list of connected peers.
    /// </summary>
    Task<JsonDocument?> GetPeersAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a port forwarding rule for tunneling traffic.
    /// </summary>
    /// <param name="protocol">Protocol type ("tcp" or "udp")</param>
    /// <param name="localAddress">Local bind address (e.g., "0.0.0.0:13448")</param>
    /// <param name="remoteAddress">Remote target address (e.g., "10.144.144.1:13448")</param>
    Task<bool> AddPortForwardAsync(string protocol, string localAddress, string remoteAddress, CancellationToken ct = default);

    /// <summary>
    /// Sets the TCP port whitelist for firewall.
    /// </summary>
    /// <param name="ports">Array of port numbers or ranges (e.g., ["80", "443", "8000-9000"])</param>
    Task<bool> SetTcpWhitelistAsync(string[] ports, CancellationToken ct = default);

    /// <summary>
    /// Sets the UDP port whitelist for firewall.
    /// </summary>
    /// <param name="ports">Array of port numbers or ranges</param>
    Task<bool> SetUdpWhitelistAsync(string[] ports, CancellationToken ct = default);
}
