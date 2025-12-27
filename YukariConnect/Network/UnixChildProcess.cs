using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace YukariConnect.Network;

/// <summary>
/// Unix-specific (Linux/macOS) child process management using process groups.
/// </summary>
internal static class UnixChildProcess
{
    private static readonly ILogger logger = ApplicationLogging.CreateLogger(nameof(UnixChildProcess));
    private static readonly List<int> _childProcessGroups = new();
    private static readonly object _lock = new();
    private static bool _initialized = false;

    #region Unix API

    [DllImport("libc", SetLastError = true, EntryPoint = "setpgid")]
    private static extern int SetPgid(int pid, int pgid);

    [DllImport("libc", SetLastError = true, EntryPoint = "getpgrp")]
    private static extern int GetPgrp();

    [DllImport("libc", SetLastError = true)]
    private static extern int KillPg(int pgrp, int sig);

    private const int SIGTERM = 15;
    private const int SIGKILL = 9;

    #endregion

    /// <summary>
    /// Sets up a signal handler that will terminate all child process groups on exit.
    /// </summary>
    public static void SetupSignalHandler()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        lock (_lock)
        {
            if (_initialized)
                return;

            try
            {
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                _initialized = true;
                logger.LogDebug("Registered Unix process exit handler");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to register process exit handler");
            }
        }
    }

    /// <summary>
    /// Sets the process group for a process.
    /// </summary>
    public static void SetProcessGroup(Process process)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        lock (_lock)
        {
            try
            {
                int result = SetPgid(process.Id, process.Id);

                if (result == 0)
                {
                    _childProcessGroups.Add(process.Id);
                    logger.LogDebug("Set process group for PID {Pid}", process.Id);
                }
                else
                {
                    logger.LogWarning("Failed to set process group for PID {Pid}: errno={Errno}",
                        process.Id, Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error setting process group for PID {Pid}", process.Id);
            }
        }
    }

    /// <summary>
    /// Cleanup resources.
    /// </summary>
    public static void Cleanup()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        TerminateChildProcessGroups();
    }

    private static void TerminateChildProcessGroups()
    {
        lock (_lock)
        {
            if (_childProcessGroups.Count == 0)
                return;

            logger.LogInformation("Terminating {Count} child process groups...", _childProcessGroups.Count);

            foreach (int pgid in _childProcessGroups.ToList())
            {
                try
                {
                    int result = KillPg(pgid, SIGTERM);

                    if (result == 0)
                    {
                        logger.LogDebug("Sent SIGTERM to process group {Pgid}", pgid);
                        Thread.Sleep(100);
                        KillPg(pgid, SIGKILL);
                    }
                    else
                    {
                        logger.LogDebug("Process group {Pgid} already terminated or not found", pgid);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to terminate process group {Pgid}", pgid);
                }
            }

            _childProcessGroups.Clear();
        }
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        logger.LogInformation("Process exit detected, cleaning up child processes...");
        TerminateChildProcessGroups();
    }
}

