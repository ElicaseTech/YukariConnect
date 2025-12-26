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
            _logger.LogInformation("Fetching node info from EasyTier CLI...");
            var node = await cliService.NodeAsync(ct);
            if (node != null)
            {
                _logger.LogInformation("EasyTier is ready");

                // Get local node's virtual IP for logging
                string? localVirtualIp = null;
                if (node.RootElement.TryGetProperty("ipv4_addr", out var ipv4Prop))
                {
                    var ipv4Addr = ipv4Prop.GetString();
                    // ipv4_addr format is "10.144.144.1/24", extract just the IP
                    if (!string.IsNullOrEmpty(ipv4Addr))
                    {
                        var slashIndex = ipv4Addr.IndexOf('/');
                        localVirtualIp = slashIndex > 0 ? ipv4Addr[..slashIndex] : ipv4Addr;
                    }
                }

                // Log virtual network information for debugging
                _logger.LogInformation("=== Virtual Network Information ===");
                _logger.LogInformation("Network Name: {NetworkName}", _runtime.NetworkName);
                _logger.LogInformation("Network Secret: {NetworkSecret}", _runtime.NetworkSecret);
                _logger.LogInformation("Center Hostname: {Hostname}", ScaffoldingHelpers.GenerateCenterHostname(_runtime.ScaffoldingPort));
                _logger.LogInformation("Scaffolding Port: {Port}", _runtime.ScaffoldingPort);
                if (!string.IsNullOrEmpty(localVirtualIp))
                {
                    _logger.LogInformation("Virtual IP: {VirtualIp}", localVirtualIp);
                }
                _logger.LogInformation("Public Servers: tcp://public.easytier.top:11010, tcp://public2.easytier.cn:54321");

                // Log local node information
                var hasHostname = node.RootElement.TryGetProperty("hostname", out var hostnameProp);
                var hasId = node.RootElement.TryGetProperty("id", out var idProp);
                _logger.LogInformation("Local Node: Hostname={Hostname}, ID={ID}",
                    hasHostname ? hostnameProp.GetString() : "N/A",
                    hasId ? idProp.GetString() : "N/A");

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
            // Port whitelist - only Scaffolding port initially
            // MC port will be added dynamically after detection
            "--tcp-whitelist", _runtime.ScaffoldingPort.ToString(),
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

                // Get local node's virtual IP for logging
                string? localVirtualIp = null;
                if (node.RootElement.TryGetProperty("ipv4_addr", out var ipv4Prop))
                {
                    var ipv4Addr = ipv4Prop.GetString();
                    // ipv4_addr format is "10.144.144.1/24", extract just the IP
                    if (!string.IsNullOrEmpty(ipv4Addr))
                    {
                        var slashIndex = ipv4Addr.IndexOf('/');
                        localVirtualIp = slashIndex > 0 ? ipv4Addr[..slashIndex] : ipv4Addr;
                    }
                }

                // Log virtual network information for debugging
                _logger.LogInformation("=== Virtual Network Information ===");
                _logger.LogInformation("Network Name: {NetworkName}", _runtime.NetworkName);
                _logger.LogInformation("Network Secret: {NetworkSecret}", _runtime.NetworkSecret);
                _logger.LogInformation("Center Hostname: {Hostname}", ScaffoldingHelpers.GenerateCenterHostname(_runtime.ScaffoldingPort));
                _logger.LogInformation("Scaffolding Port: {Port}", _runtime.ScaffoldingPort);
                if (!string.IsNullOrEmpty(localVirtualIp))
                {
                    _logger.LogInformation("Virtual IP: {VirtualIp}", localVirtualIp);
                }
                _logger.LogInformation("Public Servers: tcp://public.easytier.top:11010, tcp://public2.easytier.cn:54321");

                // Log local node information
                var hasHostname = node.RootElement.TryGetProperty("hostname", out var hostnameProp);
                var hasId = node.RootElement.TryGetProperty("id", out var idProp);
                _logger.LogInformation("Local Node: Hostname={Hostname}, ID={ID}",
                    hasHostname ? hostnameProp.GetString() : "N/A",
                    hasId ? idProp.GetString() : "N/A");

                _logger.LogInformation("====================================");

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

            // Update EasyTier whitelist to allow the MC port
            var cliService = _serviceProvider.GetRequiredService<EasyTierCliService>();
            var mcPortStr = _runtime.MinecraftPort.ToString()!;
            var tcpWhitelist = new string[] { _runtime.ScaffoldingPort.ToString()!, mcPortStr };
            var udpWhitelist = new string[] { mcPortStr };

            var tcpWhitelistLog = string.Join(",", tcpWhitelist);
            var udpWhitelistLog = string.Join(",", udpWhitelist);
            _logger.LogInformation("Setting EasyTier whitelist - TCP: {Tcp}, UDP: {Udp}",
                tcpWhitelistLog, udpWhitelistLog);

            await cliService.SetTcpWhitelistAsync(tcpWhitelist, ct);
            await cliService.SetUdpWhitelistAsync(udpWhitelist, ct);
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

    private int _mcFailureCount = 0;
    private const int MC_MAX_FAILURES = 3;

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

        // MC server TCP connection check (like Terracotta does)
        var mcPort = _runtime.MinecraftPort;
        if (mcPort.HasValue)
        {
            var isMcAlive = await CheckMcServerConnectionAsync(mcPort.Value, ct);
            if (isMcAlive)
            {
                if (_mcFailureCount > 0)
                {
                    _logger.LogInformation("MC server connection restored");
                }
                _mcFailureCount = 0;
            }
            else
            {
                _mcFailureCount++;
                _logger.LogWarning("MC server check failed ({Count}/{Max})",
                    _mcFailureCount, MC_MAX_FAILURES);

                if (_mcFailureCount >= MC_MAX_FAILURES)
                {
                    _lastError = "Minecraft server connection lost";
                    _logger.LogError("Minecraft server connection lost after {Count} failures",
                        _mcFailureCount);
                    _state = RoomStateKind.Error;
                    EmitStatus();
                    return;
                }
            }
        }

        // Check for MC server changes (LAN broadcast discovery)
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

                // Update EasyTier whitelist to allow the new MC port
                var cliService = _serviceProvider.GetRequiredService<EasyTierCliService>();
                var mcPortStr = newPort.Value.ToString()!;
                var tcpWhitelist = new string[] { _runtime.ScaffoldingPort.ToString()!, mcPortStr };
                var udpWhitelist = new string[] { mcPortStr };

                var tcpWhitelistLog = string.Join(",", tcpWhitelist);
                var udpWhitelistLog = string.Join(",", udpWhitelist);
                _logger.LogInformation("Updating EasyTier whitelist - TCP: {Tcp}, UDP: {Udp}",
                    tcpWhitelistLog, udpWhitelistLog);

                await cliService.SetTcpWhitelistAsync(tcpWhitelist, ct);
                await cliService.SetUdpWhitelistAsync(udpWhitelist, ct);

                // Reset failure count when port changes
                _mcFailureCount = 0;
            }
            else
            {
                _logger.LogWarning("Minecraft server disappeared - c:server_port will return status=32");

                // Remove MC port from EasyTier whitelist (keep only Scaffolding port)
                var cliService = _serviceProvider.GetRequiredService<EasyTierCliService>();
                var tcpWhitelist = new string[] { _runtime.ScaffoldingPort.ToString()! };

                _logger.LogInformation("Removing MC port from whitelist - TCP: {Tcp}, UDP: (empty)",
                    string.Join(",", tcpWhitelist));

                await cliService.SetTcpWhitelistAsync(tcpWhitelist, ct);
                await cliService.SetUdpWhitelistAsync([], ct);
            }

            EmitStatus();
        }

        await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }

    /// <summary>
    /// Check MC server connection using TCP handshake (like Terracotta's check_mc_conn).
    /// Sends MC handshake packet (0xFE) and expects 0xFF response.
    /// </summary>
    private static async Task<bool> CheckMcServerConnectionAsync(ushort port, CancellationToken ct)
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp);

            // Connect with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            await socket.ConnectAsync("127.0.0.1", port, cts.Token);

            // Send MC legacy handshake packet (0xFE)
            var handshake = new byte[] { 0xFE };
            await socket.SendAsync(handshake, System.Net.Sockets.SocketFlags.None, cts.Token);

            // Receive response
            var response = new byte[1];
            var received = await socket.ReceiveAsync(response, System.Net.Sockets.SocketFlags.None, cts.Token);

            // MC server should respond with 0xFF
            return received == 1 && response[0] == 0xFF;
        }
        catch
        {
            return false;
        }
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
