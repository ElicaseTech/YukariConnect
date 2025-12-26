using System.Diagnostics;
using YukariConnect.Minecraft.Services;
using YukariConnect.Scaffolding.Models;

namespace YukariConnect.Scaffolding;

/// <summary>
/// Room role type.
/// </summary>
public sealed class RoomRole
{
    public string Value { get; }

    public static readonly RoomRole HostCenter = new("HostCenter");
    public static readonly RoomRole Guest = new("Guest");

    private RoomRole(string value)
    {
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>
/// Room state kind for state machine.
/// </summary>
public sealed class RoomStateKind
{
    public string Value { get; }

    public static readonly RoomStateKind Idle = new("Idle");

    // HostCenter path
    public static readonly RoomStateKind Host_Prepare = new("Host_Prepare");
    public static readonly RoomStateKind Host_EasyTierStarting = new("Host_EasyTierStarting");
    public static readonly RoomStateKind Host_ScaffoldingStarting = new("Host_ScaffoldingStarting");
    public static readonly RoomStateKind Host_MinecraftDetecting = new("Host_MinecraftDetecting");
    public static readonly RoomStateKind Host_Running = new("Host_Running");

    // Guest path
    public static readonly RoomStateKind Guest_Prepare = new("Guest_Prepare");
    public static readonly RoomStateKind Guest_EasyTierStarting = new("Guest_EasyTierStarting");
    public static readonly RoomStateKind Guest_DiscoveringCenter = new("Guest_DiscoveringCenter");
    public static readonly RoomStateKind Guest_ConnectingScaffolding = new("Guest_ConnectingScaffolding");
    public static readonly RoomStateKind Guest_Running = new("Guest_Running");

    // Terminal / Error
    public static readonly RoomStateKind Stopping = new("Stopping");
    public static readonly RoomStateKind Error = new("Error");

    private RoomStateKind(string value)
    {
        Value = value;
    }

    public static RoomStateKind FromString(string value)
    {
        return AllStates.FirstOrDefault(s => s.Value == value) ?? new RoomStateKind(value);
    }

    public static readonly RoomStateKind[] AllStates = new[]
    {
        Idle,
        Host_Prepare, Host_EasyTierStarting, Host_ScaffoldingStarting, Host_MinecraftDetecting, Host_Running,
        Guest_Prepare, Guest_EasyTierStarting, Guest_DiscoveringCenter, Guest_ConnectingScaffolding, Guest_Running,
        Stopping, Error
    };

    public override string ToString() => Value;
}

/// <summary>
/// Room runtime context passed through the state machine.
/// </summary>
public sealed class RoomRuntime
{
    public required RoomRole Role { get; init; }

    // Room code / Network credentials
    public string? RoomCode { get; set; }
    public required string NetworkName { get; init; }
    public required string NetworkSecret { get; init; }

    // Scaffolding
    public required ushort ScaffoldingPort { get; init; } = 13448;
    public ushort? MinecraftPort { get; set; }

    // Center discovery (for guest)
    public System.Net.IPAddress? CenterIp { get; set; }
    public ushort? CenterScaffoldingPort { get; set; }

    // Player info
    public required string MachineId { get; init; }
    public required string PlayerName { get; init; }
    public required string Vendor { get; init; }

    // Process handles
    public Process? EasyTierProcess { get; set; }
    public ScaffoldingServer? ScaffoldingServer { get; set; }
    public ScaffoldingClient? ScaffoldingClient { get; set; }

    // Fake MC server for broadcasting to virtual network
    public MinecraftFakeServer? FakeServer { get; set; }
}

/// <summary>
/// EasyTier peer info.
/// </summary>
public sealed class EasyTierPeer
{
    public required string Hostname { get; init; }
    public System.Net.IPAddress? IpAddress { get; init; }
    public bool IsLocal { get; init; }
    public string? NatType { get; init; }
}

/// <summary>
/// Room status for API responses.
/// </summary>
public sealed class RoomStatus
{
    public required RoomStateKind State { get; init; }
    public required RoomRole? Role { get; init; }
    public string? Error { get; init; }
    public required string? RoomCode { get; init; }
    public required IReadOnlyList<ScaffoldingProfile> Players { get; init; }
    public required ushort? MinecraftPort { get; init; }
    public required DateTimeOffset LastUpdate { get; init; }
}
