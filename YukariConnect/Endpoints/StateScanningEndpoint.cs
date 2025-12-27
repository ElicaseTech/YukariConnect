using YukariConnect.Scaffolding;

namespace YukariConnect.Endpoints
{
    public static class StateScanningEndpoint
    {
        public static void Map(WebApplication app)
        {
            app.MapGet("/state/scanning", async (string? room, string? player, RoomController roomController) =>
            {
                // room parameter is optional in Terracotta - if not provided, generate a new room code
                // player parameter is optional
                // NOTE: Terracotta does NOT support launcher/vendor customization via query params
                var playerName = string.IsNullOrWhiteSpace(player) ? "Host" : player!;

                // Start host mode
                await roomController.StartHostAsync(
                    scaffoldingPort: 13448,
                    playerName: playerName,
                    launcherCustomString: null  // Terracotta compatibility: no custom launcher
                );

                return Results.Ok();
            });
        }
    }
}
