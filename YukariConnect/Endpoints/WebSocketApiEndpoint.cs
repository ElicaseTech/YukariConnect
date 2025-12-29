using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using YukariConnect.Configuration;
using YukariConnect.Minecraft.Services;
using YukariConnect.Scaffolding;
using YukariConnect.Scaffolding.Models;
using YukariConnect.Services;
using YukariConnect.WebSocket;
using YukariConnect.WebSocket.Models;

namespace YukariConnect.Endpoints;

public static class WebSocketApiEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/ws", async (
            HttpContext context,
            IWebSocketManager wsManager,
            RoomController roomController,
            MinecraftLanState mcState,
            PublicServersService publicServers,
            EasyTierCliService etService,
            YukariOptions options,
            CancellationToken ct) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var clientId = Guid.NewGuid();
            wsManager.RegisterClient(clientId, webSocket);

            var stateHandler = new Action<RoomStatus>(async status =>
            {
                var data = MapStatusToStatusResponse(status);
                await wsManager.SendApiToClientAsync(clientId, "get_status_response", data, 0, "", ct);
                var roomData = MapStatusToRoomStatusResponse(status);
                await wsManager.SendApiToClientAsync(clientId, "get_room_status_response", roomData, 0, "", ct);
            });
            roomController.OnStateChanged += stateHandler;

            try
            {
                await SendInitialDownlinks(clientId, wsManager, mcState, publicServers, etService, options, ct);

                var recvBuffer = new byte[4096];
                while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    System.Net.WebSockets.WebSocketReceiveResult result;
                    try
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(recvBuffer), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (System.IO.IOException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (System.Net.WebSockets.WebSocketException)
                    {
                        break;
                    }
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                        break;
                    }

                    if (result.MessageType != WebSocketMessageType.Text) continue;

                    var json = Encoding.UTF8.GetString(recvBuffer, 0, result.Count);
                    WsRequest? request = null;
                    try
                    {
                        request = JsonSerializer.Deserialize(json, WebSocketApiJsonContext.Default.WsRequest);
                    }
                    catch { }
                    if (request == null) continue;

                    await HandleCommandAsync(clientId, request, wsManager, roomController, mcState, publicServers, etService, options, ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                roomController.OnStateChanged -= stateHandler;
                wsManager.UnregisterClient(clientId);
            }
        });
    }

    private static async Task SendInitialDownlinks(Guid clientId, IWebSocketManager wsManager, MinecraftLanState mcState, PublicServersService publicServers, EasyTierCliService etService, YukariOptions options, CancellationToken ct)
    {
        var serversList = mcState.AllServers.Select(s => new ServerInfoDto
        {
            EndPoint = s.EndPoint.ToString(),
            Motd = s.Motd,
            IsVerified = s.IsVerified,
            Version = s.PingResult?.Version,
            OnlinePlayers = s.PingResult?.OnlinePlayers ?? 0,
            MaxPlayers = s.PingResult?.MaxPlayers ?? 0
        }).ToList();
        await wsManager.SendApiToClientAsync(clientId, "list_servers_response", new ServersListResponseData
        {
            Servers = serversList,
            Count = serversList.Count
        }, 0, "", ct);

        var publicList = ParsePublicServers(publicServers.GetServers());
        await wsManager.SendApiToClientAsync(clientId, "list_public_servers_response", new PublicServersListResponseData
        {
            Servers = publicList
        }, 0, "", ct);

        var meta = await BuildMetadataAsync(etService, options, ct);
        await wsManager.SendApiToClientAsync(clientId, "get_metadata_response", meta, 0, "", ct);
    }

    private static async Task HandleCommandAsync(Guid clientId, WsRequest request, IWebSocketManager wsManager, RoomController roomController, MinecraftLanState mcState, PublicServersService publicServers, EasyTierCliService etService, YukariOptions options, CancellationToken ct)
    {
        var cmd = request.Command?.Trim().ToLowerInvariant();
        switch (cmd)
        {
            case "get_status":
            {
                var status = roomController.GetStatus();
                var data = MapStatusToStatusResponse(status);
                await wsManager.SendApiToClientAsync(clientId, "get_status_response", data, 0, "", ct);
                break;
            }
            case "start_host":
            {
                var payload = Deserialize<StartHostRequestData>(request.Data);
                ushort port = (ushort)(payload?.ScaffoldingPort > 0 ? payload!.ScaffoldingPort : options.DefaultScaffoldingPort);
                var playerName = payload?.PlayerName ?? "Host";
                var lcs = payload?.LauncherCustomString;
                try
                {
                    await roomController.StartHostAsync(port, playerName, lcs, ct);
                    var status = roomController.GetStatus();
                    await wsManager.SendApiToClientAsync(clientId, "start_host_response", new StartHostResponseData
                    {
                        Room = status.RoomCode ?? "",
                        Status = "ok"
                    }, 0, "", ct);
                }
                catch (Exception ex)
                {
                    await wsManager.SendApiToClientAsync(clientId, "start_host_response", new StartHostResponseData
                    {
                        Room = "",
                        Status = $"error:{ex.Message}"
                    }, 0, "", ct);
                }
                break;
            }
            case "join_room":
            {
                var payload = Deserialize<JoinRoomRequestData>(request.Data);
                var room = payload?.Room ?? "";
                var playerName = payload?.Player ?? "Guest";
                var lcs = payload?.LauncherCustomString;
                try
                {
                    await roomController.StartGuestAsync(room, playerName, lcs, ct);
                    await wsManager.SendApiToClientAsync(clientId, "join_room_response", new BasicStatusData
                    {
                        Status = "ok"
                    }, 0, "", ct);
                }
                catch (Exception ex)
                {
                    await wsManager.SendApiToClientAsync(clientId, "join_room_response", new BasicStatusData
                    {
                        Status = $"error:{ex.Message}"
                    }, 0, "", ct);
                }
                break;
            }
            case "get_room_status":
            {
                var status = roomController.GetStatus();
                var data = MapStatusToRoomStatusResponse(status);
                await wsManager.SendApiToClientAsync(clientId, "get_room_status_response", data, 0, "", ct);
                break;
            }
            case "stop_room":
            {
                await roomController.StopAsync();
                await wsManager.SendApiToClientAsync(clientId, "stop_room_response", new BasicStatusData
                {
                    Status = "ok"
                }, 0, "", ct);
                break;
            }
            case "room_retry":
            {
                await roomController.RetryAsync();
                await wsManager.SendApiToClientAsync(clientId, "room_retry_response", new BasicStatusData
                {
                    Status = "Retrying from error state..."
                }, 0, "", ct);
                break;
            }
            case "get_config":
            {
                await wsManager.SendApiToClientAsync(clientId, "get_config_response", new ConfigResponseData
                {
                    LauncherCustomString = options.LauncherCustomString ?? ""
                }, 0, "", ct);
                break;
            }
            case "set_launcher_custom_string":
            {
                var value = (request.Data as JsonElement?)?.GetProperty("launcherCustomString").GetString();
                options.LauncherCustomString = value;
                await wsManager.SendApiToClientAsync(clientId, "set_launcher_custom_string_response", new BasicStatusData
                {
                    Status = $"Launcher custom string set to: {value}"
                }, 0, "", ct);
                break;
            }
            case "get_server_by_ip":
            {
                var payload = Deserialize<GetServerByIpRequestData>(request.Data);
                var ip = payload?.Ip ?? "";
                if (!IPAddress.TryParse(ip, out var addr))
                {
                    await wsManager.SendApiToClientAsync(clientId, "get_server_by_ip_response", new ServerByIpResponseData
                    {
                        Server = new ServerInfoDto()
                    }, 0, "invalid ip", ct);
                    break;
                }
                var server = mcState.GetServer(addr);
                var dto = server == null ? new ServerInfoDto() : new ServerInfoDto
                {
                    EndPoint = server.EndPoint.ToString(),
                    Motd = server.Motd,
                    IsVerified = server.IsVerified,
                    Version = server.PingResult?.Version,
                    OnlinePlayers = server.PingResult?.OnlinePlayers ?? 0,
                    MaxPlayers = server.PingResult?.MaxPlayers ?? 0
                };
                await wsManager.SendApiToClientAsync(clientId, "get_server_by_ip_response", new ServerByIpResponseData
                {
                    Server = dto
                }, 0, "", ct);
                break;
            }
            case "search_servers":
            {
                var payload = Deserialize<SearchServersRequestData>(request.Data);
                var pattern = payload?.Query ?? "";
                var servers = mcState.FindServersByMotdPattern(pattern)
                    .Select(s => new ServerInfoDto
                    {
                        EndPoint = s.EndPoint.ToString(),
                        Motd = s.Motd,
                        IsVerified = s.IsVerified,
                        Version = s.PingResult?.Version,
                        OnlinePlayers = s.PingResult?.OnlinePlayers ?? 0,
                        MaxPlayers = s.PingResult?.MaxPlayers ?? 0
                    }).ToList();
                await wsManager.SendApiToClientAsync(clientId, "search_servers_response", new ServersListResponseData
                {
                    Servers = servers,
                    Count = servers.Count
                }, 0, "", ct);
                break;
            }
            case "get_minecraft_status":
            {
                await wsManager.SendApiToClientAsync(clientId, "get_minecraft_status_response", new MinecraftStatusResponseData
                {
                    TotalServers = mcState.TotalCount,
                    VerifiedServers = mcState.VerifiedCount
                }, 0, "", ct);
                break;
            }
            case "get_metadata":
            {
                var meta = await BuildMetadataAsync(etService, options, ct);
                await wsManager.SendApiToClientAsync(clientId, "get_metadata_response", meta, 0, "", ct);
                break;
            }
            case "panic":
            {
                await wsManager.SendApiToClientAsync(clientId, "panic_response", new BasicStatusData
                {
                    Status = "Panic triggered, cleaning up..."
                }, 0, "", ct);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100, ct);
                    Environment.Exit(1);
                });
                break;
            }
        }
    }

    private static T? Deserialize<T>(object? data) where T : class
    {
        if (data is null) return null;

        string? raw = null;
        if (data is JsonElement elem)
        {
            raw = elem.GetRawText();
        }
        else if (data is string s)
        {
            raw = s;
        }
        else
        {
            try
            {
                raw = JsonSerializer.Serialize(data);
            }
            catch
            {
                raw = null;
            }
        }

        if (raw == null) return null;

        try
        {
            if (typeof(T) == typeof(StartHostRequestData))
            {
                var res = JsonSerializer.Deserialize<StartHostRequestData>(raw, WebSocketApiJsonContext.Default.StartHostRequestData);
                return res as T;
            }
            if (typeof(T) == typeof(JoinRoomRequestData))
            {
                var res = JsonSerializer.Deserialize<JoinRoomRequestData>(raw, WebSocketApiJsonContext.Default.JoinRoomRequestData);
                return res as T;
            }
            if (typeof(T) == typeof(GetServerByIpRequestData))
            {
                var res = JsonSerializer.Deserialize<GetServerByIpRequestData>(raw, WebSocketApiJsonContext.Default.GetServerByIpRequestData);
                return res as T;
            }
            if (typeof(T) == typeof(SearchServersRequestData))
            {
                var res = JsonSerializer.Deserialize<SearchServersRequestData>(raw, WebSocketApiJsonContext.Default.SearchServersRequestData);
                return res as T;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static StatusResponseData MapStatusToStatusResponse(RoomStatus status)
    {
        var profiles = status.Players.Select(p => new RoomProfileDto
        {
            Name = p.Name,
            MachineId = p.MachineId,
            Vendor = p.Vendor,
            Kind = p.Kind.ToString()
        }).ToList();
        var terracottaState = MapRoomStateToTerracottaState(status.State);
        var role = status.Role?.Value == RoomRole.HostCenter.Value ? "host" :
            status.Role?.Value == RoomRole.Guest.Value ? "guest" : null;
        string? url = null;
        if (role == "guest" && status.MinecraftPort.HasValue)
        {
            url = $"127.0.0.1:{status.MinecraftPort.Value}";
        }
        return new StatusResponseData
        {
            State = terracottaState,
            Role = role,
            Room = status.RoomCode,
            ProfileIndex = 0,
            Profiles = profiles,
            Url = url,
            Difficulty = null
        };
    }

    private static RoomStatusResponseData MapStatusToRoomStatusResponse(RoomStatus status)
    {
        var players = status.Players.Select(p => new RoomProfileDto
        {
            Name = p.Name,
            MachineId = p.MachineId,
            Vendor = p.Vendor,
            Kind = p.Kind.ToString()
        }).ToList();
        var role = status.Role?.Value == RoomRole.HostCenter.Value ? "host" :
            status.Role?.Value == RoomRole.Guest.Value ? "guest" : null;
        return new RoomStatusResponseData
        {
            State = status.State.Value,
            Role = role ?? "",
            Error = status.Error,
            RoomCode = status.RoomCode ?? "",
            Players = players,
            MinecraftPort = status.MinecraftPort ?? 0,
            LastUpdate = status.LastUpdate
        };
    }

    private static string MapRoomStateToTerracottaState(RoomStateKind state)
    {
        return state.Value switch
        {
            "Idle" => "waiting",
            "Host_Prepare" => "host-scanning",
            "Host_EasyTierStarting" => "host-starting",
            "Host_ScaffoldingStarting" => "host-starting",
            "Host_MinecraftDetecting" => "host-starting",
            "Host_Running" => "host-ok",
            "Guest_Prepare" => "guest-connecting",
            "Guest_EasyTierStarting" => "guest-starting",
            "Guest_DiscoveringCenter" => "guest-starting",
            "Guest_ConnectingScaffolding" => "guest-starting",
            "Guest_Running" => "guest-ok",
            "Stopping" => "waiting",
            "Error" => "exception",
            _ => state.Value
        };
    }

    private static List<PublicServerDto> ParsePublicServers(string[] servers)
    {
        var list = new List<PublicServerDto>();
        foreach (var s in servers)
        {
            try
            {
                var uri = new Uri(s);
                list.Add(new PublicServerDto
                {
                    Hostname = uri.Host,
                    Port = uri.Port
                });
            }
            catch
            {
            }
        }
        return list;
    }

    private static async Task<MetadataResponseData> BuildMetadataAsync(EasyTierCliService etService, YukariOptions options, CancellationToken ct)
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        string compileTime = GetCompileTime();
        string? etVersion = null;
        try { etVersion = await etService.GetVersionAsync(ct); } catch { }

        var targetTuple = $"{RuntimeInformation.ProcessArchitecture}-{RuntimeInformation.OSArchitecture}-{RuntimeInformation.OSDescription}";
        return new MetadataResponseData
        {
            Version = version,
            CompileTimestamp = DateTimeOffset.TryParse(compileTime, out var dt) ? dt : DateTimeOffset.UtcNow,
            EasyTierVersion = etVersion ?? "",
            YggdrasilPort = options.DefaultScaffoldingPort.ToString(),
            TargetTuple = targetTuple,
            TargetArch = RuntimeInformation.ProcessArchitecture.ToString(),
            TargetVendor = RuntimeInformation.OSArchitecture.ToString(),
            TargetOS = RuntimeInformation.OSDescription,
            TargetEnv = RuntimeInformation.FrameworkDescription
        };
    }

    private static string GetCompileTime()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrEmpty(assemblyLocation))
            return DateTimeOffset.UtcNow.ToString("o");
        try
        {
            var fileInfo = new FileInfo(assemblyLocation);
            return fileInfo.LastWriteTimeUtc.ToString("o");
        }
        catch
        {
            return DateTimeOffset.UtcNow.ToString("o");
        }
    }
}
