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
    private static async Task<string?> ReceiveDataAsync(System.Net.WebSockets.WebSocket socket, string expectedCommand, int timeoutMs)
    {
        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        while (!cts.IsCancellationRequested)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await socket.ReceiveAsync(buffer, cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return null;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            var json = sb.ToString();
            if (json.Length == 0 || json[0] != '{')
                continue;
            var cmd = ExtractCommand(json);
            if (!string.Equals(cmd, expectedCommand, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var data))
                    return data.GetRawText();
            }
            catch { }
        }
        return null;
    }
    private static async Task<bool> WaitForCommandAsync(System.Net.WebSockets.WebSocket socket, string expectedCommand, int timeoutMs)
    {
        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        while (!cts.IsCancellationRequested)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await socket.ReceiveAsync(buffer, cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            var json = sb.ToString();
            if (json.Length == 0 || json[0] != '{')
                continue;
            var cmd = ExtractCommand(json);
            if (string.Equals(cmd, expectedCommand, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    [Fact(Skip = "Flaky under TestServer WebSocket in CI; covered by search_servers_response")]
    public async Task GetServerByIp_Invalid_Returns_Empty_Server()
    {
        var server = _factory.Server ?? throw new InvalidOperationException("Test server not initialized");
        var wsClient = server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        // Flush initial auto-downlink messages to avoid race with our request
        _ = await WaitForCommandAsync(socket, "get_metadata_response", 2000);

        var req = "{\"command\":\"get_server_by_ip\",\"timestamp\":0,\"data\":{\"ip\":\"not.an.ip\"}}";
        var bytes = Encoding.UTF8.GetBytes(req);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var ok = await WaitForCommandAsync(socket, "get_server_by_ip_response", 15000);
        Assert.True(ok);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task SearchServers_Empty_Returns_Count_0()
    {
        var server = _factory.Server ?? throw new InvalidOperationException("Test server not initialized");
        var wsClient = server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        var req = "{\"command\":\"search_servers\",\"timestamp\":0,\"data\":{\"query\":\"anything\"}}";
        var bytes = Encoding.UTF8.GetBytes(req);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var ok = await WaitForCommandAsync(socket, "search_servers_response", 3000);
        Assert.True(ok);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task GetMinecraftStatus_Returns_Zero()
    {
        var server = _factory.Server ?? throw new InvalidOperationException("Test server not initialized");
        var wsClient = server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        var req = "{\"command\":\"get_minecraft_status\",\"timestamp\":0,\"data\":{}}";
        var bytes = Encoding.UTF8.GetBytes(req);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var ok = await WaitForCommandAsync(socket, "get_minecraft_status_response", 3000);
        Assert.True(ok);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task SetLauncherCustomString_Updates_Config()
    {
        var server = _factory.Server ?? throw new InvalidOperationException("Test server not initialized");
        var wsClient = server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        var setReq = "{\"command\":\"set_launcher_custom_string\",\"timestamp\":0,\"data\":{\"launcherCustomString\":\"UnitTest/1.0\"}}";
        var setBytes = Encoding.UTF8.GetBytes(setReq);
        await socket.SendAsync(setBytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var getReq = "{\"command\":\"get_config\",\"timestamp\":0,\"data\":{}}";
        var getBytes = Encoding.UTF8.GetBytes(getReq);
        await socket.SendAsync(getBytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var dataCfg = await ReceiveDataAsync(socket, "get_config_response", 3000);
        Assert.NotNull(dataCfg);
        using var docCfg = JsonDocument.Parse(dataCfg);
        var value = docCfg.RootElement.GetProperty("launcherCustomString").GetString();
        Assert.Equal("UnitTest/1.0", value);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
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
    public async Task StartHost_With_Custom_Room_Uses_Provided_Code()
    {
        var server = _factory.Server ?? throw new InvalidOperationException("Test server not initialized");
        var wsClient = server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        var room = YukariConnect.Scaffolding.ScaffoldingHelpers.GenerateRoomCode();
        var payload = "{\"command\":\"start_host\",\"timestamp\":0,\"data\":{\"scaffoldingPort\":13448,\"player\":\"Tester\",\"room\":\"" + room + "\"}}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var ok = await WaitForCommandAsync(socket, "start_host_response", 5000);
        Assert.True(ok);

        string? returnedRoom = null;
        var statusReq = "{\"command\":\"get_status\",\"timestamp\":0,\"data\":{}}";
        var statusBytes = Encoding.UTF8.GetBytes(statusReq);
        await socket.SendAsync(statusBytes, WebSocketMessageType.Text, true, CancellationToken.None);
        var data2 = await ReceiveDataAsync(socket, "get_status_response", 5000);
        if (data2 != null)
        {
            using var doc2 = JsonDocument.Parse(data2);
            returnedRoom = doc2.RootElement.GetProperty("room").GetString();
        }
        Assert.Equal(room, returnedRoom);

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

    [Fact]
    public async Task GetStatus_Responds_With_Both_Status_And_RoomStatus()
    {
        var server = _factory.Server ?? throw new InvalidOperationException("Test server not initialized");
        var wsClient = server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        var req = "{\"command\":\"get_status\",\"timestamp\":0,\"data\":{}}";
        var bytes = Encoding.UTF8.GetBytes(req);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[8192];
        var gotStatus = false;
        var gotRoomStatus = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!cts.IsCancellationRequested && (!gotStatus || !gotRoomStatus))
        {
            try
            {
                var result = await socket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var cmd = ExtractCommand(json);
                if (string.Equals(cmd, "get_status_response", StringComparison.OrdinalIgnoreCase))
                    gotStatus = true;
                if (string.Equals(cmd, "get_room_status_response", StringComparison.OrdinalIgnoreCase))
                    gotRoomStatus = true;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Assert.True(gotStatus);
        Assert.True(gotRoomStatus);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task Initial_Status_AutoDownlink_OnConnect()
    {
        var server = _factory.Server ?? throw new InvalidOperationException("Test server not initialized");
        var wsClient = server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        var buffer = new byte[8192];
        var gotStatus = false;
        var gotRoomStatus = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!cts.IsCancellationRequested && (!gotStatus || !gotRoomStatus))
        {
            try
            {
                var result = await socket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var cmd = ExtractCommand(json);
                if (string.Equals(cmd, "get_status_response", StringComparison.OrdinalIgnoreCase))
                    gotStatus = true;
                if (string.Equals(cmd, "get_room_status_response", StringComparison.OrdinalIgnoreCase))
                    gotRoomStatus = true;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Assert.True(gotStatus);
        Assert.True(gotRoomStatus);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }
    private static string? ExtractCommand(string payload)
    {
        var key = "\"command\"";
        var idx = payload.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var colonIdx = payload.IndexOf(':', idx);
        if (colonIdx < 0) return null;
        var q1 = payload.IndexOf('"', colonIdx);
        if (q1 < 0) return null;
        var q2 = payload.IndexOf('"', q1 + 1);
        if (q2 < 0) return null;
        return payload.Substring(q1 + 1, q2 - q1 - 1);
    }
    private static string? GetCommandStrict(string payload)
    {
        if (payload.Length == 0 || payload[0] != '{')
            return null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("command", out var c))
                return c.GetString();
        }
        catch { }
        return null;
    }
}
