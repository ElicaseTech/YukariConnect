using System.Text.Json;
using Microsoft.Extensions.Logging;
using YukariConnect.Network;

namespace YukariConnect.Configuration;

/// <summary>
/// YukariConnect configuration loader.
/// </summary>
public static class YukariConfiguration
{
    private static readonly ILogger logger = ApplicationLogging.CreateLogger(nameof(YukariConfiguration));

    /// <summary>
    /// Configuration file name.
    /// </summary>
    private const string CONFIG_FILE = "yukari.json";

    /// <summary>
    /// Load configuration from file or create default.
    /// </summary>
    public static YukariOptions LoadOrCreate(string? configPath = null)
    {
        var path = configPath ?? CONFIG_FILE;

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var options = JsonSerializer.Deserialize(json, YukariSerializerContext.Default.YukariOptions);
                if (options != null)
                {
                    logger.LogInformation("Loaded configuration from {Path}", Path.GetFullPath(path));
                    return options;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load configuration from {Path}, using defaults", path);
            }
        }
        else
        {
            logger.LogInformation("Configuration file not found at {Path}, creating with defaults", Path.GetFullPath(path));

            // Create default configuration file
            var defaultOptions = new YukariOptions();
            Save(path, defaultOptions);

            return defaultOptions;
        }

        return new YukariOptions();
    }

    /// <summary>
    /// Save configuration to file.
    /// </summary>
    public static void Save(string? configPath, YukariOptions options)
    {
        var path = configPath ?? CONFIG_FILE;

        try
        {
            var json = JsonSerializer.Serialize(options, YukariSerializerContext.Default.YukariOptions);
            File.WriteAllText(path, json);
            logger.LogInformation("Saved configuration to {Path}", Path.GetFullPath(path));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save configuration to {Path}", path);
        }
    }
}
