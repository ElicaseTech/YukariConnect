using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq;
using YukariConnect.Minecraft.Services;

namespace YukariConnect.Test;

public class CustomWebApplicationFactory : WebApplicationFactory<YukariConnect.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var toRemove = services
                .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MinecraftLanListener))
                .ToList();
            foreach (var d in toRemove)
                services.Remove(d);
        });
    }
}
