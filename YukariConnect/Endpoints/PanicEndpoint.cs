namespace YukariConnect.Endpoints
{
    public static class PanicEndpoint
    {
        public record PanicResponse(string Status);
        public static void Map(WebApplication app)
        {
            app.MapGet("/panic", (bool peaceful) =>
            {
                var payload = new PanicResponse(peaceful ? "peaceful" : "panic");
                return TypedResults.Ok(payload);
            });
        }
    }
}
