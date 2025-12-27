using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace YukariConnect.Network;

/// <summary>
/// Windows Job Object wrapper to ensure child processes are terminated when parent exits.
/// </summary>
internal static class WindowsJobObject
{
    private static readonly ILogger logger = ApplicationLogging.CreateLogger(nameof(WindowsJobObject));

    #region Windows API

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        JOBOBJECTINFOCLASS JobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    #endregion

    #region Constants and Structs

    private const int JOB_OBJECT_LIMIT_BREAKAWAY_OK = 0x00000800;
    private const int JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    private enum JOBOBJECTINFOCLASS
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    #endregion

    private static IntPtr _jobHandle;
    private static readonly object _lock = new();

    /// <summary>
    /// Assigns a process to a job object that will terminate all child processes
    /// when the parent process exits.
    /// </summary>
    public static void AssignProcessToJobObject(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            logger.LogDebug("Job object is only supported on Windows, skipping for process {Pid}", process.Id);
            return;
        }

        lock (_lock)
        {
            try
            {
                // Create job object on first use
                if (_jobHandle == IntPtr.Zero)
                {
                    _jobHandle = CreateJobObject(IntPtr.Zero, null);
                    if (_jobHandle == IntPtr.Zero)
                    {
                        logger.LogError("Failed to create job object: error={Error}", Marshal.GetLastWin32Error());
                        return;
                    }

                    // Set JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE flag
                    var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                    {
                        BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                        {
                            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_BREAKAWAY_OK
                        }
                    };

                    var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    var infoPtr = Marshal.AllocHGlobal(length);

                    try
                    {
                        Marshal.StructureToPtr(info, infoPtr, false);
                        if (!SetInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.ExtendedLimitInformation, infoPtr, (uint)length))
                        {
                            logger.LogError("Failed to set job object info: error={Error}", Marshal.GetLastWin32Error());
                            CloseHandle(_jobHandle);
                            _jobHandle = IntPtr.Zero;
                            return;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(infoPtr);
                    }

                    logger.LogInformation("Created job object with KILL_ON_JOB_CLOSE flag");
                }

                // Assign process to job
                if (!AssignProcessToJobObject(_jobHandle, process.Handle))
                {
                    int error = Marshal.GetLastWin32Error();
                    // Error 5 (ACCESS_DENIED) is expected if the process is already in a job
                    if (error != 5)
                    {
                        logger.LogWarning("Failed to assign process {Pid} to job object: error={Error}", process.Id, error);
                    }
                }
                else
                {
                    logger.LogDebug("Successfully assigned process {Pid} to job object", process.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error assigning process {Pid} to job object", process.Id);
            }
        }
    }

    /// <summary>
    /// Closes the job object handle. Called during application shutdown.
    /// </summary>
    public static void Cleanup()
    {
        lock (_lock)
        {
            if (_jobHandle != IntPtr.Zero)
            {
                CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
                logger.LogDebug("Closed job object handle");
            }
        }
    }
}
