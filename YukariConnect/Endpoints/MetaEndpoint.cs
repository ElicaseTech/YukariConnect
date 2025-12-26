namespace YukariConnect.Endpoints
{
    public static class MetaEndpoint
    {
        public record MetaResponse(string Name, string Version, DateTimeOffset Time);
        public static void Map(WebApplication app)
        {
            app.MapGet("/meta", () =>
            {
                var version = typeof(YukariConnect.Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
                var payload = new MetaResponse("YukariConnect", version, DateTimeOffset.UtcNow);
                return TypedResults.Ok(payload);
            });
        }
    }
}
