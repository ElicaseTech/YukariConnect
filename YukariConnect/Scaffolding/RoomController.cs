using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YukariConnect.Scaffolding.Models;
using YukariConnect.Services;

namespace YukariConnect.Scaffolding;

/// <summary>
/// Room controller with state machine for HostCenter and Guest roles.
/// Manages EasyTier, Scaffolding server/client, and Minecraft integration.
/// </summary>
public sealed partial class RoomController : IAsyncDisposable
{
    private readonly ILogger<RoomController> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _machineIdPath;

    private readonly TimeSpan _tick = TimeSpan.FromMilliseconds(250);
    private readonly TimeSpan _easyTierStartupTimeout = TimeSpan.FromSeconds(12);
    private readonly TimeSpan _centerDiscoveryTimeout = TimeSpan.FromSeconds(25);
    private readonly TimeSpan _scaffoldingConnectTimeout = TimeSpan.FromSeconds(10);

    // State
    private RoomStateKind _state = RoomStateKind.Idle;
    private string? _lastError;
    private RoomRuntime? _runtime;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    // Events
    public event Action<RoomStatus>? OnStateChanged;

    public RoomController(
        IServiceProvider serviceProvider,
        ILogger<RoomController> logger,
        string? machineIdPath = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _machineIdPath = machineIdPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YukariConnect", "machine_id.txt");
    }

    /// <summary>
    /// Current state.
    /// </summary>
    public RoomStateKind State => _state;

    /// <summary>
    /// Last error message (if in Error state).
    /// </summary>
    public string? LastError => _lastError;

    /// <summary>
    /// Current status snapshot.
    /// </summary>
    public RoomStatus GetStatus()
    {
        List<ScaffoldingProfile> players = new();
        ushort? mcPort = null;

        if (_runtime?.Role == RoomRole.HostCenter && _runtime.ScaffoldingServer != null)
        {
            players = _runtime.ScaffoldingServer.GetPlayers();
            mcPort = _runtime.MinecraftPort;
        }
        else if (_runtime?.Role == RoomRole.Guest && _runtime.ScaffoldingClient != null)
        {
            try
            {
                var profiles = _runtime.ScaffoldingClient.GetPlayerProfilesAsync(default).GetAwaiter().GetResult();
                players = profiles.ToList();
                mcPort = _runtime.MinecraftPort;
            }
            catch { }
        }

        return new RoomStatus
        {
            State = _state,
            Role = _runtime?.Role,
            Error = _lastError,
            RoomCode = _runtime?.RoomCode,
            Players = players,
            MinecraftPort = mcPort,
            LastUpdate = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Start as HostCenter.
    /// </summary>
    public async Task StartHostAsync(
        string? roomCode,
        string networkName,
        string networkSecret,
        ushort scaffoldingPort = 13448,
        string playerName = "Host",
        CancellationToken ct = default)
    {
        if (_loop != null)
            throw new InvalidOperationException("Already running.");

        var machineId = ScaffoldingHelpers.LoadOrCreateMachineId(_machineIdPath);

        _runtime = new RoomRuntime
        {
            Role = RoomRole.HostCenter,
            RoomCode = roomCode ?? ScaffoldingHelpers.GenerateRoomCode(networkName, networkSecret),
            NetworkName = networkName,
            NetworkSecret = networkSecret,
            ScaffoldingPort = scaffoldingPort,
            MachineId = machineId,
            PlayerName = playerName,
            Vendor = "YukariConnect 1.0"
        };

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _state = RoomStateKind.Host_Prepare;
        _lastError = null;

        EmitStatus();

        _loop = Task.Run(() => RunHostLoopAsync(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// Start as Guest.
    /// </summary>
    public async Task StartGuestAsync(
        string roomCode,
        string playerName = "Guest",
        CancellationToken ct = default)
    {
        if (_loop != null)
            throw new InvalidOperationException("Already running.");

        if (!ScaffoldingHelpers.TryParseRoomCode(roomCode, out var networkName, out var networkSecret))
            throw new ArgumentException("Invalid room code format");

        var machineId = ScaffoldingHelpers.LoadOrCreateMachineId(_machineIdPath);

        _runtime = new RoomRuntime
        {
            Role = RoomRole.Guest,
            RoomCode = roomCode,
            NetworkName = networkName,
            NetworkSecret = networkSecret,
            ScaffoldingPort = 13448, // Default, will be discovered
            MachineId = machineId,
            PlayerName = playerName,
            Vendor = "YukariConnect 1.0"
        };

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _state = RoomStateKind.Guest_Prepare;
        _lastError = null;

        EmitStatus();

        _loop = Task.Run(() => RunGuestLoopAsync(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// Stop the current room session.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts == null) return;

        _state = RoomStateKind.Stopping;
        EmitStatus();

        _cts.Cancel();

        if (_loop != null)
        {
            try { await _loop; } catch { }
        }

        await CleanupAsync();

        _loop = null;
        _cts = null;
        _runtime = null;
        _state = RoomStateKind.Idle;
        _lastError = null;

        EmitStatus();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>
    /// Host state machine loop.
    /// </summary>
    private async Task RunHostLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await StepHostAsync(ct);
                await Task.Delay(_tick, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            _lastError = e.Message;
            _state = RoomStateKind.Error;
            _logger.LogError(e, "Host loop error");
            EmitStatus();
        }
    }

    /// <summary>
    /// Guest state machine loop.
    /// </summary>
    private async Task RunGuestLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await StepGuestAsync(ct);
                await Task.Delay(_tick, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            _lastError = e.Message;
            _state = RoomStateKind.Error;
            _logger.LogError(e, "Guest loop error");
            EmitStatus();
        }
    }

    private void EmitStatus()
    {
        try
        {
            OnStateChanged?.Invoke(GetStatus());
        }
        catch { }
    }

    private async Task CleanupAsync()
    {
        if (_runtime == null) return;

        try { await (_runtime.ScaffoldingClient?.DisposeAsync() ?? ValueTask.CompletedTask); } catch { }
        try { (_runtime.ScaffoldingServer as IAsyncDisposable)?.DisposeAsync(); } catch { }

        if (_runtime.EasyTierProcess != null && !_runtime.EasyTierProcess.HasExited)
        {
            try { _runtime.EasyTierProcess.Kill(entireProcessTree: true); } catch { }
        }

        _runtime.ScaffoldingClient = null;
        _runtime.ScaffoldingServer = null;
        _runtime.EasyTierProcess = null;
    }
}
