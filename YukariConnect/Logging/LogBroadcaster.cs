using Microsoft.Extensions.Logging;

namespace YukariConnect.Logging;

/// <summary>
/// Service for broadcasting logs to WebSocket clients.
/// </summary>
public interface ILogBroadcaster
{
    void Broadcast(DateTimeOffset timestamp, string level, string category, string message);
    int GetClientCount();
}

/// <summary>
/// Log entry structure for WebSocket transmission.
/// </summary>
public record LogEntry(
    [property: System.Text.Json.Serialization.JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: System.Text.Json.Serialization.JsonPropertyName("level")] string Level,
    [property: System.Text.Json.Serialization.JsonPropertyName("category")] string Category,
    [property: System.Text.Json.Serialization.JsonPropertyName("message")] string Message
);

/// <summary>
/// Default implementation of log broadcaster.
/// Uses WebSocketManager for actual message delivery.
/// </summary>
public class LogBroadcaster : ILogBroadcaster
{
    private readonly WebSocket.IWebSocketManager _wsManager;

    public LogBroadcaster(WebSocket.IWebSocketManager wsManager)
    {
        _wsManager = wsManager;
    }

    public int GetClientCount() => _wsManager.GetClientCount();

    public void Broadcast(DateTimeOffset timestamp, string level, string category, string message)
    {
        var logEntry = new LogEntry(timestamp, level, category, message);
        _wsManager.Broadcast("log", logEntry);
    }
}
