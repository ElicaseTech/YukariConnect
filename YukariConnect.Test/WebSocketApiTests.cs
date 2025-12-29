using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
 
namespace YukariConnect.Test;
 
public class WebSocketApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
 
    public WebSocketApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }
 
    [Fact]
    public async Task Ws_Endpoint_Connects_Successfully()
    {
        var server = _factory.Server ?? throw new InvalidOperationException("Test server not initialized");
        var wsClient = server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        Assert.Equal(WebSocketState.Open, socket.State);
 
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task Ws_Receives_Log_Broadcast()
    {
        var server = _factory.Server ?? throw new InvalidOperationException("Test server not initialized");
        var wsClient = server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        var httpClient = _factory.CreateClient();
        _ = await httpClient.GetAsync("/meta");

        var buffer = new byte[8192];
        var foundLog = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var result = await socket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var cmd = ExtractCommand(json);
                if (cmd != null && string.Equals(cmd, "get_log_response", StringComparison.OrdinalIgnoreCase))
                {
                    foundLog = true;
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Assert.True(foundLog);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    private static string? ExtractCommand(string payload)
    {
        var key = "\"command\"";
        var idx = payload.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var colonIdx = payload.IndexOf(':', idx);
        if (colonIdx < 0) return null;
        // skip possible spaces
        var q1 = payload.IndexOf('"', colonIdx);
        if (q1 < 0) return null;
        var q2 = payload.IndexOf('"', q1 + 1);
        if (q2 < 0) return null;
        return payload.Substring(q1 + 1, q2 - q1 - 1);
    }
}
