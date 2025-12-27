namespace YukariConnect.Configuration;

/// <summary>
/// YukariConnect application options.
/// </summary>
public class YukariOptions
{
    /// <summary>
    /// Terracotta compatibility mode.
    /// When true: Wait for MC server before starting, like Terracotta.
    /// When false (Yukari mode): Start immediately and dynamically update MC port.
    /// Default: true
    /// </summary>
    public bool TerracottaCompatibilityMode { get; set; } = true;

    /// <summary>
    /// MC server offline detection threshold (number of consecutive checks).
    /// Only applies in Terracotta compatibility mode.
    /// Default: 6 (approximately 30 seconds at 5-second check interval)
    /// </summary>
    public int McServerOfflineThreshold { get; set; } = 6;

    /// <summary>
    /// EasyTier startup timeout in seconds.
    /// Default: 12
    /// </summary>
    public int EasyTierStartupTimeoutSeconds { get; set; } = 12;

    /// <summary>
    /// Center discovery timeout in seconds.
    /// Default: 25
    /// </summary>
    public int CenterDiscoveryTimeoutSeconds { get; set; } = 25;

    /// <summary>
    /// HTTP server port.
    /// Default: 5062
    /// </summary>
    public int HttpPort { get; set; } = 5062;

    /// <summary>
    /// Default Scaffolding port for Host.
    /// Default: 13448
    /// </summary>
    public int DefaultScaffoldingPort { get; set; } = 13448;

    /// <summary>
    /// Launcher custom string for vendor identification.
    /// This string is appended to the vendor field in player profiles.
    /// Default: null (no custom string)
    /// Example: "mylauncher/1.0.0"
    /// </summary>
    public string? LauncherCustomString { get; set; }

    /// <summary>
    /// HTTP request paths to suppress logging for (frequently polled endpoints).
    /// Default: ["/state"]
    /// Set to empty array [] to log all requests.
    /// </summary>
    public string[] SuppressHttpLogPaths { get; set; } = new[] { "/state" };
}
