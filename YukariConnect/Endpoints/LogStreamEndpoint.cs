using System.Net.WebSockets;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using YukariConnect.Logging;
using YukariConnect.WebSocket;

namespace YukariConnect.Endpoints;

/// <summary>
/// WebSocket endpoint for real-time log streaming.
/// Protocol: JSON messages with 'type' and 'data' fields.
/// </summary>
public static class LogStreamEndpoint
{
    private record ConnectedMessage([property: JsonPropertyName("message")] string Message, [property: JsonPropertyName("clientId")] Guid ClientId);
    private record InfoMessage([property: JsonPropertyName("message")] string Message);

    public static void Map(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("YukariConnect.Endpoints.LogStreamEndpoint");

        app.MapGet("/log/stream", async (
            HttpContext context,
            IWebSocketManager wsManager,
            CancellationToken ct) =>
        {
            logger.LogInformation("WebSocket connection requested from {RemoteEndPoint}", context.Connection.RemoteIpAddress);

            if (context.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                var clientId = Guid.NewGuid();
                wsManager.RegisterClient(clientId, webSocket);
                logger.LogInformation("WebSocket client {ClientId} connected", clientId);

                try
                {
                    // Send welcome message
                    await wsManager.SendToClientAsync(clientId, "connected",
                        new ConnectedMessage("Connected to YukariConnect log stream", clientId), ct);

                    // Send initial info
                    await wsManager.SendToClientAsync(clientId, "info",
                        new InfoMessage("Listening for logs..."), ct);

                    logger.LogInformation("WebSocket client {ClientId} initialized", clientId);

                    // Keep connection alive
                    var buffer = new byte[1024 * 4];
                    while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        var result = await webSocket.ReceiveAsync(buffer.AsMemory(0, buffer.Length), ct);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            logger.LogInformation("WebSocket client {ClientId} requested close", clientId);
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                    logger.LogInformation("WebSocket client {ClientId} connection cancelled", clientId);
                }
                catch (Exception ex)
                {
                    // Log error but don't throw
                    logger.LogError(ex, "WebSocket client {ClientId} error", clientId);
                }
                finally
                {
                    wsManager.UnregisterClient(clientId);
                    logger.LogInformation("WebSocket client {ClientId} unregistered", clientId);
                }
            }
            else
            {
                logger.LogWarning("WebSocket connection requested but not a valid WebSocket request");
                context.Response.StatusCode = 400;
            }
        });
    }
}
