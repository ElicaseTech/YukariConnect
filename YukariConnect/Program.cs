using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using YukariConnect.Minecraft.Models;
using YukariConnect.Minecraft.Services;
using YukariConnect.Endpoints;
using YukariConnect.Services;

namespace YukariConnect
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateSlimBuilder(args);

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();
            builder.Services.AddHostedService<EasyTierResourceInitializer>();

            // Register Minecraft LAN services
            builder.Services.AddSingleton<MinecraftLanState>();
            builder.Services.AddHostedService<MinecraftLanListener>();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            // Minecraft LAN endpoints
            var mcApi = app.MapGroup("/minecraft");
            mcApi.MapGet("/servers", GetMinecraftServers)
                .WithName("GetMinecraftServers")
                .WithOpenApi();

            mcApi.MapGet("/servers/{ip}", GetMinecraftServerByIp)
                .WithName("GetMinecraftServerByIp")
                .WithOpenApi();

            mcApi.MapGet("/servers/search", SearchMinecraftServers)
                .WithName("SearchMinecraftServers")
                .WithOpenApi();

            mcApi.MapGet("/status", GetMinecraftStatus)
                .WithName("GetMinecraftStatus")
                .WithOpenApi();

            // Original Todo endpoints (can be removed later)
            var todosApi = app.MapGroup("/todos");
            todosApi.MapGet("/", () => new Todo[0])
                    .WithName("GetTodos");

            todosApi.MapGet("/{id}", Results<Ok<Todo>, NotFound> (int id) =>
                TypedResults.NotFound())
                .WithName("GetTodoById");

            MetaEndpoint.Map(app);
            StateEndpoint.Map(app);
            StateIdeEndpoint.Map(app);
            StateScanningEndpoint.Map(app);
            StateGuestingEndpoint.Map(app);
            LogEndpoint.Map(app);
            PanicEndpoint.Map(app);

            app.Run();
        }

        /// <summary>
        /// Get all currently online Minecraft LAN servers.
        /// </summary>
        static IResult GetMinecraftServers(MinecraftLanState state)
        {
            return TypedResults.Ok(new MinecraftServerListResponse(
                state.OnlineServers.ToList(),
                state.OnlineCount
            ));
        }

        /// <summary>
        /// Get a specific Minecraft LAN server by IP address.
        /// </summary>
        static IResult GetMinecraftServerByIp(string ip, MinecraftLanState state)
        {
            if (!IPAddress.TryParse(ip, out var ipAddress))
            {
                return TypedResults.BadRequest(new ErrorResponse("Invalid IP address format"));
            }

            var server = state.GetServer(ipAddress);
            if (server == null)
            {
                return TypedResults.NotFound(new ErrorResponse($"Server at {ip} not found or offline"));
            }

            return TypedResults.Ok(server);
        }

        /// <summary>
        /// Search for Minecraft LAN servers by MOTD pattern.
        /// Useful for filtering Scaffolding rooms (e.g., ?pattern=Scaffolding).
        /// </summary>
        static IResult SearchMinecraftServers(string? pattern, MinecraftLanState state)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return TypedResults.Ok(new MinecraftServerListResponse(
                    state.OnlineServers.ToList(),
                    state.OnlineCount
                ));
            }

            var results = state.FindServersByMotdPattern(pattern);
            return TypedResults.Ok(new MinecraftServerListResponse(
                results.ToList(),
                results.Count
            ));
        }

        /// <summary>
        /// Get overall Minecraft LAN monitoring status.
        /// </summary>
        static IResult GetMinecraftStatus(MinecraftLanState state)
        {
            return TypedResults.Ok(new MinecraftStatusResponse(
                state.OnlineCount,
                state.OnlineServers.Any(s => s.IsLocalHost),
                DateTimeOffset.UtcNow
            ));
        }
    }

    // Records for API responses

    public record MinecraftServerListResponse(
        List<MinecraftLanAnnounce> Servers,
        int Count
    );

    public record MinecraftStatusResponse(
        int OnlineCount,
        bool HasLocalServer,
        DateTimeOffset Timestamp
    );

    public record ErrorResponse(string Message);

    // Original Todo record (can be removed later)
    public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

    [JsonSerializable(typeof(MinecraftServerListResponse))]
    [JsonSerializable(typeof(MinecraftLanAnnounce))]
    [JsonSerializable(typeof(MinecraftLanAnnounce[]))]
    [JsonSerializable(typeof(MinecraftStatusResponse))]
    [JsonSerializable(typeof(ErrorResponse))]
    [JsonSerializable(typeof(Todo[]))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.MetaEndpoint.MetaResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.StateEndpoint.StateResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.StateIdeEndpoint.StateIdeResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.StateScanningEndpoint.StateScanningResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.StateGuestingEndpoint.StateGuestingResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.LogEndpoint.LogResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.PanicEndpoint.PanicResponse))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}
