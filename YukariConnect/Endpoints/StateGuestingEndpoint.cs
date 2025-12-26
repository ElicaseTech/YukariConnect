using YukariConnect.Scaffolding;

namespace YukariConnect.Endpoints
{
    public static class StateGuestingEndpoint
    {
        public static void Map(WebApplication app)
        {
            app.MapGet("/state/guesting", async (string? room, string? player, RoomController roomController) =>
            {
                // room parameter is REQUIRED in Terracotta - must be provided
                if (string.IsNullOrWhiteSpace(room))
                {
                    return Results.BadRequest();
                }

                // player parameter is optional
                var playerName = string.IsNullOrWhiteSpace(player) ? "Guest" : player!;

                // Start guest mode
                try
                {
                    await roomController.StartGuestAsync(
                        roomCode: room!,
                        playerName: playerName,
                        launcherCustomString: null
                    );

                    return Results.Ok();
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest();
                }
            });
        }
    }
}
