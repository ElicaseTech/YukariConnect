using System.Diagnostics;
using Microsoft.Extensions.Logging;
using YukariConnect.Services;

namespace YukariConnect.Network;

/// <summary>
/// EasyTier implementation of INetworkProcess.
/// Manages the EasyTier core process lifecycle.
/// </summary>
public class EasyTierNetworkProcess : INetworkProcess
{
    private readonly IHostEnvironment _env;
    private readonly ILogger<EasyTierNetworkProcess> _logger;

    public Process? CurrentProcess { get; private set; }
    public bool IsRunning => CurrentProcess != null && !CurrentProcess.HasExited;

    public EasyTierNetworkProcess(IHostEnvironment env, ILogger<EasyTierNetworkProcess> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task<Process?> StartAsync(NetworkProcessConfig config, CancellationToken ct = default)
    {
        var resourceDir = Path.Combine(_env.ContentRootPath, "resource");
        var coreExe = Path.Combine(resourceDir, OperatingSystem.IsWindows() ? "easytier-core.exe" : "easytier-core");

        if (!File.Exists(coreExe))
        {
            _logger.LogError("EasyTier core not found at {Path}", coreExe);
            return null;
        }

        var args = BuildArguments(config);

        var psi = new ProcessStartInfo
        {
            FileName = coreExe,
            WorkingDirectory = resourceDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var process = Process.Start(psi);
        if (process == null)
        {
            _logger.LogError("Failed to start EasyTier process");
            return null;
        }

        // Set up cross-platform process cleanup
        ChildProcessManager.SetupProcessCleanup(process);
        _logger.LogInformation("EasyTier process {Pid} set up for auto-cleanup on parent exit", process.Id);

        // Log output asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync(ct) is string line)
                    _logger.LogInformation("[EasyTier-stdout] {Line}", line);
            }
            catch { }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync(ct) is string line)
                    _logger.LogWarning("[EasyTier-stderr] {Line}", line);
            }
            catch { }
        });

        CurrentProcess = process;
        _logger.LogInformation("EasyTier started with PID {Pid}", process.Id);

        return process;
    }

    private static List<string> BuildArguments(NetworkProcessConfig config)
    {
        var args = new List<string>
        {
            // Core options
            "--no-tun",
            "--multi-thread",
            "--latency-first",
            "--compression", "zstd",
            "--enable-kcp-proxy",
            "--p2p-only",
            // Network
            "--network-name", config.NetworkName,
            "--network-secret", config.NetworkSecret,
            "--hostname", config.Hostname,
        };

        // Host-specific options
        if (config.IsHost)
        {
            args.Add("--ipv4");
            args.Add(config.Ipv4 ?? "10.144.144.1");

            if (config.ScaffoldingPort.HasValue)
            {
                args.Add("--tcp-whitelist");
                args.Add(config.ScaffoldingPort.Value.ToString());
            }
        }
        else
        {
            // Guest uses DHCP
            args.Add("--dhcp");
        }

        // Listeners
        args.Add("-l");
        args.Add("udp://0.0.0.0:0");
        args.Add("-l");
        args.Add("tcp://0.0.0.0:0");

        // Public servers
        if (config.PublicServers != null)
        {
            foreach (var server in config.PublicServers)
            {
                args.Add("-p");
                args.Add(server);
            }
        }

        return args;
    }
}
