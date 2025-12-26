using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using YukariConnect.Scaffolding.Models;

namespace YukariConnect.Scaffolding;

/// <summary>
/// Scaffolding protocol TCP client.
/// Compatible with Terracotta's scaffolding protocol.
/// </summary>
public sealed partial class ScaffoldingClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _timeout;
    private readonly ILogger<ScaffoldingClient>? _logger;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;

    public ScaffoldingClient(
        string host,
        int port,
        TimeSpan? timeout = null,
        ILogger<ScaffoldingClient>? logger = null)
    {
        _host = host;
        _port = port;
        _timeout = timeout ?? TimeSpan.FromSeconds(64);
        _logger = logger;
    }

    /// <summary>
    /// Connect to the scaffolding server.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_client != null && _client.Connected) return;

        _client = new TcpClient();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            _cts.Token, timeoutCts.Token);

        await _client.ConnectAsync(_host, _port, linkedCts.Token);
        _stream = _client.GetStream();
    }

    /// <summary>
    /// Send a request and receive response.
    /// </summary>
    public async Task<ScaffoldingResponse> SendRequestAsync(
        string kind,
        byte[] body,
        CancellationToken ct = default)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        // Send request
        await SendRequestAsyncInternal(kind, body, ct);

        // Read response
        return await ReadResponseAsync(ct);
    }

    private async Task SendRequestAsyncInternal(string kind, byte[] body, CancellationToken ct)
    {
        var kindBytes = Encoding.UTF8.GetBytes(kind);

        // Write: kind length (1 byte)
        _stream!.WriteByte((byte)kindBytes.Length);

        // Write: kind (variable)
        await _stream.WriteAsync(kindBytes, ct);

        // Write: body length (4 bytes, Big Endian)
        var bodyLengthBytes = BitConverter.GetBytes((uint)body.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bodyLengthBytes);
        await _stream.WriteAsync(bodyLengthBytes, ct);

        // Write: body (variable)
        if (body.Length > 0)
            await _stream.WriteAsync(body, ct);

        await _stream.FlushAsync(ct);
    }

    private async Task<ScaffoldingResponse> ReadResponseAsync(CancellationToken ct)
    {
        // Read: status (1 byte)
        int status = _stream!.ReadByte();
        if (status < 0)
            throw new InvalidOperationException("Connection closed while reading status.");

        // Read: data length (4 bytes, Big Endian)
        var lengthBytes = new byte[4];
        await ReadExactAsync(lengthBytes, ct);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        int dataLength = BitConverter.ToInt32(lengthBytes, 0);

        // Read: data (variable)
        var data = new byte[dataLength];
        if (dataLength > 0)
            await ReadExactAsync(data, ct);

        return new ScaffoldingResponse
        {
            Status = (byte)status,
            Data = data
        };
    }

    private async Task ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int n = await _stream!.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (n == 0)
                throw new InvalidOperationException("Connection closed while reading data.");
            offset += n;
        }
    }

    /// <summary>
    /// Verify server identity using c:ping fingerprint.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        var response = await SendRequestAsync("c:ping", ScaffoldingFingerprint.Value, ct);

        if (!response.IsSuccess || response.Data.Length != 16)
            return false;

        return response.Data.SequenceEqual(ScaffoldingFingerprint.Value);
    }

    /// <summary>
    /// Get supported protocols list.
    /// </summary>
    public async Task<string[]> GetProtocolsAsync(CancellationToken ct = default)
    {
        var response = await SendRequestAsync("c:protocols", Array.Empty<byte>(), ct);

        if (!response.IsSuccess)
            throw new InvalidOperationException($"c:protocols failed: {response.GetErrorMessage()}");

        var protocolsStr = Encoding.UTF8.GetString(response.Data);
        return protocolsStr.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Get Minecraft server port from host.
    /// Returns null if MC server is not ready (status = 32).
    /// </summary>
    public async Task<ushort?> GetServerPortAsync(CancellationToken ct = default)
    {
        var response = await SendRequestAsync("c:server_port", Array.Empty<byte>(), ct);

        if (response.Status == 32)
            return null;  // MC server not ready

        if (!response.IsSuccess || response.Data.Length < 2)
            throw new InvalidOperationException($"c:server_port failed: status={response.Status}");

        if (BitConverter.IsLittleEndian)
            Array.Reverse(response.Data, 0, 2);
        return BitConverter.ToUInt16(response.Data, 0);
    }

    /// <summary>
    /// Send player heartbeat/registration.
    /// </summary>
    public async Task PlayerPingAsync(
        string name,
        string machineId,
        string vendor,
        CancellationToken ct = default)
    {
        var request = new PlayerPingRequest
        {
            Name = name,
            MachineId = machineId,
            Vendor = vendor
        };

        var body = JsonSerializer.SerializeToUtf8Bytes(request);
        var response = await SendRequestAsync("c:player_ping", body, ct);

        if (!response.IsSuccess)
            throw new InvalidOperationException($"c:player_ping failed: {response.GetErrorMessage()}");
    }

    /// <summary>
    /// Get list of all players in the room.
    /// </summary>
    public async Task<ScaffoldingProfile[]> GetPlayerProfilesAsync(CancellationToken ct = default)
    {
        var response = await SendRequestAsync("c:player_profiles_list", Array.Empty<byte>(), ct);

        if (!response.IsSuccess)
            throw new InvalidOperationException($"c:player_profiles_list failed: {response.GetErrorMessage()}");

        return JsonSerializer.Deserialize<ScaffoldingProfile[]>(response.Data)
            ?? Array.Empty<ScaffoldingProfile>();
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { }

        try { _stream?.Dispose(); } catch { }
        try { _client?.Close(); } catch { }
        try { _client?.Dispose(); } catch { }

        _cts?.Dispose();
        _stream = null;
        _client = null;
    }
}
