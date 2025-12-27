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
                // NOTE: Terracotta does NOT support launcher/vendor customization via query params
                var playerName = string.IsNullOrWhiteSpace(player) ? "Guest" : player!;

                // Start guest mode
                try
                {
                    await roomController.StartGuestAsync(
                        roomCode: room!,
                        playerName: playerName,
                        launcherCustomString: null  // Terracotta compatibility: no custom launcher
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
