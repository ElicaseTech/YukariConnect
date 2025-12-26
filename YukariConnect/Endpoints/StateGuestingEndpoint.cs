using Microsoft.AspNetCore.Http.HttpResults;

namespace YukariConnect.Endpoints
{
    public static class StateGuestingEndpoint
    {
        public record StateGuestingResponse(string State, string Room, string Player);
        public static void Map(WebApplication app)
        {
            app.MapGet("/state/guesting", Results<Ok<StateGuestingResponse>, BadRequest> (string? room, string? player) =>
            {
                if (string.IsNullOrWhiteSpace(room) || string.IsNullOrWhiteSpace(player))
                {
                    return TypedResults.BadRequest();
                }
                var payload = new StateGuestingResponse("guesting", room!, player!);
                return TypedResults.Ok(payload);
            });
        }
    }
}
