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

    // Retry counters (shared across partial classes)
    internal int _scaffoldingConnectRetryCount = 0;
    internal int _mcFailureCount = 0;

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
        ushort scaffoldingPort = 13448,
        string playerName = "Host",
        string? launcherCustomString = null,
        CancellationToken ct = default)
    {
        if (_loop != null)
            throw new InvalidOperationException("Already running.");

        var machineId = ScaffoldingHelpers.LoadOrCreateMachineId(_machineIdPath);
        var roomCode = ScaffoldingHelpers.GenerateRoomCode();

        _logger.LogInformation("Generated room code: {RoomCode}, stamp: {Stamp}",
            roomCode, ScaffoldingHelpers.BuildStamp);

        if (!ScaffoldingHelpers.TryParseRoomCode(roomCode, out var networkName, out var networkSecret, out var error))
        {
            _logger.LogError("Failed to parse generated room code: {RoomCode}, error: {Error}, stamp: {Stamp}",
                roomCode, error, ScaffoldingHelpers.BuildStamp);
            throw new InvalidOperationException($"Generated invalid room code: {error}");
        }

        _logger.LogInformation("Parsed network name: {Network}, secret: {Secret}", networkName, networkSecret);

        // Get ET version for vendor string
        var etService = _serviceProvider.GetRequiredService<EasyTierCliService>();
        var etVersion = await etService.GetVersionAsync(ct);

        _runtime = new RoomRuntime
        {
            Role = RoomRole.HostCenter,
            RoomCode = roomCode,
            NetworkName = networkName,
            NetworkSecret = networkSecret,
            ScaffoldingPort = scaffoldingPort,
            MachineId = machineId,
            PlayerName = playerName,
            Vendor = ScaffoldingHelpers.GetVendorString(etVersion, launcherCustomString)
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
        string? launcherCustomString = null,
        CancellationToken ct = default)
    {
        if (_loop != null)
            throw new InvalidOperationException("Already running.");

        if (!ScaffoldingHelpers.TryParseRoomCode(roomCode, out var networkName, out var networkSecret))
            throw new ArgumentException("Invalid room code format");

        var machineId = ScaffoldingHelpers.LoadOrCreateMachineId(_machineIdPath);

        // Get ET version for vendor string
        var etService = _serviceProvider.GetRequiredService<EasyTierCliService>();
        var etVersion = await etService.GetVersionAsync(ct);

        _runtime = new RoomRuntime
        {
            Role = RoomRole.Guest,
            RoomCode = roomCode,
            NetworkName = networkName,
            NetworkSecret = networkSecret,
            ScaffoldingPort = 13448, // Default, will be discovered
            MachineId = machineId,
            PlayerName = playerName,
            Vendor = ScaffoldingHelpers.GetVendorString(etVersion, launcherCustomString)
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

    /// <summary>
    /// Retry from Error state.
    /// Resets retry counters and transitions back to the appropriate starting state.
    /// </summary>
    public async Task RetryAsync()
    {
        if (_state != RoomStateKind.Error)
        {
            _logger.LogWarning("RetryAsync called but not in Error state (current: {State})", _state);
            return;
        }

        _logger.LogInformation("Retrying from Error state...");

        // Reset retry counters
        _scaffoldingConnectRetryCount = 0;
        _mcFailureCount = 0;

        // Determine which state to retry from
        if (_runtime != null)
        {
            if (_runtime.IsHost)
            {
                // For host, retry from Host_Running state
                // The state machine will handle reconnection
                _logger.LogInformation("Retrying as Host, transitioning to Host_Running");
                _state = RoomStateKind.Host_Running;
                _lastError = null;
                EmitStatus();
            }
            else
            {
                // For guest, retry from Guest_ConnectingScaffolding state
                _logger.LogInformation("Retrying as Guest, transitioning to Guest_ConnectingScaffolding");
                _state = RoomStateKind.Guest_ConnectingScaffolding;
                _lastError = null;
                EmitStatus();
            }
        }
        else
        {
            _logger.LogWarning("RetryAsync: _runtime is null, cannot determine role");
        }

        await Task.CompletedTask;
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

        _logger.LogInformation("Cleaning up RoomController resources...");

        try { await (_runtime.ScaffoldingClient?.DisposeAsync() ?? ValueTask.CompletedTask); } catch { }

        // Properly await ScaffoldingServer disposal
        if (_runtime.ScaffoldingServer != null)
        {
            _logger.LogInformation("Stopping ScaffoldingServer...");
            try
            {
                await _runtime.ScaffoldingServer.StopAsync();
                _logger.LogInformation("ScaffoldingServer stopped");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop ScaffoldingServer");
            }
        }

        try { await (_runtime.FakeServer?.DisposeAsync() ?? ValueTask.CompletedTask); } catch { }

        if (_runtime.EasyTierProcess != null && !_runtime.EasyTierProcess.HasExited)
        {
            _logger.LogInformation("Killing EasyTier process (PID: {Pid})...", _runtime.EasyTierProcess.Id);
            try
            {
                _runtime.EasyTierProcess.Kill(entireProcessTree: true);
                _logger.LogInformation("EasyTier process killed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill EasyTier process (PID: {Pid})", _runtime.EasyTierProcess.Id);
            }
        }

        _runtime.ScaffoldingClient = null;
        _runtime.ScaffoldingServer = null;
        _runtime.FakeServer = null;
        _runtime.EasyTierProcess = null;

        _logger.LogInformation("RoomController cleanup completed");
    }
}
