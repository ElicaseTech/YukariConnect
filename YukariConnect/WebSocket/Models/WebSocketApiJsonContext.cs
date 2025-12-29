using System.Text.Json.Serialization;

namespace YukariConnect.WebSocket.Models;

[JsonSerializable(typeof(WsRequest))]
[JsonSerializable(typeof(WsResponse))]
[JsonSerializable(typeof(StatusResponseData))]
[JsonSerializable(typeof(RoomProfileDto))]
[JsonSerializable(typeof(StartHostRequestData))]
[JsonSerializable(typeof(StartHostResponseData))]
[JsonSerializable(typeof(JoinRoomRequestData))]
[JsonSerializable(typeof(BasicStatusData))]
[JsonSerializable(typeof(RoomStatusResponseData))]
[JsonSerializable(typeof(ConfigResponseData))]
[JsonSerializable(typeof(ServerInfoDto))]
[JsonSerializable(typeof(ServersListResponseData))]
[JsonSerializable(typeof(GetServerByIpRequestData))]
[JsonSerializable(typeof(ServerByIpResponseData))]
[JsonSerializable(typeof(SearchServersRequestData))]
[JsonSerializable(typeof(MinecraftStatusResponseData))]
[JsonSerializable(typeof(PublicServerDto))]
[JsonSerializable(typeof(PublicServersListResponseData))]
[JsonSerializable(typeof(MetadataResponseData))]
[JsonSerializable(typeof(LogResponseData))]
[JsonSerializable(typeof(List<RoomProfileDto>))]
[JsonSerializable(typeof(List<ServerInfoDto>))]
[JsonSerializable(typeof(List<PublicServerDto>))]
public partial class WebSocketApiJsonContext : JsonSerializerContext
{
}
