using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YukariConnect.Scaffolding.Models;
using YukariConnect.Services;
using YukariConnect.Minecraft.Services;

namespace YukariConnect.Scaffolding;

/// <summary>
/// Host state machine steps implementation.
/// </summary>
public sealed partial class RoomController
{
    private async Task StepHostAsync(CancellationToken ct)
    {
        if (_state == RoomStateKind.Host_Prepare)
            await StepHost_PrepareAsync(ct);
        else if (_state == RoomStateKind.Host_EasyTierStarting)
            await StepHost_EasyTierStartingAsync(ct);
        else if (_state == RoomStateKind.Host_ScaffoldingStarting)
            await StepHost_ScaffoldingStartingAsync(ct);
        else if (_state == RoomStateKind.Host_MinecraftDetecting)
            await StepHost_MinecraftDetectingAsync(ct);
        else if (_state == RoomStateKind.Host_Running)
            await StepHost_RunningAsync(ct);
    }

    private async Task StepHost_PrepareAsync(CancellationToken ct)
    {
        _logger.LogInformation("Host prepare: network={Network}, room={Room}",
            _runtime!.NetworkName, _runtime.RoomCode);

        // Generate room code if not provided
        if (string.IsNullOrEmpty(_runtime.RoomCode))
        {
            _runtime.RoomCode = ScaffoldingHelpers.GenerateRoomCode(
                _runtime.NetworkName, _runtime.NetworkSecret);
        }

        _state = RoomStateKind.Host_EasyTierStarting;
        EmitStatus();
    }

    private async Task StepHost_EasyTierStartingAsync(CancellationToken ct)
    {
        if (_runtime!.EasyTierProcess != null)
        {
            // Already started, check if ready
            var cliService = _serviceProvider.GetRequiredService<EasyTierCliService>();
            var node = await cliService.NodeAsync(ct);
            if (node != null)
            {
                _logger.LogInformation("EasyTier is ready");
                _state = RoomStateKind.Host_ScaffoldingStarting;
                EmitStatus();
                return;
            }
        }

        // Start EasyTier with host configuration
        var env = _serviceProvider.GetRequiredService<IHostEnvironment>();
        var resourceDir = Path.Combine(env.ContentRootPath, "resource");
        var coreExe = Path.Combine(resourceDir, OperatingSystem.IsWindows() ? "easytier-core.exe" : "easytier-core");

        if (!File.Exists(coreExe))
        {
            _lastError = "EasyTier core not found";
            _state = RoomStateKind.Error;
            EmitStatus();
            return;
        }

        var hostname = ScaffoldingHelpers.GenerateCenterHostname(_runtime.ScaffoldingPort);
        var args = new List<string>
        {
            // Core options
            "--no-tun",
            "--multi-thread",
            "--latency-first",
            "--compression", "zstd",
            // Network
            "--network-name", _runtime.NetworkName,
            "--network-secret", _runtime.NetworkSecret,
            "--hostname", hostname,
            "--ipv4", "10.144.144.1",
            // Listeners
            "-l", "udp://0.0.0.0:0",
            "-l", "tcp://0.0.0.0:0",
            // Port whitelist
            "--tcp-whitelist", _runtime.ScaffoldingPort.ToString(),
            "--tcp-whitelist", "25565",
            "--udp-whitelist", "25565",
            // Public server
            "--peer", "udp://public-server.easytier.top:11010",
            "--p2p"
        };

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

        _runtime.EasyTierProcess = Process.Start(psi);

        if (_runtime.EasyTierProcess == null)
        {
            _lastError = "Failed to start EasyTier";
            _state = RoomStateKind.Error;
            EmitStatus();
            return;
        }

        // Log output
        _ = Task.Run(async () =>
        {
            try
            {
                while (await _runtime.EasyTierProcess.StandardOutput.ReadLineAsync(ct) is string line)
                    _logger.LogDebug("EasyTier: {Line}", line);
            }
            catch { }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                while (await _runtime.EasyTierProcess.StandardError.ReadLineAsync(ct) is string line)
                    _logger.LogWarning("EasyTier: {Line}", line);
            }
            catch { }
        });

        _logger.LogInformation("EasyTier started with PID {Pid}", _runtime.EasyTierProcess.Id);

        // Wait for readiness
        var startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < _easyTierStartupTimeout)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            var cliService = _serviceProvider.GetRequiredService<EasyTierCliService>();
            var node = await cliService.NodeAsync(ct);
            if (node != null)
            {
                _logger.LogInformation("EasyTier is ready");
                _state = RoomStateKind.Host_ScaffoldingStarting;
                EmitStatus();
                return;
            }
        }

        _lastError = "EasyTier startup timeout";
        _state = RoomStateKind.Error;
        EmitStatus();
    }

    private async Task StepHost_ScaffoldingStartingAsync(CancellationToken ct)
    {
        if (_runtime!.ScaffoldingServer != null)
        {
            _state = RoomStateKind.Host_MinecraftDetecting;
            EmitStatus();
            return;
        }

        _runtime.ScaffoldingServer = new ScaffoldingServer(
            _runtime.ScaffoldingPort,
            logger: _serviceProvider.GetRequiredService<ILogger<ScaffoldingServer>>());

        await _runtime.ScaffoldingServer.StartAsync(ct);

        // Set host profile
        _runtime.ScaffoldingServer.SetHostProfile(
            _runtime.PlayerName,
            _runtime.MachineId,
            _runtime.Vendor);

        _logger.LogInformation("Scaffolding server started on port {Port}", _runtime.ScaffoldingPort);
        _state = RoomStateKind.Host_MinecraftDetecting;
        EmitStatus();
    }

    private async Task StepHost_MinecraftDetectingAsync(CancellationToken ct)
    {
        // Check if MC server is available via Minecraft LAN listener
        var mcState = _serviceProvider.GetRequiredService<MinecraftLanState>();

        // Look for local MC servers
        var localServer = mcState.AllServers.FirstOrDefault(s => s.IsLocalHost);

        if (localServer != null)
        {
            _runtime!.MinecraftPort = (ushort)localServer.EndPoint.Port;
            _runtime.ScaffoldingServer!.SetMinecraftPort(_runtime.MinecraftPort);
            _logger.LogInformation("Minecraft server detected on port {Port}", _runtime.MinecraftPort);
        }
        else
        {
            // No MC server yet - that's ok, set null (c:server_port will return status=32)
            _runtime!.ScaffoldingServer!.SetMinecraftPort(null);
        }

        _state = RoomStateKind.Host_Running;
        EmitStatus();
    }

    private async Task StepHost_RunningAsync(CancellationToken ct)
    {
        // Monitor EasyTier process
        if (_runtime!.EasyTierProcess != null && _runtime.EasyTierProcess.HasExited)
        {
            _lastError = "EasyTier process exited";
            _state = RoomStateKind.Error;
            EmitStatus();
            return;
        }

        // Check for MC server changes
        var mcState = _serviceProvider.GetRequiredService<MinecraftLanState>();
        var localServer = mcState.AllServers.FirstOrDefault(s => s.IsLocalHost);
        var newPort = localServer != null ? (ushort?)localServer.EndPoint.Port : null;

        if (newPort != _runtime.MinecraftPort)
        {
            _runtime.MinecraftPort = newPort;
            _runtime.ScaffoldingServer!.SetMinecraftPort(_runtime.MinecraftPort);
            _logger.LogInformation("Minecraft port updated to {Port}", _runtime.MinecraftPort ?? 0);
            EmitStatus();
        }

        await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
}
