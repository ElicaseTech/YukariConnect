namespace YukariConnect.Network;

/// <summary>
/// Abstract peer discovery service for finding hosts/centers on the network.
/// </summary>
public interface IPeerDiscoveryService
{
    /// <summary>
    /// Gets the list of public server addresses for initial connection.
    /// </summary>
    string[] GetPublicServers();

    /// <summary>
    /// Gets the validated list of public server addresses.
    /// Servers are checked for DNS resolution and basic connectivity.
    /// </summary>
    Task<string[]> GetValidatedPublicServersAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a random public server for load balancing.
    /// </summary>
    string? GetRandomServer();

    /// <summary>
    /// Gets the default public server.
    /// </summary>
    string? GetDefaultServer();
}
