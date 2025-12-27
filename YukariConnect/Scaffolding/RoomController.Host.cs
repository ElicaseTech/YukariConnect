using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YukariConnect.Scaffolding.Models;
using YukariConnect.Services;
using YukariConnect.Minecraft.Services;
using YukariConnect.Network;

namespace YukariConnect.Scaffolding;

// ======== BUILD STAMP: 2025-12-27-15:50 ========

/// <summary>
/// Host state machine steps implementation.
/// </summary>
public sealed partial class RoomController
{
    private async Task StepHostAsync(CancellationToken ct)
    {
        // Handle terminal states
        if (_state == RoomStateKind.Idle ||
            _state == RoomStateKind.Stopping)
        {
            return;
        }

        // Clean up resources in Error state
        if (_state == RoomStateKind.Error)
        {
            if (_runtime?.ScaffoldingServer != null)
            {
                try
                {
                    await _runtime.ScaffoldingServer.StopAsync();
                    _runtime.ScaffoldingServer = null;
                    _logger.LogInformation("ScaffoldingServer stopped in Error state");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to stop ScaffoldingServer in Error state");
                }
            }
            return;
        }

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
            _logger.LogInformation("Fetching node info from network...");
            var node = await _networkNode.GetNodeInfoAsync(ct);
            if (node != null)
            {
                _logger.LogInformation("Network layer is ready");

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
                var servers = _peerDiscovery.GetPublicServers();
                _logger.LogInformation("=== Virtual Network Information ===");
                _logger.LogInformation("Network Name: {NetworkName}", _runtime.NetworkName);
                _logger.LogInformation("Network Secret: {NetworkSecret}", _runtime.NetworkSecret);
                _logger.LogInformation("Center Hostname: {Hostname}", ScaffoldingHelpers.GenerateCenterHostname(_runtime.ScaffoldingPort));
                _logger.LogInformation("Scaffolding Port: {Port}", _runtime.ScaffoldingPort);
                if (!string.IsNullOrEmpty(localVirtualIp))
                {
                    _logger.LogInformation("Virtual IP: {VirtualIp}", localVirtualIp);
                }
                _logger.LogInformation("Public Servers: {Servers}", string.Join(", ", servers));

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

        // Start network process with host configuration
        var hostname = ScaffoldingHelpers.GenerateCenterHostname(_runtime.ScaffoldingPort);

        // Validate public servers before using them
        _logger.LogInformation("Validating public servers...");
        var publicServers = await _peerDiscovery.GetValidatedPublicServersAsync(ct);
        if (publicServers.Length == 0)
        {
            _lastError = "No valid public servers available";
            _state = RoomStateKind.Error;
            EmitStatus();
            return;
        }

        var config = new NetworkProcessConfig
        {
            NetworkName = _runtime.NetworkName,
            NetworkSecret = _runtime.NetworkSecret,
            Hostname = hostname,
            Ipv4 = "10.144.144.1",
            IsHost = true,
            ScaffoldingPort = _runtime.ScaffoldingPort,
            PublicServers = publicServers
        };

        _logger.LogInformation("Starting network process...");
        _runtime.EasyTierProcess = await _networkProcess.StartAsync(config, ct);

        if (_runtime.EasyTierProcess == null)
        {
            _lastError = "Failed to start network process";
            _state = RoomStateKind.Error;
            EmitStatus();
            return;
        }

        _logger.LogInformation("Network process started with PID {Pid}", _runtime.EasyTierProcess.Id);

        // Wait for readiness
        var startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < _easyTierStartupTimeout)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            var node = await _networkNode.GetNodeInfoAsync(ct);
            if (node != null)
            {
                _logger.LogInformation("Network layer is ready");

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
                _logger.LogInformation("Public Servers: {Servers}", string.Join(", ", publicServers));

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

        _lastError = "Network startup timeout";
        _state = RoomStateKind.Error;
        EmitStatus();
    }

    private DateTime _mcDetectionStartTime;
    private int _mcServerOfflineCount = 0;

    private async Task StepHost_MinecraftDetectingAsync(CancellationToken ct)
    {
        // Track when we started detecting (first time only)
        if (_mcDetectionStartTime == default)
            _mcDetectionStartTime = DateTime.UtcNow;

        // Check if MC server is available via Minecraft LAN listener
        var mcState = _serviceProvider.GetRequiredService<MinecraftLanState>();

        // Look for local MC servers (from LAN, not virtual network)
        var localServer = mcState.AllServers.FirstOrDefault(s => s.IsLocalNetwork);

        if (localServer != null)
        {
            _runtime!.MinecraftPort = (ushort)localServer.EndPoint.Port;
            _runtime.ScaffoldingServer!.SetMinecraftPort(_runtime.MinecraftPort);
            _logger.LogInformation("Minecraft server detected on port {Port} from {EndPoint}",
                _runtime.MinecraftPort, localServer.EndPoint);

            // Update network whitelist to allow the MC port
            var mcPortStr = _runtime.MinecraftPort.ToString()!;
            var tcpWhitelist = new string[] { _runtime.ScaffoldingPort.ToString()!, mcPortStr };
            var udpWhitelist = new string[] { mcPortStr };

            var tcpWhitelistLog = string.Join(",", tcpWhitelist);
            var udpWhitelistLog = string.Join(",", udpWhitelist);
            _logger.LogInformation("Setting network whitelist - TCP: {Tcp}, UDP: {Udp}",
                tcpWhitelistLog, udpWhitelistLog);

            await _networkNode.SetTcpWhitelistAsync(tcpWhitelist, ct);
            await _networkNode.SetUdpWhitelistAsync(udpWhitelist, ct);

            // Reset detection timer and move to running state
            _mcDetectionStartTime = default;
            _state = RoomStateKind.Host_Running;
            EmitStatus();
            _logger.LogInformation("Minecraft server found, transitioning to Host_Running state");
        }
        else
        {
            // Set null port (c:server_port will return status=32)
            _runtime!.ScaffoldingServer!.SetMinecraftPort(null);

            if (_terracottaCompatibilityMode)
            {
                // Terracotta compatible mode: Wait for MC server before starting
                // Log periodically (every 30 seconds) to avoid spam
                if (DateTime.UtcNow - _mcDetectionStartTime > TimeSpan.FromSeconds(30))
                {
                    _logger.LogInformation("Still scanning for Minecraft server... (total servers found: {Count})",
                        mcState.TotalCount);
                    _mcDetectionStartTime = DateTime.UtcNow;
                }

                // Stay in detecting state - don't transition to running yet
                // This allows continuous scanning until a server is found
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            else
            {
                // Yukari mode: Start immediately and dynamically update MC port later
                _logger.LogInformation("Yukari mode: No MC server detected yet, but starting anyway (will update port dynamically when server appears)");

                // Set initial network whitelist (Scaffolding only)
                var tcpWhitelist = new string[] { _runtime.ScaffoldingPort.ToString()! };
                await _networkNode.SetTcpWhitelistAsync(tcpWhitelist, ct);
                await _networkNode.SetUdpWhitelistAsync([], ct);

                _mcDetectionStartTime = default;
                _state = RoomStateKind.Host_Running;
                EmitStatus();
                _logger.LogInformation("Yukari mode: Transitioning to Host_Running state without MC server (will detect dynamically)");
            }
        }
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

        // Check for MC server changes (LAN broadcast discovery)
        var mcState = _serviceProvider.GetRequiredService<MinecraftLanState>();
        var allServers = mcState.AllServers.ToList();
        var localServer = allServers.FirstOrDefault(s => s.IsLocalNetwork);
        var newPort = localServer != null ? (ushort?)localServer.EndPoint.Port : null;

        // Check if local server is verified (responsive)
        var isLocalServerVerified = localServer?.IsVerified ?? false;

        _logger.LogInformation("Host_Running loop: newPort={NewPort}, currentPort={CurrentPort}, terracottaMode={TerracottaMode}, totalServers={TotalCount}, isVerified={IsVerified}",
            newPort.HasValue ? newPort.Value.ToString() : "null",
            _runtime.MinecraftPort.HasValue ? _runtime.MinecraftPort.Value.ToString() : "null",
            _terracottaCompatibilityMode,
            allServers.Count,
            isLocalServerVerified);

        if (newPort != _runtime.MinecraftPort)
        {
            _runtime.MinecraftPort = newPort;
            _runtime.ScaffoldingServer!.SetMinecraftPort(_runtime.MinecraftPort);

            if (newPort.HasValue)
            {
                _logger.LogInformation("Minecraft server detected on port {Port} from {EndPoint}",
                    _runtime.MinecraftPort.Value, localServer!.EndPoint);

                // Update network whitelist to allow the new MC port
                var mcPortStr = newPort.Value.ToString()!;
                var tcpWhitelist = new string[] { _runtime.ScaffoldingPort.ToString()!, mcPortStr };
                var udpWhitelist = new string[] { mcPortStr };

                var tcpWhitelistLog = string.Join(",", tcpWhitelist);
                var udpWhitelistLog = string.Join(",", udpWhitelist);
                _logger.LogInformation("Updating network whitelist - TCP: {Tcp}, UDP: {Udp}",
                    tcpWhitelistLog, udpWhitelistLog);

                await _networkNode.SetTcpWhitelistAsync(tcpWhitelist, ct);
                await _networkNode.SetUdpWhitelistAsync(udpWhitelist, ct);

                EmitStatus();
            }
            else
            {
                _logger.LogInformation("Minecraft server not available - c:server_port will return status=32");

                // Remove MC port from network whitelist (keep only Scaffolding port)
                var tcpWhitelist = new string[] { _runtime.ScaffoldingPort.ToString()! };

                _logger.LogInformation("Removing MC port from whitelist - TCP: {Tcp}, UDP: (empty)",
                    string.Join(",", tcpWhitelist));

                _logger.LogInformation("Calling SetTcpWhitelistAsync with {Count} ports", tcpWhitelist.Length);
                await _networkNode.SetTcpWhitelistAsync(tcpWhitelist, ct);
                _logger.LogInformation("Calling SetUdpWhitelistAsync with empty ports");
                await _networkNode.SetUdpWhitelistAsync([], ct);
                _logger.LogInformation("Whitelist update completed");

                EmitStatus();
            }
        }

        _logger.LogInformation("About to check offline status, isVerified={IsVerified}, terracottaMode={TerracottaMode}",
            isLocalServerVerified, _terracottaCompatibilityMode);

        // Track MC server offline status in Terracotta compatibility mode
        // Check every cycle, not just when port changes
        // In Terracotta mode, we only enter Host_Running after finding an MC server,
        // so we always need to check if it goes offline
        if (_terracottaCompatibilityMode)
        {
            if (isLocalServerVerified)
            {
                // MC server is verified (responsive), reset offline counter
                _mcServerOfflineCount = 0;
            }
            else
            {
                // MC server was online before but is now:
                // - NOT verified (offline but still in list)
                // - OR completely gone from list
                _mcServerOfflineCount++;
                _logger.LogWarning("Minecraft server offline (count: {Count}/{Threshold})",
                    _mcServerOfflineCount, _options.McServerOfflineThreshold);

                if (_mcServerOfflineCount >= _options.McServerOfflineThreshold)
                {
                    _lastError = $"Minecraft server offline for {_mcServerOfflineCount} consecutive checks";
                    _logger.LogError("Minecraft server offline threshold reached, transitioning to Error state");
                    _state = RoomStateKind.Error;
                    EmitStatus();
                    return;
                }
            }
        }

        _logger.LogInformation("Host_Running loop completed, sleeping 5s");
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
