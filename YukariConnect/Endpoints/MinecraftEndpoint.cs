using System.Net;
using Microsoft.AspNetCore.Http.HttpResults;
using YukariConnect.Minecraft.Services;
using YukariConnect.Minecraft.Models;

namespace YukariConnect.Endpoints
{
    public static class MinecraftEndpoint
    {
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

        public static void Map(WebApplication app)
        {
            var mcApi = app.MapGroup("/minecraft");

            mcApi.MapGet("/servers", GetMinecraftServers);
            mcApi.MapGet("/servers/verified", GetVerifiedServers);
            mcApi.MapGet("/servers/{ip}", GetMinecraftServerByIp);
            mcApi.MapGet("/servers/search", SearchMinecraftServers);
            mcApi.MapGet("/status", GetMinecraftStatus);
        }

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

        static IResult GetMinecraftServerByIp(string ip, MinecraftLanState state)
        {
            if (!IPAddress.TryParse(ip, out var ipAddress))
            {
                return TypedResults.BadRequest();
            }

            var server = state.GetServer(ipAddress);
            if (server == null)
            {
                return TypedResults.NotFound();
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

        static IResult GetMinecraftStatus(MinecraftLanState state)
        {
            return TypedResults.Ok(new MinecraftStatusResponse(
                state.TotalCount,
                state.VerifiedCount,
                DateTimeOffset.UtcNow
            ));
        }
    }
}
