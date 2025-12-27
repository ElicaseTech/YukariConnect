using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace YukariConnect.Network;

/// <summary>
/// Cross-platform child process manager.
/// Ensures child processes are terminated when parent exits.
/// </summary>
internal static class ChildProcessManager
{
    private static readonly ILogger logger = ApplicationLogging.CreateLogger(nameof(ChildProcessManager));
    private static readonly object _lock = new();
    private static bool _initialized = false;

    /// <summary>
    /// Sets up process cleanup on parent exit for the given process.
    /// </summary>
    public static void SetupProcessCleanup(Process process)
    {
        lock (_lock)
        {
            // Initialize platform-specific handler on first use
            if (!_initialized)
            {
                InitializePlatformHandler();
                _initialized = true;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            SetupWindowsCleanup(process);
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            SetupUnixCleanup(process);
        }
    }

    private static void InitializePlatformHandler()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            UnixChildProcess.SetupSignalHandler();
            logger.LogInformation("Unix: Set up process group handler for child process cleanup");
        }
    }

    /// <summary>
    /// Windows: Use Job Objects
    /// </summary>
    private static void SetupWindowsCleanup(Process process)
    {
        try
        {
            WindowsJobObject.AssignProcessToJobObject(process);
            logger.LogDebug("Windows: Assigned process {Pid} to job object", process.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to assign process {Pid} to job object", process.Id);
        }
    }

    /// <summary>
    /// Unix (Linux/macOS): Use process groups
    /// </summary>
    private static void SetupUnixCleanup(Process process)
    {
        try
        {
            UnixChildProcess.SetProcessGroup(process);
            logger.LogDebug("Unix: Set process group for process {Pid}", process.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set up Unix process cleanup for process {Pid}", process.Id);
        }
    }

    /// <summary>
    /// Cleanup resources. Called during application shutdown.
    /// </summary>
    public static void Cleanup()
    {
        lock (_lock)
        {
            if (OperatingSystem.IsWindows())
            {
                WindowsJobObject.Cleanup();
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                UnixChildProcess.Cleanup();
            }
        }
    }
}
