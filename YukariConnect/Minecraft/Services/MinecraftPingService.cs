using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using YukariConnect.Minecraft.Models;

namespace YukariConnect.Minecraft.Services;

/// <summary>
/// Pings Minecraft servers to verify they are online and get server info.
/// Uses the modern Minecraft Server List Ping protocol.
/// </summary>
public sealed class MinecraftPingService
{
    private readonly ILogger<MinecraftPingService> _logger;
    private readonly TimeSpan _pingTimeout;

    public MinecraftPingService(
        TimeSpan? pingTimeout = null,
        ILogger<MinecraftPingService>? logger = null)
    {
        _pingTimeout = pingTimeout ?? TimeSpan.FromSeconds(3);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MinecraftPingService>.Instance;
    }

    /// <summary>
    /// Pings a Minecraft server to verify it's online and get info.
    /// Returns null if the server is offline or unreachable.
    /// </summary>
    public async Task<MinecraftPingResult?> PingAsync(IPEndPoint endPoint, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        try
        {
            // Connect with timeout using CancellationToken
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_pingTimeout);

            await client.ConnectAsync(endPoint.Address, endPoint.Port, cts.Token);

            // Send handshake + status request
            var stream = client.GetStream();

            // Handshake packet
            var handshake = BuildHandshake(endPoint.Address.ToString(), endPoint.Port);
            await stream.WriteAsync(handshake, cts.Token);

            // Status request packet (empty)
            var statusRequest = new byte[] { 0x01, 0x00 };
            await stream.WriteAsync(statusRequest, cts.Token);

            // Read response
            var response = await ReadFullResponseAsync(stream, cts.Token);
            if (response == null) return null;

            // Parse JSON
            var json = Encoding.UTF8.GetString(response);
            _logger.LogDebug("Ping response from {EndPoint}: {Json}", endPoint, json);

            return ParsePingResponse(json);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException)
        {
            _logger.LogDebug("Ping failed to {EndPoint}: {Message}", endPoint, ex.Message);
            return null;
        }
        finally
        {
            try { client.Close(); } catch { }
        }
    }

    private static byte[] BuildHandshake(string host, int port)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Packet ID: 0x00
        writer.Write((byte)0x00);

        // Protocol version: 47 (1.8, compatible with most modern servers)
        WriteVarInt(writer, 47);

        // Host (UTF-8)
        var hostBytes = Encoding.UTF8.GetBytes(host);
        WriteVarInt(writer, hostBytes.Length);
        writer.Write(hostBytes);

        // Port
        writer.Write((ushort)port);

        // Next state: 1 (Status)
        writer.Write((byte)0x01);

        // Build full packet with length prefix
        var packet = ms.ToArray();
        var packetLength = EncodeVarInt(packet.Length);
        var result = new byte[packetLength.Length + packet.Length];
        packetLength.CopyTo(result, 0);
        packet.CopyTo(result, packetLength.Length);

        return result;
    }

    private static async Task<byte[]?> ReadFullResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        try
        {
            // Read varint packet length
            int packetLength = await ReadVarIntAsync(stream, ct);
            if (packetLength <= 0 || packetLength > 1048576) return null; // Sanity check: max 1MB

            // Read varint packet ID (should be 0x00)
            int packetId = await ReadVarIntAsync(stream, ct);
            if (packetId != 0x00) return null;

            // Read varint payload length
            int payloadLength = await ReadVarIntAsync(stream, ct);
            if (payloadLength <= 0 || payloadLength > 1048576) return null;

            // Read JSON payload
            var buffer = new byte[payloadLength];
            int bytesRead = 0;
            while (bytesRead < payloadLength)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(bytesRead, payloadLength - bytesRead), ct);
                if (n == 0) return null;
                bytesRead += n;
            }

            return buffer;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken ct)
    {
        int result = 0;
        int shift = 0;
        byte b;

        do
        {
            var buffer = new byte[1];
            int n = await stream.ReadAsync(buffer, 0, 1, ct);
            if (n == 0) throw new IOException("Failed to read varint");
            b = buffer[0];

            result |= (b & 0x7F) << shift;
            shift += 7;

            if (shift > 35) throw new IOException("VarInt too big");
        }
        while ((b & 0x80) != 0);

        return result;
    }

    private static void WriteVarInt(BinaryWriter writer, int value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            writer.Write(b);
        }
        while (value != 0);
    }

    private static byte[] EncodeVarInt(int value)
    {
        using var ms = new MemoryStream();
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            ms.WriteByte(b);
        }
        while (value != 0);
        return ms.ToArray();
    }

    private static MinecraftPingResult? ParsePingResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var version = root.GetProperty("version");
            var players = root.GetProperty("players");

            return new MinecraftPingResult
            {
                Version = version.GetProperty("name").GetString() ?? "Unknown",
                Protocol = version.GetProperty("protocol").GetInt32(),
                MaxPlayers = players.GetProperty("max").GetInt32(),
                OnlinePlayers = players.GetProperty("online").GetInt32(),
                Description = root.GetProperty("description").GetString() ?? ""
            };
        }
        catch
        {
            return null;
        }
    }
}
