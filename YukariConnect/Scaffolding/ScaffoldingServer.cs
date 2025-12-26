using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YukariConnect.Scaffolding.Models;

namespace YukariConnect.Scaffolding;

/// <summary>
/// Scaffolding protocol TCP server.
/// Implements all standard protocols compatible with Terracotta.
/// </summary>
public sealed partial class ScaffoldingServer : IAsyncDisposable
{
    private readonly ushort _port;
    private readonly TimeSpan _heartbeatTimeout;
    private readonly ILogger<ScaffoldingServer> _logger;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private readonly List<Task> _clientHandlers = new();

    // Server state
    private ushort? _minecraftPort;
    private readonly Dictionary<string, PlayerEntry> _players = new();
    private readonly object _playersLock = new();

    public ScaffoldingServer(
        ushort port = 13448,
        TimeSpan? heartbeatTimeout = null,
        ILogger<ScaffoldingServer>? logger = null)
    {
        _port = port;
        _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(10);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ScaffoldingServer>.Instance;
    }

    /// <summary>
    /// Set the Minecraft server port.
    /// When null, c:server_port returns status=32 (not ready).
    /// </summary>
    public void SetMinecraftPort(ushort? port)
    {
        _minecraftPort = port;
    }

    /// <summary>
    /// Start the scaffolding server.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Start heartbeat cleanup loop
        Task.Run(() => HeartbeatCleanupLoopAsync(_cts.Token), _cts.Token);

        _acceptLoop = AcceptLoopAsync(_cts.Token);

        _logger.LogInformation("Scaffolding server started on port {Port}", _port);
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;

        _cts.Cancel();
        _listener?.Stop();

        try { await (_acceptLoop ?? Task.CompletedTask); } catch { }

        foreach (var handler in _clientHandlers)
        {
            try { await handler; } catch { }
        }
        _clientHandlers.Clear();

        _cts.Dispose();
        _cts = null;

        _logger.LogInformation("Scaffolding server stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                var handler = Task.Run(() => HandleClientAsync(client, ct), ct);
                _clientHandlers.Add(handler);

                // Clean up completed tasks
                _clientHandlers.RemoveAll(t => t.IsCompleted);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    _logger.LogWarning(ex, "Error accepting client");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var endPoint = (IPEndPoint)client.Client.RemoteEndPoint!;
        _logger.LogDebug("Client connected from {EndPoint}", endPoint);

        try
        {
            using var _ = client;
            using var stream = client.GetStream();
            var reader = new StreamReader(stream, leaveOpen: true);

            while (!ct.IsCancellationRequested)
            {
                // Read request
                var request = await ReadRequestAsync(stream, ct);
                if (request == null) break;

                _logger.LogDebug("Received request: {Kind}", request.Kind);

                // Handle request
                var response = await HandleRequestAsync(request, endPoint);

                // Write response
                await WriteResponseAsync(stream, response, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Client handler error");
        }
        finally
        {
            _logger.LogDebug("Client disconnected: {EndPoint}", endPoint);
        }
    }

    private async Task<Models.ScaffoldingRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        // Read: kind length (1 byte)
        int kindLength = stream.ReadByte();
        if (kindLength < 0) return null;

        // Read: kind (variable)
        var kindBytes = new byte[kindLength];
        await ReadExactAsync(stream, kindBytes, ct);
        var kind = Encoding.UTF8.GetString(kindBytes);

        // Read: body length (4 bytes, Big Endian)
        var lengthBytes = new byte[4];
        await ReadExactAsync(stream, lengthBytes, ct);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        int bodyLength = BitConverter.ToInt32(lengthBytes, 0);

        // Sanity check
        if (bodyLength > 1024 * 1024) // Max 1MB
            throw new InvalidOperationException("Body too large");

        // Read: body (variable)
        var body = new byte[bodyLength];
        if (bodyLength > 0)
            await ReadExactAsync(stream, body, ct);

        return new Models.ScaffoldingRequest { Kind = kind, Body = body };
    }

    private async Task<Models.ScaffoldingResponse> HandleRequestAsync(
        Models.ScaffoldingRequest request,
        IPEndPoint clientEndPoint)
    {
        // Parse kind: namespace:path
        var parts = request.Kind.Split(':');
        if (parts.Length != 2 || parts[0] != "c")
        {
            return new Models.ScaffoldingResponse
            {
                Status = 255,  // Not found
                Data = Array.Empty<byte>()
            };
        }

        var command = parts[1];

        return command switch
        {
            "ping" => HandlePing(request),
            "protocols" => HandleProtocols(),
            "server_port" => HandleServerPort(),
            "player_ping" => await HandlePlayerPingAsync(request),
            "player_profiles_list" => HandlePlayerProfilesList(),
            _ => new Models.ScaffoldingResponse { Status = 255, Data = Array.Empty<byte>() }
        };
    }

    /// <summary>
    /// c:ping - Echo back request body for verification.
    /// </summary>
    private Models.ScaffoldingResponse HandlePing(Models.ScaffoldingRequest request)
    {
        // Echo back the request body
        return new Models.ScaffoldingResponse
        {
            Status = 0,
            Data = request.Body
        };
    }

    /// <summary>
    /// c:protocols - Get supported protocols list.
    /// </summary>
    private Models.ScaffoldingResponse HandleProtocols()
    {
        var protocols = "c:ping\0c:protocols\0c:server_port\0c:player_ping\0c:player_profiles_list";
        return new Models.ScaffoldingResponse
        {
            Status = 0,
            Data = Encoding.UTF8.GetBytes(protocols)
        };
    }

    /// <summary>
    /// c:server_port - Get Minecraft server port.
    /// Returns status=32 if MC not ready.
    /// </summary>
    private Models.ScaffoldingResponse HandleServerPort()
    {
        if (!_minecraftPort.HasValue)
        {
            return new Models.ScaffoldingResponse
            {
                Status = 32,  // Server not ready
                Data = Array.Empty<byte>()
            };
        }

        var portBytes = BitConverter.GetBytes(_minecraftPort.Value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(portBytes);

        return new Models.ScaffoldingResponse
        {
            Status = 0,
            Data = portBytes
        };
    }

    /// <summary>
    /// c:player_ping - Register/update player and heartbeat.
    /// </summary>
    private async Task<Models.ScaffoldingResponse> HandlePlayerPingAsync(Models.ScaffoldingRequest request)
    {
        PlayerPingRequest? ping;
        try
        {
            ping = JsonSerializer.Deserialize<PlayerPingRequest>(request.Body);
        }
        catch
        {
            return new Models.ScaffoldingResponse
            {
                Status = 1,
                Data = Encoding.UTF8.GetBytes("Invalid JSON")
            };
        }

        if (ping == null || string.IsNullOrEmpty(ping.MachineId))
        {
            return new Models.ScaffoldingResponse
            {
                Status = 1,
                Data = Encoding.UTF8.GetBytes("Missing machine_id")
            };
        }

        lock (_playersLock)
        {
            // Find existing player or add new
            if (!_players.TryGetValue(ping.MachineId, out var entry))
            {
                // New player - add as GUEST
                entry = new PlayerEntry
                {
                    MachineId = ping.MachineId,
                    Name = ping.Name,
                    Vendor = ping.Vendor,
                    Kind = ScaffoldingProfileKind.Guest,
                    LastSeen = DateTimeOffset.UtcNow
                };
                _players[ping.MachineId] = entry;
                _logger.LogInformation("New player registered: {Name} ({MachineId})", ping.Name, ping.MachineId);
            }
            else
            {
                // Existing player - update heartbeat
                // Don't allow modifying HOST (index 0)
                if (entry.Kind == ScaffoldingProfileKind.Host)
                {
                    return new Models.ScaffoldingResponse
                    {
                        Status = 1,
                        Data = Encoding.UTF8.GetBytes("Cannot modify host profile")
                    };
                }

                entry.Name = ping.Name;
                entry.Vendor = ping.Vendor;
                entry.LastSeen = DateTimeOffset.UtcNow;
            }
        }

        return new Models.ScaffoldingResponse
        {
            Status = 0,
            Data = Array.Empty<byte>()
        };
    }

    /// <summary>
    /// c:player_profiles_list - Get all players.
    /// </summary>
    private Models.ScaffoldingResponse HandlePlayerProfilesList()
    {
        List<ScaffoldingProfile> profiles;

        lock (_playersLock)
        {
            profiles = _players.Values.Select(e => new ScaffoldingProfile
            {
                Name = e.Name,
                MachineId = e.MachineId,
                Vendor = e.Vendor,
                Kind = e.Kind
            }).ToList();
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(profiles);
        return new Models.ScaffoldingResponse
        {
            Status = 0,
            Data = json
        };
    }

    /// <summary>
    /// Set the host profile (index 0).
    /// </summary>
    public void SetHostProfile(string name, string machineId, string vendor)
    {
        lock (_playersLock)
        {
            _players[machineId] = new PlayerEntry
            {
                MachineId = machineId,
                Name = name,
                Vendor = vendor,
                Kind = ScaffoldingProfileKind.Host,
                LastSeen = DateTimeOffset.UtcNow
            };
        }
        _logger.LogInformation("Host profile set: {Name} ({MachineId})", name, machineId);
    }

    /// <summary>
    /// Get all current players.
    /// </summary>
    public List<ScaffoldingProfile> GetPlayers()
    {
        lock (_playersLock)
        {
            return _players.Values.Select(e => new ScaffoldingProfile
            {
                Name = e.Name,
                MachineId = e.MachineId,
                Vendor = e.Vendor,
                Kind = e.Kind
            }).ToList();
        }
    }

    /// <summary>
    /// Background loop to remove players with expired heartbeats.
    /// </summary>
    private async Task HeartbeatCleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);

                var now = DateTimeOffset.UtcNow;
                var toRemove = new List<string>();

                lock (_playersLock)
                {
                    foreach (var kv in _players)
                    {
                        if (now - kv.Value.LastSeen > _heartbeatTimeout)
                        {
                            // Don't remove host
                            if (kv.Value.Kind != ScaffoldingProfileKind.Host)
                            {
                                toRemove.Add(kv.Key);
                            }
                        }
                    }

                    foreach (var key in toRemove)
                    {
                        var removed = _players.Remove(key);
                        if (removed)
                            _logger.LogInformation("Player timeout removed: {MachineId}", key);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in heartbeat cleanup");
            }
        }
    }

    private async Task WriteResponseAsync(NetworkStream stream, Models.ScaffoldingResponse response, CancellationToken ct)
    {
        // Write: status (1 byte)
        stream.WriteByte(response.Status);

        // Write: data length (4 bytes, Big Endian)
        var lengthBytes = BitConverter.GetBytes((uint)response.Data.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        await stream.WriteAsync(lengthBytes, ct);

        // Write: data (variable)
        if (response.Data.Length > 0)
            await stream.WriteAsync(response.Data, ct);

        await stream.FlushAsync(ct);
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (n == 0)
                throw new InvalidOperationException("Connection closed while reading");
            offset += n;
        }
    }

    private class PlayerEntry
    {
        public required string MachineId { get; init; }
        public string Name { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public ScaffoldingProfileKind Kind { get; set; } = ScaffoldingProfileKind.Guest;
        public DateTimeOffset LastSeen { get; set; }
    }
}
