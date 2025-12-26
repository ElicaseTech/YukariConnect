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

            // Register Minecraft LAN services
            builder.Services.AddSingleton<MinecraftLanState>();
            builder.Services.AddHostedService<MinecraftLanListener>();

            // Register Scaffolding services
            builder.Services.AddSingleton<RoomController>();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

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
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}
