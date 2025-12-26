using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace YukariConnect.Services
{
    public class EasyTierResourceInitializer : IHostedService
    {
        private readonly IHostEnvironment _env;
        private readonly ILogger<EasyTierResourceInitializer> _logger;
        public EasyTierResourceInitializer(IHostEnvironment env, ILogger<EasyTierResourceInitializer> logger)
        {
            _env = env;
            _logger = logger;
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var resourceDir = Path.Combine(_env.ContentRootPath, "resource");
            var coreExe = Path.Combine(resourceDir, "easytier-core.exe");
            if (File.Exists(coreExe)) return;
            Directory.CreateDirectory(resourceDir);
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("YukariConnect/1.0");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                var latestJson = await http.GetStringAsync("https://api.github.com/repos/EasyTier/EasyTier/releases/latest", cancellationToken);
                using var doc = JsonDocument.Parse(latestJson);
                var root = doc.RootElement;
                var assets = root.GetProperty("assets");
                string? downloadUrl = null;
                string? assetName = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    var url = asset.GetProperty("browser_download_url").GetString();
                    if (name != null && url != null && name.Contains("windows", StringComparison.OrdinalIgnoreCase) && name.Contains("x86_64", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = url;
                        assetName = name;
                        break;
                    }
                }
                if (downloadUrl == null)
                {
                    _logger.LogWarning("EasyTier release asset not found for windows x86_64 zip");
                    return;
                }
                var zipPath = Path.Combine(resourceDir, assetName!);
                using (var resp = await http.GetAsync(downloadUrl, cancellationToken))
                {
                    resp.EnsureSuccessStatusCode();
                    await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await resp.Content.CopyToAsync(fs, cancellationToken);
                }
                using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        var destPath = Path.Combine(resourceDir, entry.Name);
                        entry.ExtractToFile(destPath, true);
                    }
                }
                if (File.Exists(coreExe))
                {
                    _logger.LogInformation("EasyTier core prepared at {Path}", coreExe);
                }
                else
                {
                    var found = Directory.GetFiles(resourceDir, "easytier-core.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null && !string.Equals(found, coreExe, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(found, coreExe, true);
                    }
                    if (File.Exists(coreExe))
                    {
                        _logger.LogInformation("EasyTier core prepared at {Path}", coreExe);
                    }
                    else
                    {
                        _logger.LogWarning("EasyTier core not found after extraction");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EasyTier resource initialization failed");
            }
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
