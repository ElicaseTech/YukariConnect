using System.Text.Json.Serialization;

namespace YukariConnect.WebSocket.Models;

public sealed class PublicServerDto
{
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }
}

public sealed class PublicServersListResponseData
{
    [JsonPropertyName("servers")]
    public List<PublicServerDto> Servers { get; set; } = new();
}
