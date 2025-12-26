using System.Text.Json.Serialization;
using YukariConnect.Endpoints;
using YukariConnect.Services;
using YukariConnect.Minecraft.Services;
using YukariConnect.Scaffolding;

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

            builder.Services.AddOpenApi();
            builder.Services.AddHostedService<EasyTierResourceInitializer>();
            builder.Services.AddSingleton<EasyTierCliService>();
            builder.Services.AddSingleton<PublicServersService>();

            // Register Minecraft LAN services
            builder.Services.AddSingleton<MinecraftLanState>();
            builder.Services.AddHostedService<MinecraftLanListener>();

            // Register Scaffolding services
            builder.Services.AddSingleton<RoomController>();

            var app = builder.Build();

            // Register shutdown handler to clean up RoomController
            app.Lifetime.ApplicationStopping.Register(() =>
            {
                Console.WriteLine("[Shutdown] Cleaning up RoomController...");
                var roomController = app.Services.GetService<RoomController>();
                if (roomController != null)
                {
                    Console.WriteLine("[Shutdown] Disposing RoomController...");
                    roomController.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    Console.WriteLine("[Shutdown] RoomController disposed");
                }
            });

            app.Lifetime.ApplicationStopped.Register(() =>
            {
                Console.WriteLine("[Shutdown] Application stopped");
            });

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            // Enable static files for web console
            app.UseFileServer();
            // Redirect root to index.html
            app.MapGet("/", () => Results.Redirect("/index.html"));

            // Map all endpoints
            MetaEndpoint.Map(app);
            StateEndpoint.Map(app);
            StateIdeEndpoint.Map(app);
            StateScanningEndpoint.Map(app);
            StateGuestingEndpoint.Map(app);
            LogEndpoint.Map(app);
            PanicEndpoint.Map(app);
            MinecraftEndpoint.Map(app);
            RoomEndpoint.Map(app);
            EasyTierEndpoint.Map(app);

            app.Run();
        }
    }

    [JsonSerializable(typeof(YukariConnect.Endpoints.MetaEndpoint.MetaResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.StateEndpoint.StateResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.StateIdeEndpoint.StateIdeResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.StateScanningEndpoint.StateScanningResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.StateGuestingEndpoint.StateGuestingResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.LogEndpoint.LogResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.PanicEndpoint.PanicResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.MinecraftEndpoint.MinecraftServerListResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.MinecraftEndpoint.MinecraftServerDto))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.MinecraftEndpoint.MinecraftStatusResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.RoomStatusResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.PlayerInfoDto))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.StartHostRequest))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.StartGuestRequest))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.MessageResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.ErrorResponse))]
    [JsonSerializable(typeof(YukariConnect.Scaffolding.Models.PlayerPingRequest))]
    [JsonSerializable(typeof(YukariConnect.Scaffolding.Models.ScaffoldingProfile))]
    [JsonSerializable(typeof(List<YukariConnect.Scaffolding.Models.ScaffoldingProfile>))]
    public partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}
