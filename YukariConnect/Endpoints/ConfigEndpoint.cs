using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Serialization;
using YukariConnect.Configuration;

namespace YukariConnect.Endpoints;

/// <summary>
/// Configuration management endpoints.
/// Allows runtime modification of YukariConnect settings.
/// </summary>
public static class ConfigEndpoint
{
    public record ConfigResponse(
        [property: JsonPropertyName("launcherCustomString")] string? LauncherCustomString
    );

    public record SetLauncherRequest(
        [property: JsonPropertyName("launcherCustomString")] string? LauncherCustomString
    );

    public record MessageResponse([property: JsonPropertyName("message")] string Message);

    public static void Map(WebApplication app)
    {
        var configApi = app.MapGroup("/config");

        // Get current configuration
        configApi.MapGet("/", GetConfig);

        // Set launcher custom string
        configApi.MapPost("/launcher", SetLauncher);
    }

    static IResult GetConfig(YukariOptions options)
    {
        return TypedResults.Ok(new ConfigResponse(
            LauncherCustomString: options.LauncherCustomString
        ));
    }

    static IResult SetLauncher(SetLauncherRequest request, YukariOptions options)
    {
        options.LauncherCustomString = request.LauncherCustomString;

        return TypedResults.Ok(new MessageResponse(
            $"Launcher custom string set to: {request.LauncherCustomString ?? "(null)"}"
        ));
    }
}
