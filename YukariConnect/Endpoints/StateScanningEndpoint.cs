using Microsoft.AspNetCore.Http.HttpResults;

namespace YukariConnect.Endpoints
{
    public static class StateScanningEndpoint
    {
        public record StateScanningResponse(string State, string Room, string Player);
        public static void Map(WebApplication app)
        {
            app.MapGet("/state/scanning", Results<Ok<StateScanningResponse>, BadRequest> (string? room, string? player) =>
            {
                if (string.IsNullOrWhiteSpace(room) || string.IsNullOrWhiteSpace(player))
                {
                    return TypedResults.BadRequest();
                }
                var payload = new StateScanningResponse("scanning", room!, player!);
                return TypedResults.Ok(payload);
            });
        }
    }
}
