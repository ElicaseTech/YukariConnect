using System.Text.Json.Serialization;

namespace YukariConnect.WebSocket.Models;

public sealed class RoomProfileDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("vendor")]
    public string Vendor { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;
}

public sealed class StatusResponseData
{
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("room")]
    public string? Room { get; set; }

    [JsonPropertyName("profileIndex")]
    public int ProfileIndex { get; set; }

    [JsonPropertyName("profiles")]
    public List<RoomProfileDto>? Profiles { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("difficulty")]
    public string? Difficulty { get; set; }
}
