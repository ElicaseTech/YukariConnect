using Microsoft.AspNetCore.Http.HttpResults;
using YukariConnect.Scaffolding;

namespace YukariConnect.Endpoints;

public static class RoomEndpoint
{
    public record RoomStatusResponse(
        string State,
        string? Role,
        string? Error,
        string? RoomCode,
        List<PlayerInfoDto> Players,
        int? MinecraftPort,
        DateTimeOffset LastUpdate
    );

    public record PlayerInfoDto(
        string Name,
        string MachineId,
        string Vendor,
        string Kind
    );

    public record StartHostRequest(
        string? RoomCode,
        string NetworkName,
        string NetworkSecret,
        ushort ScaffoldingPort = 13448,
        string PlayerName = "Host"
    );

    public record StartGuestRequest(
        string RoomCode,
        string PlayerName = "Guest"
    );

    public static void Map(WebApplication app)
    {
        var roomApi = app.MapGroup("/room");

        roomApi.MapGet("/status", GetRoomStatus);
        roomApi.MapPost("/host/start", StartHost);
        roomApi.MapPost("/guest/start", StartGuest);
        roomApi.MapPost("/stop", StopRoom);
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
                request.RoomCode,
                request.NetworkName,
                request.NetworkSecret,
                request.ScaffoldingPort,
                request.PlayerName,
                ct);

            return TypedResults.Ok(new { message = "Host starting..." });
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new { error = ex.Message });
        }
    }

    static async Task<IResult> StartGuest(StartGuestRequest request, RoomController controller, CancellationToken ct)
    {
        try
        {
            await controller.StartGuestAsync(
                request.RoomCode,
                request.PlayerName,
                ct);

            return TypedResults.Ok(new { message = "Guest joining..." });
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new { error = ex.Message });
        }
    }

    static async Task<IResult> StopRoom(RoomController controller)
    {
        try
        {
            await controller.StopAsync();
            return TypedResults.Ok(new { message = "Room stopped" });
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new { error = ex.Message });
        }
    }
}
