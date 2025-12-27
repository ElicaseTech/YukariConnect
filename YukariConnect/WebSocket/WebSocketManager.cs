using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace YukariConnect.WebSocket;

/// <summary>
/// WebSocket message structure.
/// All WebSocket messages use this format.
/// </summary>
public record WsMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("data")] object? Data = null
);

/// <summary>
/// Interface for WebSocket connection manager.
/// Manages WebSocket clients and message broadcasting.
/// </summary>
public interface IWebSocketManager
{
    /// <summary>
    /// Register a new WebSocket client.
    /// </summary>
    void RegisterClient(Guid clientId, System.Net.WebSockets.WebSocket ws);

    /// <summary>
    /// Unregister a WebSocket client.
    /// </summary>
    void UnregisterClient(Guid clientId);

    /// <summary>
    /// Get the number of connected clients.
    /// </summary>
    int GetClientCount();

    /// <summary>
    /// Broadcast a message to all connected clients.
    /// </summary>
    void Broadcast(string type, object? data);

    /// <summary>
    /// Send a message to a specific client.
    /// </summary>
    Task SendToClientAsync(Guid clientId, string type, object? data, CancellationToken ct);
}

/// <summary>
/// Default implementation of WebSocket manager.
/// Thread-safe, supports multiple concurrent connections.
/// </summary>
public class WebSocketManager : IWebSocketManager
{
    private readonly ConcurrentDictionary<Guid, System.Net.WebSockets.WebSocket> _clients = new();

    public void RegisterClient(Guid clientId, System.Net.WebSockets.WebSocket ws)
    {
        _clients[clientId] = ws;
    }

    public void UnregisterClient(Guid clientId)
    {
        _clients.TryRemove(clientId, out _);
    }

    public int GetClientCount() => _clients.Count;

    public void Broadcast(string type, object? data)
    {
        var json = SerializeMessage(type, data);
        if (json == null) return;

        var deadClients = new List<Guid>();

        foreach (var (clientId, ws) in _clients)
        {
            try
            {
                if (ws.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    // Fire and forget
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ws.SendAsync(
                                new ArraySegment<byte>(json, 0, json.Length),
                                System.Net.WebSockets.WebSocketMessageType.Text,
                                true,
                                CancellationToken.None
                            );
                        }
                        catch
                        {
                            // Client disconnected
                        }
                    });
                }
                else
                {
                    deadClients.Add(clientId);
                }
            }
            catch
            {
                deadClients.Add(clientId);
            }
        }

        // Clean up dead clients
        foreach (var clientId in deadClients)
        {
            _clients.TryRemove(clientId, out _);
        }
    }

    public async Task SendToClientAsync(Guid clientId, string type, object? data, CancellationToken ct)
    {
        if (!_clients.TryGetValue(clientId, out var ws))
        {
            throw new ArgumentException($"Client {clientId} not found");
        }

        var json = SerializeMessage(type, data);
        if (json == null) return;

        await ws.SendAsync(
            new ArraySegment<byte>(json, 0, json.Length),
            System.Net.WebSockets.WebSocketMessageType.Text,
            true,
            ct
        );
    }

    private static byte[]? SerializeMessage(string type, object? data)
    {
        try
        {
            using var ms = new System.IO.MemoryStream();
            // Use UTF8Encoding without BOM to avoid JSON parsing issues
            using var writer = new System.IO.StreamWriter(ms, new System.Text.UTF8Encoding(false));
            WriteWsMessage(writer, type, data);
            writer.Flush();
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static void WriteWsMessage(System.IO.StreamWriter writer, string type, object? data)
    {
        writer.Write("{\"type\":\"");
        writer.Write(type);
        writer.Write("\",\"data\":");

        if (data is null)
        {
            writer.Write("null");
        }
        else if (data is Logging.LogEntry log)
        {
            // Log entry
            writer.Write("{\"timestamp\":\"");
            writer.Write(log.Timestamp.ToString("o"));
            writer.Write("\",\"level\":\"");
            writer.Write(log.Level);
            writer.Write("\",\"category\":\"");
            writer.Write(EscapeJsonString(log.Category));
            writer.Write("\",\"message\":\"");
            writer.Write(EscapeJsonString(log.Message));
            writer.Write("\"}");
        }
        else
        {
            // For other types, use reflection
#pragma warning disable IL2075 // 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties'
            var props = data.GetType().GetProperties();
#pragma warning restore IL2075
            writer.Write("{");
            var first = true;
            foreach (var prop in props)
            {
                if (!first) writer.Write(",");
                first = false;
                writer.Write("\"");
                writer.Write(prop.Name);
                writer.Write("\":");
                var value = prop.GetValue(data);
                if (value is string s)
                {
                    writer.Write("\"");
                    writer.Write(EscapeJsonString(s));
                    writer.Write("\"");
                }
                else if (value is Guid g)
                {
                    writer.Write("\"");
                    writer.Write(g.ToString());
                    writer.Write("\"");
                }
                else if (value is int i)
                {
                    writer.Write(i.ToString());
                }
                else
                {
                    writer.Write(value?.ToString() ?? "null");
                }
            }
            writer.Write("}");
        }

        writer.Write("}");
    }

    private static string EscapeJsonString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
