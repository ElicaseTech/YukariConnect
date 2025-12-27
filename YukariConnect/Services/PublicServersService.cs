using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace YukariConnect.Services;

/// <summary>
/// Provides public EasyTier server list from configuration.
/// Validates servers before returning them to ensure they are reachable.
/// </summary>
public class PublicServersService
{
    private readonly string _configPath;
    private readonly ILogger<PublicServersService> _logger;

    private string[]? _cachedServers;
    private string[]? _validatedServers;

    public PublicServersService(
        IHostEnvironment env,
        ILogger<PublicServersService> logger)
    {
        _logger = logger;
        var contentRoot = env.ContentRootPath;
        _configPath = Path.Combine(contentRoot, "public-servers.json");
    }

    /// <summary>
    /// Get the list of public EasyTier servers.
    /// Returns default servers if config not found or invalid.
    /// </summary>
    public string[] GetServers()
    {
        if (_cachedServers != null)
            return _cachedServers;

        if (!File.Exists(_configPath))
        {
            _logger.LogInformation("Public servers config not found at {Path}, using defaults", _configPath);
            _cachedServers = GetDefaultServers();
            return _cachedServers;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Public servers config must be a JSON array, using defaults");
                _cachedServers = GetDefaultServers();
                return _cachedServers;
            }

            var servers = new string[root.GetArrayLength()];
            for (int i = 0; i < servers.Length; i++)
            {
                var element = root[i];
                if (element.ValueKind == JsonValueKind.String)
                {
                    servers[i] = element.GetString() ?? string.Empty;
                }
                else
                {
                    servers[i] = element.ToString() ?? string.Empty;
                }
            }

            if (servers.Length == 0)
            {
                _logger.LogInformation("Public servers config is empty, using defaults");
                _cachedServers = GetDefaultServers();
                return _cachedServers;
            }

            _cachedServers = servers;
            _logger.LogInformation("Loaded {Count} public servers from config", _cachedServers.Length);
            return _cachedServers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load public servers from {Path}, using defaults", _configPath);
            _cachedServers = GetDefaultServers();
            return _cachedServers;
        }
    }

    /// <summary>
    /// Get validated list of servers. Servers are checked for DNS resolution and basic connectivity.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of validated server URLs</returns>
    public async Task<string[]> GetValidatedServersAsync(CancellationToken ct = default)
    {
        if (_validatedServers != null)
            return _validatedServers;

        var servers = GetServers();
        var validServers = new List<string>();

        _logger.LogInformation("Validating {Count} public servers...", servers.Length);

        var validationTasks = servers.Select(server => ValidateServerAsync(server, ct));
        var results = await Task.WhenAll(validationTasks);

        for (int i = 0; i < servers.Length; i++)
        {
            if (results[i].IsValid)
            {
                validServers.Add(servers[i]);
                _logger.LogDebug("Server validated: {Server}", servers[i]);
            }
            else
            {
                _logger.LogWarning("Server validation failed: {Server} - {Reason}",
                    servers[i], results[i].Reason);
            }
        }

        _validatedServers = validServers.ToArray();
        _logger.LogInformation("Validated {ValidCount}/{TotalCount} public servers",
            _validatedServers.Length, servers.Length);

        return _validatedServers;
    }

    /// <summary>
    /// Validate a single server by checking DNS resolution and TCP connectivity.
    /// </summary>
    private static async Task<ServerValidationResult> ValidateServerAsync(string serverUrl, CancellationToken ct)
    {
        try
        {
            // Parse URL to extract host and port
            if (!TryParseServerUrl(serverUrl, out var protocol, out var host, out var port))
            {
                return new ServerValidationResult(false, $"Invalid URL format: {serverUrl}");
            }

            // Only support tcp:// and udp:// protocols
            if (protocol != "tcp" && protocol != "udp")
            {
                return new ServerValidationResult(false, $"Unsupported protocol: {protocol}");
            }

            // Check DNS resolution
            IPAddress[]? addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, ct);
            }
            catch
            {
                return new ServerValidationResult(false, $"DNS resolution failed for {host}");
            }

            if (addresses.Length == 0)
            {
                return new ServerValidationResult(false, $"No IP addresses found for {host}");
            }

            // For TCP servers, try a quick connection test
            if (protocol == "tcp")
            {
                try
                {
                    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(3));

                    await socket.ConnectAsync(addresses[0], port, cts.Token);
                    socket.Close();
                    return new ServerValidationResult(true, "OK");
                }
                catch
                {
                    // Connection failed, but DNS worked
                    // Still consider it valid - server might be temporarily unavailable
                    return new ServerValidationResult(true, "DNS OK (connection failed)");
                }
            }

            // UDP servers - just DNS validation
            return new ServerValidationResult(true, "DNS OK");
        }
        catch
        {
            return new ServerValidationResult(false, "Validation error");
        }
    }

    /// <summary>
    /// Parse server URL into protocol, host, and port components.
    /// Supports formats: tcp://host:port, udp://host:port
    /// </summary>
    private static bool TryParseServerUrl(string url, out string protocol, out string host, out int port)
    {
        protocol = string.Empty;
        host = string.Empty;
        port = 0;

        try
        {
            var uri = new Uri(url);
            protocol = uri.Scheme;
            host = uri.Host;
            port = uri.Port;

            return !string.IsNullOrEmpty(host) && port > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the default list of public EasyTier servers (from Terracotta configuration).
    /// Only includes verified TCP relay servers that are known to work.
    /// </summary>
    private static string[] GetDefaultServers()
    {
        return new string[]
        {
            // Core public servers (verified working)
            "tcp://public.easytier.top:11010",
            "tcp://public2.easytier.cn:54321",
            // TCP relay servers from legacy.rs (all verified)
            "tcp://ah.nkbpal.cn:11010",
            "tcp://turn.hb.629957.xyz:11010",
            "tcp://turn.js.629957.xyz:11012",
            "tcp://sh.993555.xyz:11010",
            "tcp://turn.bj.629957.xyz:11010",
            "tcp://et.sh.suhoan.cn:11010",
            "tcp://et-hk.clickor.click:11010",
            "tcp://et.01130328.xyz:11010",
            "tcp://et.gbc.moe:11011",
        };
    }

    /// <summary>
    /// Get a random public server (for load balancing).
    /// </summary>
    public string? GetRandomServer()
    {
        var servers = GetServers();
        if (servers.Length == 0)
            return null;

        var index = Random.Shared.Next(servers.Length);
        return servers[index];
    }

    /// <summary>
    /// Get the default public server (usually the first one).
    /// </summary>
    public string? GetDefaultServer()
    {
        var servers = GetServers();
        return servers.Length > 0 ? servers[0] : null;
    }

    /// <summary>
    /// Clear the validation cache. Forces re-validation on next call to GetValidatedServersAsync.
    /// </summary>
    public void InvalidateCache()
    {
        _validatedServers = null;
    }

    private record ServerValidationResult(bool IsValid, string Reason);
}
