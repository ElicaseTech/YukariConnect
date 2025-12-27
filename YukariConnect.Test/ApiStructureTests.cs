using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Xunit;

namespace YukariConnect.Test;

public class ApiStructureTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private static bool HasProperty(JsonElement root, string name)
    {
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
    private static string? GetString(JsonElement root, string name)
    {
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value.GetString();
        }
        return null;
    }

    public ApiStructureTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Root_Redirects_To_Index()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var resp = await client.GetAsync("/");
        Assert.InRange((int)resp.StatusCode, 300, 399);
        Assert.Equal("/index.html", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Meta_Endpoint_Has_Expected_Fields()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/meta");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(HasProperty(root, "Version"));
        Assert.True(HasProperty(root, "CompileTimestamp"));
        Assert.True(HasProperty(root, "EasyTierVersion"));
        Assert.True(HasProperty(root, "YggdrasilPort"));
        Assert.True(HasProperty(root, "TargetTuple"));
        Assert.True(HasProperty(root, "TargetArch"));
        Assert.True(HasProperty(root, "TargetVendor"));
        Assert.True(HasProperty(root, "TargetOS"));
    }

    [Fact]
    public async Task State_Endpoint_Returns_Waiting_State()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/state");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var state = GetString(root, "State");
        Assert.Equal("waiting", state);
    }

    [Fact]
    public async Task Config_Endpoint_Returns_Config_Object()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/config");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(HasProperty(root, "launcherCustomString"));
    }

    [Fact]
    public async Task EasyTier_PublicServers_Returns_List()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/easytier/public-servers");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(HasProperty(root, "Servers"));
        var servers = root.EnumerateObject().First(p => string.Equals(p.Name, "Servers", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.Equal(JsonValueKind.Array, servers.ValueKind);
        Assert.True(servers.GetArrayLength() >= 1);
    }
}
