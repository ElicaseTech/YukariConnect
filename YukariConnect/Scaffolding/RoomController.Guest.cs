using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YukariConnect.Scaffolding.Models;
using YukariConnect.Services;
using YukariConnect.Minecraft.Services;

namespace YukariConnect.Scaffolding;

/// <summary>
/// Guest state machine steps implementation.
/// </summary>
public sealed partial class RoomController
{
    private async Task StepGuestAsync(CancellationToken ct)
    {
        if (_state == RoomStateKind.Guest_Prepare)
            await StepGuest_PrepareAsync(ct);
        else if (_state == RoomStateKind.Guest_EasyTierStarting)
            await StepGuest_EasyTierStartingAsync(ct);
        else if (_state == RoomStateKind.Guest_DiscoveringCenter)
            await StepGuest_DiscoveringCenterAsync(ct);
        else if (_state == RoomStateKind.Guest_ConnectingScaffolding)
            await StepGuest_ConnectingScaffoldingAsync(ct);
        else if (_state == RoomStateKind.Guest_Running)
            await StepGuest_RunningAsync(ct);
    }

    private async Task StepGuest_PrepareAsync(CancellationToken ct)
    {
        _logger.LogInformation("Guest prepare: room={Room}", _runtime!.RoomCode);

        _state = RoomStateKind.Guest_EasyTierStarting;
        EmitStatus();
    }

    private async Task StepGuest_EasyTierStartingAsync(CancellationToken ct)
    {
        if (_runtime!.EasyTierProcess != null)
        {
            var cliService = _serviceProvider.GetRequiredService<EasyTierCliService>();
            var node = await cliService.NodeAsync(ct);
            if (node != null)
            {
                _logger.LogInformation("EasyTier is ready");
                _state = RoomStateKind.Guest_DiscoveringCenter;
                EmitStatus();
                return;
            }
        }

        var env = _serviceProvider.GetRequiredService<IHostEnvironment>();
        var publicServers = _serviceProvider.GetRequiredService<PublicServersService>();
        var resourceDir = Path.Combine(env.ContentRootPath, "resource");
        var coreExe = Path.Combine(resourceDir, OperatingSystem.IsWindows() ? "easytier-core.exe" : "easytier-core");

        if (!File.Exists(coreExe))
        {
            _lastError = "EasyTier core not found";
            _state = RoomStateKind.Error;
            EmitStatus();
            return;
        }

        // Use TCP public servers for Terracotta compatibility
        // Terracotta uses these specific TCP servers
        var tcpPublicServers = new[]
        {
            "tcp://public.easytier.top:11010",
            "tcp://public2.easytier.cn:54321"
        };

        var args = new List<string>
        {
            // Core options
            "--no-tun",
            "--dhcp",
            "--multi-thread",
            "--latency-first",
            "--compression", "zstd",
            "--enable-kcp-proxy",
            // Network
            "--network-name", _runtime.NetworkName,
            "--network-secret", _runtime.NetworkSecret,
            // Listeners
            "-l", "udp://0.0.0.0:0",
            "-l", "tcp://0.0.0.0:0",
            // Allow all ports
            "--tcp-whitelist", "0",
            "--udp-whitelist", "0",
            // Public servers (TCP for Terracotta compatibility)
            "--peers", tcpPublicServers[0],
            "--peers", tcpPublicServers[1]
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
                _state = RoomStateKind.Guest_DiscoveringCenter;
                EmitStatus();
                return;
            }
        }

        _lastError = "EasyTier startup timeout";
        _state = RoomStateKind.Error;
        EmitStatus();
    }

    private DateTime _centerDiscoveryStart;

    private async Task StepGuest_DiscoveringCenterAsync(CancellationToken ct)
    {
        if (_centerDiscoveryStart == default)
            _centerDiscoveryStart = DateTime.UtcNow;

        if (DateTime.UtcNow - _centerDiscoveryStart > _centerDiscoveryTimeout)
        {
            _lastError = "Center discovery timeout";
            _state = RoomStateKind.Error;
            EmitStatus();
            return;
        }

        var cliService = _serviceProvider.GetRequiredService<EasyTierCliService>();
        var peersDoc = await cliService.PeersAsync(ct);

        if (peersDoc == null)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            return;
        }

        var centers = new List<CenterInfo>();

        foreach (var peer in peersDoc.RootElement.EnumerateArray())
        {
            var hostname = peer.GetProperty("hostname").GetString();
            var ipv4 = peer.GetProperty("ipv4").GetString();

            if (string.IsNullOrEmpty(hostname) || string.IsNullOrEmpty(ipv4))
                continue;

            if (!IPAddress.TryParse(ipv4, out var ip))
                continue;

            // Check if this is a center
            if (ScaffoldingHelpers.TryParseCenter(hostname, out var port))
            {
                centers.Add(new CenterInfo
                {
                    Ip = ip,
                    Port = port,
                    Hostname = hostname
                });
            }
        }

        if (centers.Count == 0)
        {
            // Continue waiting
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            return;
        }

        if (centers.Count > 1)
        {
            _lastError = $"Multiple centers found: {string.Join(", ", centers.Select(c => c.Hostname))}";
            _state = RoomStateKind.Error;
            EmitStatus();
            return;
        }

        // Found exactly one center
        var center = centers[0];
        _runtime!.CenterIp = center.Ip;
        _runtime.CenterScaffoldingPort = center.Port;
        _logger.LogInformation("Center found: {Host} at {Ip}:{Port}", center.Hostname, center.Ip, center.Port);

        _centerDiscoveryStart = default;
        _state = RoomStateKind.Guest_ConnectingScaffolding;
        EmitStatus();
    }

    private async Task StepGuest_ConnectingScaffoldingAsync(CancellationToken ct)
    {
        if (_runtime!.ScaffoldingClient != null)
        {
            // Already connected, move to running
            _state = RoomStateKind.Guest_Running;
            EmitStatus();
            return;
        }

        var client = new ScaffoldingClient(
            _runtime.CenterIp!.ToString(),
            _runtime.CenterScaffoldingPort!.Value,
            logger: _serviceProvider.GetRequiredService<ILogger<ScaffoldingClient>>());

        try
        {
            await client.ConnectAsync(ct);
            _logger.LogInformation("Connected to scaffolding server");

            // Verify fingerprint
            if (!await client.PingAsync(ct))
            {
                _lastError = "Fingerprint verification failed";
                await client.DisposeAsync();
                _state = RoomStateKind.Error;
                EmitStatus();
                return;
            }

            _runtime.ScaffoldingClient = client;

            // Get protocols
            var protocols = await client.GetProtocolsAsync(ct);
            _logger.LogInformation("Supported protocols: {Protocols}", string.Join(", ", protocols));

            // Register player
            await client.PlayerPingAsync(_runtime.PlayerName, _runtime.MachineId, _runtime.Vendor, ct);

            _logger.LogInformation("Guest connected successfully");
            _state = RoomStateKind.Guest_Running;
            EmitStatus();
        }
        catch (Exception ex)
        {
            _lastError = $"Connection failed: {ex.Message}";
            await client.DisposeAsync();
            _state = RoomStateKind.Error;
            EmitStatus();
        }
    }

    private DateTime _lastHeartbeat = DateTime.MinValue;
    private DateTime _lastPlayerListCheck = DateTime.MinValue;

    private async Task StepGuest_RunningAsync(CancellationToken ct)
    {
        // Monitor EasyTier process
        if (_runtime!.EasyTierProcess != null && _runtime.EasyTierProcess.HasExited)
        {
            _lastError = "EasyTier process exited";
            _state = RoomStateKind.Error;
            EmitStatus();
            return;
        }

        var now = DateTime.UtcNow;

        // Heartbeat every 5 seconds
        if (now - _lastHeartbeat > TimeSpan.FromSeconds(5))
        {
            try
            {
                await _runtime.ScaffoldingClient!.PlayerPingAsync(
                    _runtime.PlayerName, _runtime.MachineId, _runtime.Vendor, ct);
                _lastHeartbeat = now;
                _logger.LogDebug("Heartbeat sent");
            }
            catch (Exception ex)
            {
                _lastError = $"Heartbeat failed: {ex.Message}";
                _state = RoomStateKind.Error;
                EmitStatus();
                return;
            }
        }

        // Get MC port (may wait for host to start)
        if (_runtime.MinecraftPort == null)
        {
            try
            {
                var port = await _runtime.ScaffoldingClient.GetServerPortAsync(ct);
                if (port != null)
                {
                    _runtime.MinecraftPort = port;
                    _logger.LogInformation("Minecraft server port: {Port}", port);
                    EmitStatus();
                }
            }
            catch { }
        }

        await Task.Delay(_tick, ct);
    }
}
