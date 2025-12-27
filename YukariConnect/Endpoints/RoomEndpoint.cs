using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Serialization;
using YukariConnect.Scaffolding;

namespace YukariConnect.Endpoints;

public static class RoomEndpoint
{
    public record RoomStatusResponse(
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("roomCode")] string? RoomCode,
        [property: JsonPropertyName("players")] List<PlayerInfoDto> Players,
        [property: JsonPropertyName("minecraftPort")] int? MinecraftPort,
        [property: JsonPropertyName("lastUpdate")] DateTimeOffset LastUpdate
    );

    public record PlayerInfoDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("machineId")] string MachineId,
        [property: JsonPropertyName("vendor")] string Vendor,
        [property: JsonPropertyName("kind")] string Kind
    );

    public record StartHostRequest(
        [property: JsonPropertyName("scaffoldingPort")] ushort ScaffoldingPort = 13448,
        [property: JsonPropertyName("playerName")] string PlayerName = "Host",
        [property: JsonPropertyName("launcherCustomString")] string? LauncherCustomString = null
    );

    public record StartGuestRequest(
        [property: JsonPropertyName("roomCode")] string RoomCode,
        [property: JsonPropertyName("playerName")] string PlayerName = "Guest",
        [property: JsonPropertyName("launcherCustomString")] string? LauncherCustomString = null
    );

    public record MessageResponse([property: JsonPropertyName("message")] string Message);
    public record RoomErrorResponse([property: JsonPropertyName("error")] string Error);

    public static void Map(WebApplication app)
    {
        var roomApi = app.MapGroup("/room");

        roomApi.MapGet("/status", GetRoomStatus);
        roomApi.MapPost("/host/start", StartHost);
        roomApi.MapPost("/guest/start", StartGuest);
        roomApi.MapPost("/stop", StopRoom);
        roomApi.MapPost("/retry", RetryRoom);
    }

    static IResult GetRoomStatus(RoomController controller)
    {
        var status = controller.GetStatus();
        return TypedResults.Ok(new RoomStatusResponse(
            status.State.Value,
            status.Role?.Value,
            status.Error,
            status.RoomCode,
            status.Players.Select(p => new PlayerInfoDto(
                p.Name,
                p.MachineId,
                p.Vendor,
                p.Kind.Value
            )).ToList(),
            status.MinecraftPort,
            status.LastUpdate
        ));
    }

    static async Task<IResult> StartHost(StartHostRequest request, RoomController controller, CancellationToken ct)
    {
        try
        {
            await controller.StartHostAsync(
                request.ScaffoldingPort,
                request.PlayerName,
                request.LauncherCustomString,
                ct);

            return TypedResults.Ok(new MessageResponse("Host starting..."));
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new RoomErrorResponse(ex.Message));
        }
    }

    static async Task<IResult> StartGuest(StartGuestRequest request, RoomController controller, CancellationToken ct)
    {
        try
        {
            await controller.StartGuestAsync(
                request.RoomCode,
                request.PlayerName,
                request.LauncherCustomString,
                ct);

            return TypedResults.Ok(new MessageResponse("Guest joining..."));
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new RoomErrorResponse(ex.Message));
        }
    }

    static async Task<IResult> StopRoom(RoomController controller)
    {
        try
        {
            await controller.StopAsync();
            return TypedResults.Ok(new MessageResponse("Room stopped"));
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new RoomErrorResponse(ex.Message));
        }
    }

    static async Task<IResult> RetryRoom(RoomController controller)
    {
        try
        {
            await controller.RetryAsync();
            return TypedResults.Ok(new MessageResponse("Retrying from error state..."));
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new RoomErrorResponse(ex.Message));
        }
    }
}
