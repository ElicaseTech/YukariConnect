namespace YukariConnect.Endpoints
{
    public static class LogEndpoint
    {
        public static void Map(WebApplication app)
        {
            app.MapGet("/log", (bool fetch = false) =>
            {
                // Terracotta behavior:
                // - If fetch=false (default): On macOS, open the log file location in Finder
                // - If fetch=true: Return the log file content for download

                if (!fetch)
                {
                    // On non-macOS or when fetch is false, we can't open a folder browser via HTTP
                    // Just return NoContent to indicate the action was acknowledged
                    // In a desktop app environment, this might trigger opening the file location
                    return Results.NoContent();
                }

                // If fetch=true, try to return the log file
                // For now, return the log location info
                // In production, this should stream the actual log file
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "YukariConnect"
                );

                if (!Directory.Exists(logDir))
                {
                    return Results.NotFound();
                }

                var logFiles = Directory.GetFiles(logDir, "*.log")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(logFiles) || !File.Exists(logFiles))
                {
                    return Results.NotFound();
                }

                try
                {
                    var fileContent = File.ReadAllText(logFiles);
                    return Results.Text(fileContent, "text/plain");
                }
                catch
                {
                    return Results.StatusCode(500);
                }
            });
        }
    }
}
