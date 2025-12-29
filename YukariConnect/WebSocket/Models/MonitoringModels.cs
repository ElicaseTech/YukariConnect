using System.Text.Json.Serialization;

namespace YukariConnect.WebSocket.Models;

public sealed class MetadataResponseData
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("compileTimestamp")]
    public DateTimeOffset CompileTimestamp { get; set; }

    [JsonPropertyName("easyTierVersion")]
    public string EasyTierVersion { get; set; } = string.Empty;

    [JsonPropertyName("yggdrasilPort")]
    public string YggdrasilPort { get; set; } = string.Empty;

    [JsonPropertyName("targetTuple")]
    public string TargetTuple { get; set; } = string.Empty;

    [JsonPropertyName("targetArch")]
    public string TargetArch { get; set; } = string.Empty;

    [JsonPropertyName("targetVendor")]
    public string TargetVendor { get; set; } = string.Empty;

    [JsonPropertyName("targetOS")]
    public string TargetOS { get; set; } = string.Empty;

    [JsonPropertyName("targetEnv")]
    public string TargetEnv { get; set; } = string.Empty;
}

public sealed class LogResponseData
{
    [JsonPropertyName("logLevel")]
    public string LogLevel { get; set; } = string.Empty;

    [JsonPropertyName("LogType")]
    public string LogType { get; set; } = string.Empty;

    [JsonPropertyName("logTime")]
    public DateTimeOffset LogTime { get; set; }

    [JsonPropertyName("logComponent")]
    public string LogComponent { get; set; } = string.Empty;

    [JsonPropertyName("logMessage")]
    public string LogMessage { get; set; } = string.Empty;
}
