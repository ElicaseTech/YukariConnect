using YukariConnect.Services;

namespace YukariConnect.Network;

/// <summary>
/// EasyTier implementation of IPeerDiscoveryService.
/// Delegates to PublicServersService for server list management.
/// </summary>
public class EasyTierPeerDiscoveryService : IPeerDiscoveryService
{
    private readonly PublicServersService _publicServers;

    public EasyTierPeerDiscoveryService(PublicServersService publicServers)
    {
        _publicServers = publicServers;
    }

    public string[] GetPublicServers()
        => _publicServers.GetServers();

    public Task<string[]> GetValidatedPublicServersAsync(CancellationToken ct = default)
        => _publicServers.GetValidatedServersAsync(ct);

    public string? GetRandomServer()
        => _publicServers.GetRandomServer();

    public string? GetDefaultServer()
        => _publicServers.GetDefaultServer();
}
