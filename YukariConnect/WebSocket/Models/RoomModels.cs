using System.Text.Json.Serialization;

namespace YukariConnect.WebSocket.Models;

public sealed class StartHostRequestData
{
    [JsonPropertyName("scaffoldingPort")]
    public int ScaffoldingPort { get; set; }

    [JsonPropertyName("playerName")]
    public string? PlayerName { get; set; }

    [JsonPropertyName("launcherCustomString")]
    public string? LauncherCustomString { get; set; }

    [JsonPropertyName("room")]
    public string? Room { get; set; }

    [JsonPropertyName("player")]
    public string? Player { get; set; }
}

public sealed class StartHostResponseData
{
    [JsonPropertyName("room")]
    public string Room { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public sealed class JoinRoomRequestData
{
    [JsonPropertyName("room")]
    public string Room { get; set; } = string.Empty;

    [JsonPropertyName("player")]
    public string? Player { get; set; }

    [JsonPropertyName("launcherCustomString")]
    public string? LauncherCustomString { get; set; }
}

public sealed class BasicStatusData
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public sealed class RoomStatusResponseData
{
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("roomCode")]
    public string RoomCode { get; set; } = string.Empty;

    [JsonPropertyName("players")]
    public List<RoomProfileDto> Players { get; set; } = new();

    [JsonPropertyName("minecraftPort")]
    public int MinecraftPort { get; set; }

    [JsonPropertyName("lastUpdate")]
    public DateTimeOffset LastUpdate { get; set; }
}
