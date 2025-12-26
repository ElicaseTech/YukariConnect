using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using YukariConnect.Minecraft.Models;
using YukariConnect.Minecraft.Services;
using YukariConnect.Endpoints;

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
                .WithName("GetMinecraftServers");

            mcApi.MapGet("/servers/verified", GetVerifiedServers)
                .WithName("GetVerifiedServers");

            mcApi.MapGet("/servers/{ip}", GetMinecraftServerByIp)
                .WithName("GetMinecraftServerByIp");

            mcApi.MapGet("/servers/search", SearchMinecraftServers)
                .WithName("SearchMinecraftServers");

            mcApi.MapGet("/status", GetMinecraftStatus)
                .WithName("GetMinecraftStatus");

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
        /// Get all discovered Minecraft servers (including unverified).
        /// </summary>
        static IResult GetMinecraftServers(MinecraftLanState state)
        {
            return TypedResults.Ok(new MinecraftServerListResponse(
                state.AllServers.Select(s => new MinecraftServerDto(
                    s.EndPoint.ToString(),
                    s.Motd,
                    s.IsVerified,
                    s.PingResult?.Version,
                    s.PingResult?.OnlinePlayers,
                    s.PingResult?.MaxPlayers
                )).ToList(),
                state.TotalCount
            ));
        }

        /// <summary>
        /// Get only verified Minecraft servers (successfully pinged).
        /// </summary>
        static IResult GetVerifiedServers(MinecraftLanState state)
        {
            return TypedResults.Ok(new MinecraftServerListResponse(
                state.VerifiedServers.Select(s => new MinecraftServerDto(
                    s.EndPoint.ToString(),
                    s.Motd,
                    s.IsVerified,
                    s.PingResult?.Version,
                    s.PingResult?.OnlinePlayers,
                    s.PingResult?.MaxPlayers
                )).ToList(),
                state.VerifiedCount
            ));
        }

        /// <summary>
        /// Get a specific Minecraft server by IP address.
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

            return TypedResults.Ok(new MinecraftServerDto(
                server.EndPoint.ToString(),
                server.Motd,
                server.IsVerified,
                server.PingResult?.Version,
                server.PingResult?.OnlinePlayers,
                server.PingResult?.MaxPlayers
            ));
        }

        /// <summary>
        /// Search for Minecraft servers by MOTD pattern.
        /// </summary>
        static IResult SearchMinecraftServers(string? pattern, MinecraftLanState state)
        {
            var servers = string.IsNullOrWhiteSpace(pattern)
                ? state.AllServers
                : state.FindServersByMotdPattern(pattern);

            return TypedResults.Ok(new MinecraftServerListResponse(
                servers.Select(s => new MinecraftServerDto(
                    s.EndPoint.ToString(),
                    s.Motd,
                    s.IsVerified,
                    s.PingResult?.Version,
                    s.PingResult?.OnlinePlayers,
                    s.PingResult?.MaxPlayers
                )).ToList(),
                servers.Count
            ));
        }

        /// <summary>
        /// Get overall Minecraft LAN monitoring status.
        /// </summary>
        static IResult GetMinecraftStatus(MinecraftLanState state)
        {
            return TypedResults.Ok(new MinecraftStatusResponse(
                state.TotalCount,
                state.VerifiedCount,
                DateTimeOffset.UtcNow
            ));
        }
    }

    // Records for API responses

    public record MinecraftServerDto(
        string EndPoint,
        string Motd,
        bool IsVerified,
        string? Version,
        int? OnlinePlayers,
        int? MaxPlayers
    );

    public record MinecraftServerListResponse(
        List<MinecraftServerDto> Servers,
        int Count
    );

    public record MinecraftStatusResponse(
        int TotalServers,
        int VerifiedServers,
        DateTimeOffset Timestamp
    );

    public record ErrorResponse(string Message);

    // Original Todo record (can be removed later)
    public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

    [JsonSerializable(typeof(MinecraftServerListResponse))]
    [JsonSerializable(typeof(MinecraftServerDto))]
    [JsonSerializable(typeof(MinecraftServerDto[]))]
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
