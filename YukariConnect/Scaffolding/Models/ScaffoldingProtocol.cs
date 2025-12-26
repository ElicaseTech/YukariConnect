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
/// Player profile information.
/// </summary>
public sealed class ScaffoldingProfile
{
    public required string Name { get; init; }
    public required string MachineId { get; init; }  // 32 hex chars (16 bytes)
    public required string Vendor { get; init; }
    public required ScaffoldingProfileKind Kind { get; init; }

    // Optional: EasyTier ID for extended functionality
    public string? EasyTierId { get; init; }
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
    public required string Name { get; set; }
    public required string MachineId { get; set; }
    public required string Vendor { get; set; }
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
