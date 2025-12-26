using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace YukariConnect.Services;

/// <summary>
/// Provides public EasyTier server list from configuration.
/// </summary>
public class PublicServersService
{
    private readonly string _configPath;
    private readonly ILogger<PublicServersService> _logger;

    private string[]? _cachedServers;

    public PublicServersService(
        IHostEnvironment env,
        ILogger<PublicServersService> logger)
    {
        _logger = logger;
        // Try multiple locations for the config file
        var contentRoot = env.ContentRootPath;
        _configPath = Path.Combine(contentRoot, "public-servers.json");
    }

    /// <summary>
    /// Get the list of public EasyTier servers.
    /// Returns empty array if config not found or invalid.
    /// </summary>
    public string[] GetServers()
    {
        if (_cachedServers != null)
            return _cachedServers;

        if (!File.Exists(_configPath))
        {
            _logger.LogWarning("Public servers config not found at {Path}", _configPath);
            _cachedServers = Array.Empty<string>();
            return _cachedServers;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Public servers config must be a JSON array");
                _cachedServers = Array.Empty<string>();
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

            _cachedServers = servers;
            _logger.LogInformation("Loaded {Count} public servers", _cachedServers.Length);
            return _cachedServers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load public servers from {Path}", _configPath);
            _cachedServers = Array.Empty<string>();
            return _cachedServers;
        }
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
}
