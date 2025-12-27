using System.Text.Json;
using YukariConnect.Services;

namespace YukariConnect.Network;

/// <summary>
/// EasyTier implementation of INetworkNode.
/// Delegates to EasyTierCliService for actual CLI operations.
/// </summary>
public class EasyTierNetworkNode : INetworkNode
{
    private readonly EasyTierCliService _cliService;

    public EasyTierNetworkNode(EasyTierCliService cliService)
    {
        _cliService = cliService;
    }

    public Task<string> GetVersionAsync(CancellationToken ct = default)
        => _cliService.GetVersionAsync(ct);

    public Task<JsonDocument?> GetNodeInfoAsync(CancellationToken ct = default)
        => _cliService.NodeAsync(ct);

    public Task<JsonDocument?> GetPeersAsync(CancellationToken ct = default)
        => _cliService.PeersAsync(ct);

    public Task<bool> AddPortForwardAsync(string protocol, string localAddress, string remoteAddress, CancellationToken ct = default)
        => _cliService.AddPortForwardAsync(protocol, localAddress, remoteAddress, ct);

    public Task<bool> SetTcpWhitelistAsync(string[] ports, CancellationToken ct = default)
        => _cliService.SetTcpWhitelistAsync(ports, ct);

    public Task<bool> SetUdpWhitelistAsync(string[] ports, CancellationToken ct = default)
        => _cliService.SetUdpWhitelistAsync(ports, ct);
}
