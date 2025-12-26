namespace YukariConnect.Endpoints
{
    public static class StateIdeEndpoint
    {
        public record StateIdeResponse(string Ide);
        public static void Map(WebApplication app)
        {
            app.MapGet("/state/ide", () =>
            {
                var payload = new StateIdeResponse("ready");
                return TypedResults.Ok(payload);
            });
        }
    }
}
