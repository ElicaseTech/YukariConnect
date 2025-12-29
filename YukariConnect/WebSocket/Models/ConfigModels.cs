using System.Text.Json.Serialization;

namespace YukariConnect.WebSocket.Models;

public sealed class ConfigResponseData
{
    [JsonPropertyName("launcherCustomString")]
    public string LauncherCustomString { get; set; } = string.Empty;
}
