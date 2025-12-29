using System.Text.Json.Serialization;
using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using YukariConnect.Endpoints;
using YukariConnect.Services;
using YukariConnect.Minecraft.Services;
using YukariConnect.Scaffolding;
using YukariConnect.Configuration;
using YukariConnect.Logging;
using Serilog;

namespace YukariConnect
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Load configuration file (yukari.json)
            var options = YukariConfiguration.LoadOrCreate();

            var builder = WebApplication.CreateSlimBuilder(args);

            builder.Services.Configure<HostOptions>(o =>
            {
                o.ShutdownTimeout = TimeSpan.FromSeconds(5);
            });

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            //builder.Services.AddOpenApi();
            builder.Services.AddEndpointsApiExplorer();

            // TODO: Swagger with AOT requires additional configuration
            // For now, we rely on the built-in OpenAPI JSON endpoint

            // Register EasyTier infrastructure services
            // Note: EasyTierResourceInitializer is NOT registered as IHostedService here
            // because we need to start it manually AFTER WebSocket logging is ready
            builder.Services.AddSingleton<EasyTierResourceInitializer>();
            builder.Services.AddSingleton<EasyTierCliService>();
            builder.Services.AddSingleton<PublicServersService>();

            // Register WebSocket infrastructure
            builder.Services.AddSingleton<YukariConnect.WebSocket.IWebSocketManager, YukariConnect.WebSocket.WebSocketManager>();
            builder.Services.AddSingleton<Logging.ILogBroadcaster, Logging.LogBroadcaster>();

            // Register Network abstraction layer
            builder.Services.AddSingleton<Network.INetworkNode, Network.EasyTierNetworkNode>();
            builder.Services.AddSingleton<Network.IPeerDiscoveryService, Network.EasyTierPeerDiscoveryService>();
            builder.Services.AddSingleton<Network.INetworkProcess, Network.EasyTierNetworkProcess>();

            // Register Minecraft LAN services
            builder.Services.AddSingleton<MinecraftLanState>();
            builder.Services.AddHostedService<MinecraftLanListener>();

            // Register configuration
            builder.Services.AddSingleton(options);

            // Register Scaffolding services
            builder.Services.AddSingleton<RoomController>();

            // Register Serilog logger and LogService
            var serilogLogger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Type}][{Component}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
            var logService = new LogService(serilogLogger);
            builder.Services.AddSingleton<Serilog.ILogger>(serilogLogger);
            builder.Services.AddSingleton<ILogService>(logService);

            // Configure logging using standard ASP.NET Core approach
            builder.Logging.ClearProviders();

            // Add configuration from appsettings.json BEFORE adding providers
            builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

            // Register LogService logger provider (non-deferred to capture early hosting logs)
            builder.Logging.AddProvider(new LogServiceLoggerProvider(logService));

            // Register WebSocket logger as a provider that will be added later
            // We need to defer this because ILogBroadcaster requires DI container
            var wsLoggerProvider = new DeferredWebSocketLoggerProvider();
            builder.Logging.AddProvider(wsLoggerProvider);
            builder.Services.AddSingleton(wsLoggerProvider);

            var app = builder.Build();

            // Initialize the deferred WebSocket logging provider now that DI container is ready
            var deferredWsProvider = app.Services.GetRequiredService<DeferredWebSocketLoggerProvider>();
            deferredWsProvider.Initialize(app.Services);

            // Enable WebSocket support (must be before other middleware)
            app.UseWebSockets();

            // Log WebSocket initialization
            var loggerFactory = app.Services.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger<Program>();
            logger?.LogInformation("WebSocket infrastructure initialized");

            // Start EasyTier resource initialization AFTER WebSocket logging is ready
            var easyTierInitializer = app.Services.GetRequiredService<EasyTierResourceInitializer>();
            _ = Task.Run(async () =>
            {
                try
                {
                    await easyTierInitializer.StartAsync(app.Lifetime.ApplicationStopping);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "EasyTier resource initialization failed");
                }
            });

            // Configure embedded file provider for wwwroot
            // This allows serving static files from embedded resources
            var assembly = Assembly.GetExecutingAssembly();
            var embeddedProvider = new EmbeddedFileProvider(assembly, "YukariConnect.wwwroot");

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = embeddedProvider,
                RequestPath = ""
            });

            // Fallback to physical files for development
            if (app.Environment.IsDevelopment())
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    RequestPath = ""
                });
            }

            // Initialize ApplicationLogging for static contexts
            YukariConnect.Network.ApplicationLogging.Configure(app.Services.GetRequiredService<ILoggerFactory>());

            // Find an available port if default is not available
            EnsureAvailablePort(app, options.HttpPort);

            // Log startup info with port information in machine-readable format
            LogStartupInfo(app);

            app.Lifetime.ApplicationStopping.Register(() =>
            {
                Console.WriteLine("[Shutdown] Cleaning up RoomController...");
                var roomController = app.Services.GetService<RoomController>();
                if (roomController != null)
                {
                    Console.WriteLine("[Shutdown] Stopping RoomController asynchronously...");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await roomController.StopAsync();
                            Console.WriteLine("[Shutdown] RoomController stopped");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Shutdown] RoomController stop error: {ex.Message}");
                        }
                    });
                }
            });

            app.Lifetime.ApplicationStopped.Register(() =>
            {
                Console.WriteLine("[Shutdown] Application stopped");
                YukariConnect.Network.ChildProcessManager.Cleanup();
            });

            //if (app.Environment.IsDevelopment())
            //{
            //    app.MapOpenApi();
            //}

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
            LogStreamEndpoint.Map(app);
            WebSocketApiEndpoint.Map(app);
            PanicEndpoint.Map(app);
            MinecraftEndpoint.Map(app);
            RoomEndpoint.Map(app);
            EasyTierEndpoint.Map(app);
            ConfigEndpoint.Map(app);

            app.Run();
        }

        /// <summary>
        /// Ensures the application is listening on an available port.
        /// If the default port is unavailable, finds an available port dynamically.
        /// </summary>
        private static void EnsureAvailablePort(WebApplication app, int preferredPort)
        {
            var urls = app.Urls.FirstOrDefault();
            if (string.IsNullOrEmpty(urls))
            {
                // No URLs configured, use preferred port from config
                var port = GetAvailablePort(preferredPort);
                app.Urls.Clear();
                app.Urls.Add($"http://localhost:{port}");
                return;
            }

            // Check if current URL is accessible
            if (urls.Contains("://"))
            {
                try
                {
                    var uri = new Uri(urls.Replace("+", "localhost"));
                    if (!IsPortAvailable(uri.Port))
                    {
                        // Port not available, find a new one
                        var newPort = GetAvailablePort(uri.Port);
                        app.Urls.Clear();
                        app.Urls.Add($"http://localhost:{newPort}");
                    }
                }
                catch
                {
                    // Invalid URI, find a new port
                    var port = GetAvailablePort(preferredPort);
                    app.Urls.Clear();
                    app.Urls.Add($"http://localhost:{port}");
                }
            }
        }

        /// <summary>
        /// Gets an available port starting from the preferred port.
        /// </summary>
        private static int GetAvailablePort(int preferredPort)
        {
            const int maxAttempts = 100;
            for (int i = 0; i < maxAttempts; i++)
            {
                int port = preferredPort + i;
                if (IsPortAvailable(port))
                    return port;
            }
            // If all else fails, use port 0 to let OS assign
            return 0;
        }

        /// <summary>
        /// Checks if a port is available for binding.
        /// </summary>
        private static bool IsPortAvailable(int port)
        {
            if (port <= 0) return false;

            try
            {
                using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Logs startup information with port details in machine-readable format.
        /// Format: YUKARI_PORT_INFO:port={port}
        /// </summary>
        private static void LogStartupInfo(WebApplication app)
        {
            // Get the actual listening URLs
            var urls = app.Urls.FirstOrDefault() ?? "unknown";

            // Try to extract port from URL
            int port = 0;
            if (urls.Contains("://"))
            {
                var uri = new Uri(urls.Replace("+", "localhost"));
                port = uri.Port;
            }

            // Log in machine-readable format using Microsoft.Extensions.Logging
            var logger = app.Services.GetService<Microsoft.Extensions.Logging.ILogger<Program>>();
            if (logger != null)
            {
                // Machine-readable format for other software to parse
                // Format: YUKARI_PORT_INFO:port={port}
                logger.LogInformation("YUKARI_PORT_INFO:port={Port}", port);
            }
            else
            {
                // Fallback to Console
                Console.WriteLine($"YUKARI_PORT_INFO:port={port}");
            }
        }
    }

    [JsonSerializable(typeof(YukariConnect.Endpoints.MetaEndpoint.MetaResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.StateEndpoint.StateResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.PanicEndpoint.PanicResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.EasyTierEndpoint.PublicServersResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.MinecraftEndpoint.MinecraftServerListResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.MinecraftEndpoint.MinecraftServerDto))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.MinecraftEndpoint.MinecraftStatusResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.MinecraftEndpoint.ErrorResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.RoomStatusResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.PlayerInfoDto))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.StartHostRequest))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.StartGuestRequest))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.MessageResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.RoomEndpoint.RoomErrorResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.ConfigEndpoint.ConfigResponse))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.ConfigEndpoint.SetLauncherRequest))]
    [JsonSerializable(typeof(YukariConnect.Endpoints.ConfigEndpoint.ConfigMessageResponse))]
    [JsonSerializable(typeof(YukariConnect.Logging.LogEntry))]
    [JsonSerializable(typeof(YukariConnect.WebSocket.WsMessage))]
    [JsonSerializable(typeof(YukariConnect.Scaffolding.Models.PlayerPingRequest))]
    [JsonSerializable(typeof(YukariConnect.Scaffolding.Models.ScaffoldingProfile))]
    [JsonSerializable(typeof(List<YukariConnect.Scaffolding.Models.ScaffoldingProfile>))]
    [JsonSerializable(typeof(YukariConnect.Configuration.YukariOptions))]
    public partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}
