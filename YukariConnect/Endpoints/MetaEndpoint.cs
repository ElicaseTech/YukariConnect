using System.Runtime.InteropServices;
using System.Reflection;
using YukariConnect.Services;

namespace YukariConnect.Endpoints
{
    public static class MetaEndpoint
    {
        public record MetaResponse(
            string Version,
            string CompileTimestamp,
            string? EasyTierVersion,
            string YggdrasilPort,
            string TargetTuple,
            string TargetArch,
            string TargetVendor,
            string TargetOS,
            string? TargetEnv
        );

        public static void Map(WebApplication app)
        {
            app.MapGet("/meta", async (EasyTierCliService etService) =>
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
                var compileTime = GetCompileTime();
                var etVersion = await etService.GetVersionAsync(default);

                var targetTuple = $"{RuntimeInformation.ProcessArchitecture}-{RuntimeInformation.OSArchitecture}-{RuntimeInformation.OSDescription}";

                var payload = new MetaResponse(
                    Version: version,
                    CompileTimestamp: compileTime,
                    EasyTierVersion: etVersion,
                    YggdrasilPort: "13448",
                    TargetTuple: targetTuple,
                    TargetArch: RuntimeInformation.ProcessArchitecture.ToString(),
                    TargetVendor: RuntimeInformation.OSArchitecture.ToString(),
                    TargetOS: RuntimeInformation.OSDescription,
                    TargetEnv: RuntimeInformation.FrameworkDescription
                );
                return TypedResults.Ok(payload);
            });
        }

        private static string GetCompileTime()
        {
            // Get the link time (approximate compile time)
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(assemblyLocation))
                return DateTimeOffset.UtcNow.ToString("o");

            var assemblyPath = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrEmpty(assemblyPath))
                return DateTimeOffset.UtcNow.ToString("o");

            // Use the last write time of the main assembly as compile time approximation
            try
            {
                var fileInfo = new FileInfo(assemblyLocation);
                return fileInfo.LastWriteTimeUtc.ToString("o");
            }
            catch
            {
                return DateTimeOffset.UtcNow.ToString("o");
            }
        }
    }
}
