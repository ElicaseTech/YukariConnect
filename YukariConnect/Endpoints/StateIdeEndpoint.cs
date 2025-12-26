using YukariConnect.Scaffolding;

namespace YukariConnect.Endpoints
{
    public static class StateIdeEndpoint
    {
        public static void Map(WebApplication app)
        {
            app.MapGet("/state/ide", async (RoomController roomController) =>
            {
                // Reset to idle state (equivalent to Terracotta's set_waiting)
                await roomController.StopAsync();

                return Results.Ok();
            });
        }
    }
}
