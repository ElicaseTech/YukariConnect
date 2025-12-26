using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YukariConnect.Scaffolding.Models;
using YukariConnect.Services;
using YukariConnect.Minecraft.Services;

namespace YukariConnect.Scaffolding;

// ======== BUILD STAMP: 2025-12-27-15:50 ========

/// <summary>
/// Host state machine steps implementation.
/// </summary>
public sealed partial class RoomController
{
    private async Task StepHostAsync(CancellationToken ct)
    {
        //Console.WriteLine("=== BUILD STAMP: 2025-12-27-15:50 - StepHostAsync called, state={0} ===", _state);
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

        _state = RoomStateKind.Host_ScaffoldingStarting;
        EmitStatus();
    }

    private async Task StepHost_ScaffoldingStartingAsync(CancellationToken ct)
    {
        if (_runtime!.ScaffoldingServer != null)
        {
            _state = RoomStateKind.Host_EasyTierStarting;
            EmitStatus();
            return;
        }

        _runtime.ScaffoldingServer = new ScaffoldingServer(
            _runtime.ScaffoldingPort,
            logger: _serviceProvider.GetRequiredService<ILogger<ScaffoldingServer>>());

        // Start scaffolding server and get actual port
        var actualPort = await _runtime.ScaffoldingServer.StartAsync(ct);

        // Update runtime with actual port (may differ if 13448 was occupied)
        _runtime.ScaffoldingPort = actualPort;

        // Set host profile
        _runtime.ScaffoldingServer.SetHostProfile(
            _runtime.PlayerName,
            _runtime.MachineId,
            _runtime.Vendor);

        _logger.LogInformation("Scaffolding server started on port {Port}", actualPort);
        _state = RoomStateKind.Host_EasyTierStarting;
        EmitStatus();
    }

    private async Task StepHost_EasyTierStartingAsync(CancellationToken ct)
    {
        if (_runtime!.EasyTierProcess != null)
        {
            // Already started, check if ready
            var cliService = _serviceProvider.GetRequiredService<EasyTierCliService>();
            Console.WriteLine("[1] Fetching node info from EasyTier CLI...");
            _logger.LogInformation("Fetching node info from EasyTier CLI...");
            var node = await cliService.NodeAsync(ct);
            Console.WriteLine("[2] NodeAsync returned, node={0}", node != null ? "not null" : "null");
            if (node != null)
            {
                Console.WriteLine("[3] Inside if (node != null) block");
                _logger.LogInformation("EasyTier is ready");
                Console.WriteLine("[4] About to call PeersAsync...");
                _logger.LogInformation("About to call PeersAsync...");

                // Wait for connection to public server (at least one peer connected)
                Console.WriteLine("[5] Before PeersAsync call");
                var peers = await cliService.PeersAsync(ct);
                Console.WriteLine("[6] After PeersAsync call, peers={0}", peers != null ? "not null" : "null");
                _logger.LogInformation("Peers result: {PeersResult}", peers != null ? "got data" : "null");
                _logger.LogInformation("Waiting for P2P network connection...");
                int retryCount = 0;
                const int maxRetries = 30; // 30 seconds total timeout

                while (peers == null || peers.RootElement.GetArrayLength() == 0)
                {
                    if (retryCount >= maxRetries)
                    {
                        _logger.LogWarning("No peers connected after {Seconds}s, continuing anyway...", maxRetries);
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    peers = await cliService.PeersAsync(ct);
                    retryCount++;

                    if (retryCount % 5 == 0)
                    {
                        _logger.LogInformation("Still waiting for peers... ({Count}s)", retryCount);
                    }
                }

                if (peers != null && peers.RootElement.GetArrayLength() > 0)
                {
                    _logger.LogInformation("Connected to {Count} peer(s)", peers.RootElement.GetArrayLength());
                }
                else
                {
                    _logger.LogInformation("No peers detected (Host only)");
                }

                // Add port forwarding for Scaffolding server
                // This allows virtual network peers to access the Scaffolding server
                _logger.LogInformation("Setting up port forwarding for Scaffolding server...");
                var localAddr = $"0.0.0.0:{_runtime.ScaffoldingPort}";
                var virtualAddr = $"10.144.144.1:{_runtime.ScaffoldingPort}";
                var forwardOk = await cliService.AddPortForwardAsync("tcp", localAddr, virtualAddr, ct);

                if (!forwardOk)
                {
                    _logger.LogWarning("Failed to add port forwarding, Scaffolding server may not be accessible from virtual network");
                }

                // Log virtual network information for debugging
                _logger.LogInformation("=== Virtual Network Information ===");
                _logger.LogInformation("Network Name: {NetworkName}", _runtime.NetworkName);
                _logger.LogInformation("Network Secret: {NetworkSecret}", _runtime.NetworkSecret);
                _logger.LogInformation("Center Hostname: {Hostname}", ScaffoldingHelpers.GenerateCenterHostname(_runtime.ScaffoldingPort));
                _logger.LogInformation("Scaffolding Port: {Port}", _runtime.ScaffoldingPort);
                _logger.LogInformation("Virtual IP: 10.144.144.1");
                _logger.LogInformation("Public Servers: tcp://public.easytier.top:11010, tcp://public2.easytier.cn:54321");

                // Log local node information
                if (node != null)
                {
                    var hasHostname = node.RootElement.TryGetProperty("hostname", out var hostnameProp);
                    var hasIpv4 = node.RootElement.TryGetProperty("ipv4", out var ipv4Prop);
                    var hasId = node.RootElement.TryGetProperty("id", out var idProp);
                    _logger.LogInformation("Local Node: Hostname={Hostname}, IP={IP}, ID={ID}",
                        hasHostname ? hostnameProp.GetString() : "N/A",
                        hasIpv4 ? ipv4Prop.GetString() : "N/A",
                        hasId ? idProp.GetString() : "N/A");
                }

                // Log connected peers
                if (peers != null && peers.RootElement.GetArrayLength() > 0)
                {
                    _logger.LogInformation("Connected Peers ({Count}):", peers.RootElement.GetArrayLength());
                    foreach (var peer in peers.RootElement.EnumerateArray())
                    {
                        var hasHostname = peer.TryGetProperty("hostname", out var peerHostname);
                        var hasIp = peer.TryGetProperty("ipv4", out var peerIp);
                        var hasId = peer.TryGetProperty("id", out var peerId);
                        _logger.LogInformation("  - Hostname={Hostname}, IP={IP}, ID={ID}",
                            hasHostname ? peerHostname.GetString() : "N/A",
                            hasIp ? peerIp.GetString() : "N/A",
                            hasId ? peerId.GetString() : "N/A");
                    }
                }
                else
                {
                    _logger.LogWarning("No peers connected yet - this may cause connection issues");
                }

                _logger.LogInformation("====================================");

                _state = RoomStateKind.Host_MinecraftDetecting;
                EmitStatus();
                return;
            }
        }

        // Start EasyTier with host configuration
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

        var hostname = ScaffoldingHelpers.GenerateCenterHostname(_runtime.ScaffoldingPort);

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
            "--multi-thread",
            "--latency-first",
            "--compression", "zstd",
            "--enable-kcp-proxy",
            // Network
            "--network-name", _runtime.NetworkName,
            "--network-secret", _runtime.NetworkSecret,
            "--hostname", hostname,
            "--ipv4", "10.144.144.1",
            // Listeners - bind to specific interface to avoid Mihomo proxy
            "-l", "udp://0.0.0.0:0",
            "-l", "tcp://0.0.0.0:0",
            // Port whitelist
            "--tcp-whitelist", _runtime.ScaffoldingPort.ToString(),
            "--tcp-whitelist", "25565",
            "--udp-whitelist", "25565",
            // Public servers (use -p like Terracotta)
            "-p", tcpPublicServers[0],
            "-p", tcpPublicServers[1]
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

        // Log the actual command line for debugging
        _logger.LogInformation("EasyTier command line: {Exe} {Args}",
            coreExe,
            string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));

        // Log output
        _ = Task.Run(async () =>
        {
            try
            {
                while (await _runtime.EasyTierProcess.StandardOutput.ReadLineAsync(ct) is string line)
                    _logger.LogInformation("[EasyTier-stdout] {Line}", line);
            }
            catch { }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                while (await _runtime.EasyTierProcess.StandardError.ReadLineAsync(ct) is string line)
                    _logger.LogWarning("[EasyTier-stderr] {Line}", line);
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
                _state = RoomStateKind.Host_MinecraftDetecting;
                EmitStatus();
                return;
            }
        }

        _lastError = "EasyTier startup timeout";
        _state = RoomStateKind.Error;
        EmitStatus();
    }

    private async Task StepHost_MinecraftDetectingAsync(CancellationToken ct)
    {
        // Log all local IPs for debugging
        var localIps = GetAllLocalIPv4();
        _logger.LogInformation("Local machine IPs: {IPs}", string.Join(", ", localIps));

        // Check if MC server is available via Minecraft LAN listener
        var mcState = _serviceProvider.GetRequiredService<MinecraftLanState>();

        _logger.LogInformation("Scanning for MC servers... Found {Count} total servers",
            mcState.TotalCount);

        // Log all discovered servers for debugging
        foreach (var server in mcState.AllServers)
        {
            _logger.LogInformation("  - Server: {EndPoint}, MOTD='{Motd}', IsLocalHost={IsLocalHost}, IsLocalNetwork={IsLocalNetwork}, IsVerified={IsVerified}",
                server.EndPoint, server.Motd, server.IsLocalHost, server.IsLocalNetwork, server.IsVerified);
        }

        // Look for local MC servers (from LAN, not virtual network)
        var localServer = mcState.AllServers.FirstOrDefault(s => s.IsLocalNetwork);

        if (localServer != null)
        {
            _runtime!.MinecraftPort = (ushort)localServer.EndPoint.Port;
            _runtime.ScaffoldingServer!.SetMinecraftPort(_runtime.MinecraftPort);
            _logger.LogInformation("Minecraft server detected on port {Port} from {EndPoint}",
                _runtime.MinecraftPort, localServer.EndPoint);
        }
        else
        {
            // No MC server yet - that's ok, set null (c:server_port will return status=32)
            _runtime!.ScaffoldingServer!.SetMinecraftPort(null);
            _logger.LogWarning("No local MC server detected - c:server_port will return status=32");
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
        var localServer = mcState.AllServers.FirstOrDefault(s => s.IsLocalNetwork);
        var newPort = localServer != null ? (ushort?)localServer.EndPoint.Port : null;

        if (newPort != _runtime.MinecraftPort)
        {
            _runtime.MinecraftPort = newPort;
            _runtime.ScaffoldingServer!.SetMinecraftPort(_runtime.MinecraftPort);

            if (newPort.HasValue)
            {
                _logger.LogInformation("Minecraft port updated to {Port} from {EndPoint}",
                    _runtime.MinecraftPort.Value, localServer!.EndPoint);
            }
            else
            {
                _logger.LogWarning("Minecraft server disappeared - c:server_port will return status=32");
            }

            // Restart FakeServer with new port
            if (_runtime.FakeServer != null)
            {
                try { await _runtime.FakeServer.DisposeAsync(); } catch { }
                _runtime.FakeServer = null;
            }

            if (_runtime.MinecraftPort.HasValue)
            {
                var motd = $"{_runtime.PlayerName}'s World";
                _runtime.FakeServer = new MinecraftFakeServer(
                    _runtime.MinecraftPort.Value,
                    motd,
                    logger: _serviceProvider.GetRequiredService<ILogger<MinecraftFakeServer>>());
                await _runtime.FakeServer.StartAsync(ct);
                _logger.LogInformation("FakeServer broadcasting on port {Port}", _runtime.MinecraftPort);
            }

            EmitStatus();
        }

        await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }

    /// <summary>
    /// Get all local IPv4 addresses for debugging.
    /// </summary>
    private static List<IPAddress> GetAllLocalIPv4()
    {
        var result = new List<IPAddress>();
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;

            var nicType = nic.NetworkInterfaceType;
            if (nicType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

            var ipProps = nic.GetIPProperties();
            foreach (var ua in ipProps.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                if (System.Net.IPAddress.IsLoopback(ua.Address)) continue;

                // Skip link-local (169.254.x.x) and virtual network (10.144.x.x)
                var bytes = ua.Address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254) continue;
                if (bytes[0] == 10 && bytes[1] == 144) continue;

                result.Add(ua.Address);
            }
        }
        return result;
    }
}
