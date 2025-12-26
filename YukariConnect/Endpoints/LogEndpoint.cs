namespace YukariConnect.Endpoints
{
    public static class LogEndpoint
    {
        public record LogResponse(bool Fetch, string[] Entries);
        public static void Map(WebApplication app)
        {
            app.MapGet("/log", (bool fetch) =>
            {
                var entries = fetch ? new[] { "no entries" } : Array.Empty<string>();
                var payload = new LogResponse(fetch, entries);
                return TypedResults.Ok(payload);
            });
        }
    }
}
