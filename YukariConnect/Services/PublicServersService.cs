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
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

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
            var servers = JsonSerializer.Deserialize<string[]>(json, _jsonOptions);
            _cachedServers = servers ?? Array.Empty<string>();
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
