using System.Text.Json.Serialization;
using System.Text.Json;

namespace YukariConnect.Scaffolding.Models;

/// <summary>
/// Scaffolding protocol fingerprint constant (16 bytes).
/// Used for c:ping verification to ensure connection to the correct server.
/// </summary>
public static class ScaffoldingFingerprint
{
    public static readonly byte[] Value = new byte[16]
    {
        0x41, 0x57, 0x48, 0x44, 0x86, 0x37, 0x40, 0x59,
        0x57, 0x44, 0x92, 0x43, 0x96, 0x99, 0x85, 0x01
    };
}

/// <summary>
/// Player profile information (for internal use).
/// </summary>
public sealed class ScaffoldingProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("machine_id")]
    public string MachineId { get; set; } = string.Empty;  // 32 hex chars (16 bytes)

    [JsonPropertyName("vendor")]
    public string Vendor { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public ScaffoldingProfileKind Kind { get; set; } = ScaffoldingProfileKind.Guest;

    // Optional: EasyTier ID for extended functionality
    [JsonPropertyName("easytier_id")]
    public string? EasyTierId { get; set; }
}

/// <summary>
/// Player profile DTO for JSON serialization (kind as string).
/// </summary>
public sealed class ScaffoldingProfileDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("machine_id")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("vendor")]
    public string Vendor { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "GUEST";

    [JsonPropertyName("easytier_id")]
    public string? EasyTierId { get; set; }
}

/// <summary>
/// Player role kind.
/// </summary>
public sealed class ScaffoldingProfileKind
{
    public string Value { get; }

    public static readonly ScaffoldingProfileKind Host = new("HOST");
    public static readonly ScaffoldingProfileKind Guest = new("GUEST");
    public static readonly ScaffoldingProfileKind Local = new("LOCAL");

    private ScaffoldingProfileKind(string value)
    {
        Value = value;
    }

    public static ScaffoldingProfileKind FromString(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "HOST" => Host,
            "GUEST" => Guest,
            "LOCAL" => Local,
            _ => new ScaffoldingProfileKind(value)
        };
    }

    public override string ToString() => Value;
}

/// <summary>
/// Scaffolding protocol request.
/// </summary>
public sealed class ScaffoldingRequest
{
    public required string Kind { get; init; }  // e.g., "c:ping"
    public required byte[] Body { get; init; }
}

/// <summary>
/// Scaffolding protocol response.
/// </summary>
public sealed class ScaffoldingResponse
{
    public required byte Status { get; init; }  // 0 = success
    public required byte[] Data { get; init; }

    public bool IsSuccess => Status == 0;

    public string? GetErrorMessage()
    {
        if (Status == 0) return null;
        return System.Text.Encoding.UTF8.GetString(Data);
    }
}

/// <summary>
/// Player ping request body (JSON).
/// </summary>
public sealed class PlayerPingRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("machine_id")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("vendor")]
    public string Vendor { get; set; } = string.Empty;

    [JsonPropertyName("easytier_id")]
    public string? EasyTierId { get; set; }
}

/// <summary>
/// Center discovery result.
/// </summary>
public sealed class CenterInfo
{
    public required System.Net.IPAddress Ip { get; init; }
    public required ushort Port { get; init; }
    public required string Hostname { get; init; }
}

/// <summary>
/// JSON source generator context for Scaffolding types (AOT-compatible).
/// </summary>
[JsonSerializable(typeof(PlayerPingRequest))]
[JsonSerializable(typeof(ScaffoldingProfile))]
[JsonSerializable(typeof(ScaffoldingProfileDto))]
[JsonSerializable(typeof(List<ScaffoldingProfile>))]
[JsonSerializable(typeof(List<ScaffoldingProfileDto>))]
public partial class ScaffoldingJsonContext : JsonSerializerContext
{
}
