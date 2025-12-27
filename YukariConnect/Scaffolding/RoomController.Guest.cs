using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YukariConnect.Scaffolding.Models;
using YukariConnect.Services;
using YukariConnect.Minecraft.Services;
using YukariConnect.Network;

namespace YukariConnect.Scaffolding;

/// <summary>
/// Guest state machine steps implementation.
/// </summary>
public sealed partial class RoomController
{
    private async Task StepGuestAsync(CancellationToken ct)
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
            if (_runtime?.ScaffoldingClient != null)
            {
                try
                {
                    await _runtime.ScaffoldingClient.DisposeAsync();
                    _runtime.ScaffoldingClient = null;
                    _logger.LogInformation("ScaffoldingClient disposed in Error state");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispose ScaffoldingClient in Error state");
                }
            }
            return;
        }

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
            var node = await _networkNode.GetNodeInfoAsync(ct);
            if (node != null)
            {
                _logger.LogInformation("Network layer is ready");
                _state = RoomStateKind.Guest_DiscoveringCenter;
                EmitStatus();
                return;
            }
        }

        // Get and validate public servers from peer discovery service
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
            Hostname = "guest",
            IsHost = false,
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
                _state = RoomStateKind.Guest_DiscoveringCenter;
                EmitStatus();
                return;
            }
        }

        _lastError = "Network startup timeout";
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

        var peersDoc = await _networkNode.GetPeersAsync(ct);

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

        // IMPORTANT: Guest MUST use port forwarding because the OS doesn't know how to route to virtual IP
        // Network layer will handle the routing internally via port-forwarding
        // Forward local port to remote center's Scaffolding server
        ushort localForwardPort = _runtime.CenterScaffoldingPort.Value;
        var localAddr = $"0.0.0.0:{localForwardPort}";
        var remoteAddr = $"{center.Ip}:{center.Port}";

        _logger.LogInformation("Setting up port forwarding: {Local} -> {Remote}", localAddr, remoteAddr);
        var forwardOk = await _networkNode.AddPortForwardAsync("tcp", localAddr, remoteAddr, ct);

        if (!forwardOk)
        {
            _lastError = "Failed to add port forwarding for Scaffolding client";
            _state = RoomStateKind.Error;
            EmitStatus();
            return;
        }

        _logger.LogInformation("Port forwarding added successfully");

        // Save the original virtual IP for Minecraft forwarding
        _runtime.CenterVirtualIp = center.Ip;

        // Update runtime to use localhost instead of virtual IP for Scaffolding client
        _runtime.CenterIp = IPAddress.Loopback;
        _runtime.CenterScaffoldingPort = localForwardPort;

        _centerDiscoveryStart = default;
        _state = RoomStateKind.Guest_ConnectingScaffolding;
        _logger.LogInformation("Transitioning to Guest_ConnectingScaffolding state");
        EmitStatus();
        _logger.LogInformation("State transition completed, new state={State}", _state);
    }

    private async Task StepGuest_ConnectingScaffoldingAsync(CancellationToken ct)
    {
        _logger.LogInformation("StepGuest_ConnectingScaffoldingAsync called");

        if (_runtime!.ScaffoldingClient != null)
        {
            // Already connected, move to running
            _logger.LogInformation("ScaffoldingClient already exists, moving to running");
            _state = RoomStateKind.Guest_Running;
            EmitStatus();
            return;
        }

        _logger.LogInformation("Creating ScaffoldingClient for {Ip}:{Port}",
            _runtime.CenterIp, _runtime.CenterScaffoldingPort);

        var client = new ScaffoldingClient(
            _runtime.CenterIp!.ToString(),
            _runtime.CenterScaffoldingPort!.Value,
            logger: _serviceProvider.GetRequiredService<ILogger<ScaffoldingClient>>());

        try
        {
            _logger.LogInformation("Connecting to Scaffolding server...");
            await client.ConnectAsync(ct);
            _logger.LogInformation("Connected to scaffolding server");

            // Reset retry counter on successful connection
            _scaffoldingConnectRetryCount = 0;

            // Verify fingerprint
            _logger.LogInformation("Verifying fingerprint with c:ping...");
            if (!await client.PingAsync(ct))
            {
                _lastError = "Fingerprint verification failed";
                await client.DisposeAsync();
                _state = RoomStateKind.Error;
                EmitStatus();
                return;
            }
            _logger.LogInformation("Fingerprint verified successfully");

            _runtime.ScaffoldingClient = client;

            // Get protocols
            _logger.LogInformation("Getting supported protocols...");
            var protocols = await client.GetProtocolsAsync(ct);
            _logger.LogInformation("Supported protocols: {Protocols}", string.Join(", ", protocols));

            // Register player
            _logger.LogInformation("Registering player with c:player_ping...");
            await client.PlayerPingAsync(_runtime.PlayerName, _runtime.MachineId, _runtime.Vendor, ct);
            _logger.LogInformation("Player registered successfully");

            _logger.LogInformation("Guest connected successfully, transitioning to running");
            _state = RoomStateKind.Guest_Running;
            EmitStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scaffolding connection failed");

            // Increment retry counter
            _scaffoldingConnectRetryCount++;

            const int maxRetries = 50;
            if (_scaffoldingConnectRetryCount < maxRetries)
            {
                _logger.LogWarning("Scaffolding connection attempt {Attempt}/{Max}, will retry in 2s...",
                    _scaffoldingConnectRetryCount, maxRetries);
                _lastError = $"Connection failed (attempt {_scaffoldingConnectRetryCount}/{maxRetries})";
                await client.DisposeAsync();

                // Stay in Guest_ConnectingScaffolding state to retry
                // Add delay before next attempt
                await Task.Delay(TimeSpan.FromSeconds(2), ct);

                // Emit status to update UI with retry count
                EmitStatus();
            }
            else
            {
                _logger.LogError("Scaffolding connection failed after {Max} attempts", maxRetries);
                _lastError = $"Connection failed after {maxRetries} attempts: {ex.Message}";
                await client.DisposeAsync();
                _state = RoomStateKind.Error;
                EmitStatus();
            }
        }
    }

    private DateTime _lastHeartbeat = DateTime.MinValue;
    private DateTime _lastPlayerListCheck = DateTime.MinValue;
    private bool _minecraftForwardSetup = false;

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

                    // Set up port forwarding for Minecraft (like Terracotta does)
                    // Forward local port to Host's Minecraft server at virtual IP
                    var mcPort = port.Value;
                    var localMcAddr = $"0.0.0.0:{mcPort}";
                    var remoteMcAddr = $"{_runtime.CenterVirtualIp}:{mcPort}";

                    _logger.LogInformation("Setting up Minecraft port forwarding: {Local} -> {Remote}",
                        localMcAddr, remoteMcAddr);

                    // Set up TCP forwarding for Minecraft
                    var tcpForwardOk = await _networkNode.AddPortForwardAsync("tcp", localMcAddr, remoteMcAddr, ct);
                    if (!tcpForwardOk)
                    {
                        _logger.LogWarning("Failed to add TCP port forwarding for Minecraft");
                    }

                    // Set up UDP forwarding for Minecraft
                    var udpForwardOk = await _networkNode.AddPortForwardAsync("udp", localMcAddr, remoteMcAddr, ct);
                    if (!udpForwardOk)
                    {
                        _logger.LogWarning("Failed to add UDP port forwarding for Minecraft");
                    }

                    if (tcpForwardOk || udpForwardOk)
                    {
                        _logger.LogInformation("Minecraft port forwarding added successfully");
                        _minecraftForwardSetup = true;

                        // Start FakeServer to broadcast MC server to Guest's local network
                        // This allows other players on Guest's LAN to see and join the game
                        // Get host player name from profiles list
                        var profiles = await _runtime.ScaffoldingClient.GetPlayerProfilesAsync(ct);
                        var hostProfile = profiles.FirstOrDefault(p => p.Kind.Value == "HOST");
                        var hostName = hostProfile?.Name ?? _runtime.PlayerName;

                        // Generate MOTD with vendor info (shorten it if too long)
                        var vendor = _runtime.Vendor;
                        if (vendor.Length > 30)
                            vendor = vendor.Substring(0, 27) + "...";

                        var motd = $"{hostName}'s World [{vendor}]";
                        _runtime.FakeServer = new MinecraftFakeServer(
                            mcPort,
                            motd,
                            logger: _serviceProvider.GetRequiredService<ILogger<MinecraftFakeServer>>());
                        await _runtime.FakeServer.StartAsync(ct);
                        _logger.LogInformation("FakeServer broadcasting locally on port {Port} with MOTD: {Motd}", mcPort, motd);
                    }

                    EmitStatus();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get Minecraft server port");
            }
        }

        await Task.Delay(_tick, ct);
    }
}
