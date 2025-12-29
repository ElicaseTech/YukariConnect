using System.Text.Json.Serialization;

namespace YukariConnect.WebSocket.Models;

public sealed class ServerInfoDto
{
    [JsonPropertyName("endPoint")]
    public string EndPoint { get; set; } = string.Empty;

    [JsonPropertyName("motd")]
    public string Motd { get; set; } = string.Empty;

    [JsonPropertyName("isVerified")]
    public bool IsVerified { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("onlinePlayers")]
    public int OnlinePlayers { get; set; }

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; }
}

public sealed class ServersListResponseData
{
    [JsonPropertyName("servers")]
    public List<ServerInfoDto> Servers { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public sealed class GetServerByIpRequestData
{
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;
}

public sealed class ServerByIpResponseData
{
    [JsonPropertyName("server")]
    public ServerInfoDto Server { get; set; } = new();
}

public sealed class SearchServersRequestData
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;
}

public sealed class MinecraftStatusResponseData
{
    [JsonPropertyName("totalServers")]
    public int TotalServers { get; set; }

    [JsonPropertyName("verifiedServers")]
    public int VerifiedServers { get; set; }
}
