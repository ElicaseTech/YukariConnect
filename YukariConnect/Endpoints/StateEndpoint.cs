namespace YukariConnect.Endpoints
{
    public static class StateEndpoint
    {
        public record StateResponse(string State);
        public static void Map(WebApplication app)
        {
            app.MapGet("/state", () =>
            {
                var payload = new StateResponse("ok");
                return TypedResults.Ok(payload);
            });
        }
    }
}
