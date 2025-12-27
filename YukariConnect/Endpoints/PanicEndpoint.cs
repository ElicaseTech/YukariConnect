namespace YukariConnect.Endpoints
{
    public static class PanicEndpoint
    {
        public record PanicResponse(string Status);

        public static void Map(WebApplication app)
        {
            app.MapGet("/panic", (bool peaceful) =>
            {
                // Terracotta behavior:
                // - If peaceful=true: Gracefully shutdown the application (exit code 0)
                // - If peaceful=false (default): Trigger a panic (crash)

                if (peaceful)
                {
                    // Graceful shutdown - exit the process like Terracotta
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100); // Give time for response to be sent
                        Environment.Exit(0);
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
