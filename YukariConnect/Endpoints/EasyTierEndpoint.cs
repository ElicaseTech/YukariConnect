using Microsoft.AspNetCore.Http.HttpResults;
using YukariConnect.Services;

namespace YukariConnect.Endpoints;

public static class EasyTierEndpoint
{
    public record PublicServersResponse(string[] Servers);

    public static void Map(WebApplication app)
    {
        var etApi = app.MapGroup("/easytier");

        etApi.MapGet("/node", GetNode);
        etApi.MapGet("/peers", GetPeers);
        etApi.MapGet("/routes", GetRoutes);
        etApi.MapGet("/stats", GetStats);
        etApi.MapGet("/public-servers", GetPublicServers);
    }

    static async Task<IResult> GetNode(EasyTierCliService cli, CancellationToken ct)
    {
        var result = await cli.NodeAsync(ct);
        if (result == null)
            return TypedResults.NotFound(new { error = "EasyTier not available" });

        return TypedResults.Ok(result);
    }

    static async Task<IResult> GetPeers(EasyTierCliService cli, CancellationToken ct)
    {
        var result = await cli.PeersAsync(ct);
        if (result == null)
            return TypedResults.NotFound(new { error = "EasyTier not available" });

        return TypedResults.Ok(result);
    }

    static async Task<IResult> GetRoutes(EasyTierCliService cli, CancellationToken ct)
    {
        var result = await cli.RoutesAsync(ct);
        if (result == null)
            return TypedResults.NotFound(new { error = "EasyTier not available" });

        return TypedResults.Ok(result);
    }

    static async Task<IResult> GetStats(EasyTierCliService cli, CancellationToken ct)
    {
        var result = await cli.StatsAsync(ct);
        if (result == null)
            return TypedResults.NotFound(new { error = "EasyTier not available" });

        return TypedResults.Ok(result);
    }

    static IResult GetPublicServers(PublicServersService publicServers)
    {
        var servers = publicServers.GetServers();
        return TypedResults.Ok(new PublicServersResponse(servers));
    }
}
