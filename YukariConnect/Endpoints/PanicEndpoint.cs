namespace YukariConnect.Endpoints
{
    public static class PanicEndpoint
    {
        public record PanicResponse(string Status);

        public static void Map(WebApplication app)
        {
            app.MapGet("/panic", (bool peaceful, IHostApplicationLifetime lifetime) =>
            {
                // Terracotta behavior:
                // - If peaceful=true: Gracefully shutdown the application
                // - If peaceful=false (default): Trigger a panic (crash)

                if (peaceful)
                {
                    // Graceful shutdown
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100); // Give time for response to be sent
                        lifetime.StopApplication();
                    });
                    return TypedResults.Ok(new PanicResponse("shutting down"));
                }
                else
                {
                    // Trigger a panic (crash the application)
                    _ = Task.Run(() =>
                    {
                        // Throw an unhandled exception to crash the app
                        throw new Exception("Panic triggered via /panic endpoint");
                    });
                    return TypedResults.Ok(new PanicResponse("panicking"));
                }
            });
        }
    }
}
