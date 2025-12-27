using System.Diagnostics;

namespace YukariConnect.Network;

/// <summary>
/// Network process configuration for starting a P2P network node.
/// </summary>
public record NetworkProcessConfig
{
    public required string NetworkName { get; init; }
    public required string NetworkSecret { get; init; }
    public required string Hostname { get; init; }
    public string? Ipv4 { get; init; }
    public bool IsHost { get; init; }
    public ushort? ScaffoldingPort { get; init; }
    public string[]? PublicServers { get; init; }
}

/// <summary>
/// Abstract network process service for managing P2P network node processes.
/// </summary>
public interface INetworkProcess
{
    /// <summary>
    /// Starts the network node process with the given configuration.
    /// </summary>
    Task<Process?> StartAsync(NetworkProcessConfig config, CancellationToken ct = default);

    /// <summary>
    /// Gets the current process instance if running.
    /// </summary>
    Process? CurrentProcess { get; }

    /// <summary>
    /// Checks if the process is currently running.
    /// </summary>
    bool IsRunning { get; }
}
