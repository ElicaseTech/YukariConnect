using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Formats.Tar;

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

            // Use a separate timeout token instead of the app stopping token
            using var internalCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, internalCts.Token);

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("YukariConnect/1.0");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                http.Timeout = TimeSpan.FromMinutes(10);

                _logger.LogInformation("Fetching EasyTier release info...");
                var latestJson = await http.GetStringAsync("https://api.github.com/repos/EasyTier/EasyTier/releases/latest", linkedCts.Token);
                using var doc = JsonDocument.Parse(latestJson);
                var root = doc.RootElement;
                var assets = root.GetProperty("assets");
                string? downloadUrl = null;
                string? assetName = null;
                string osToken;
                if (OperatingSystem.IsWindows()) osToken = "windows";
                else if (OperatingSystem.IsLinux()) osToken = "linux";
                else if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()) osToken = "macos";
                else
                {
                    _logger.LogWarning("Unsupported OS platform");
                    return;
                }
                string? archToken = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "x86_64",
                    Architecture.X86 when OperatingSystem.IsWindows() => "i686",
                    Architecture.Arm64 when OperatingSystem.IsWindows() => "arm64",
                    Architecture.Arm64 when OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() => "aarch64",
                    _ => null
                };
                if (archToken is null)
                {
                    _logger.LogWarning("Unsupported architecture for {OS}", osToken);
                    return;
                }
                var token = $"easytier-{osToken}-{archToken}";
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    var url = asset.GetProperty("browser_download_url").GetString();
                    if (name != null && url != null && name.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                        (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                         name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                         name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)))
                    {
                        downloadUrl = url;
                        assetName = name;
                        break;
                    }
                }
                if (downloadUrl == null)
                {
                    _logger.LogWarning("EasyTier asset not found for {OS}-{ARCH}", osToken, archToken);
                    return;
                }
                var zipPath = Path.Combine(resourceDir, assetName!);
                _logger.LogInformation("Downloading EasyTier from {Url}...", downloadUrl);
                using (var resp = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token))
                {
                    resp.EnsureSuccessStatusCode();
                    var totalBytes = resp.Content.Headers.ContentLength ?? 0;
                    var totalMB = totalBytes / (1024.0 * 1024.0);

                    await using var contentStream = await resp.Content.ReadAsStreamAsync(linkedCts.Token);
                    await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    var buffer = new byte[81920]; // 80KB buffer
                    long bytesRead = 0;
                    int lastLoggedPercent = -1;
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer, linkedCts.Token)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), linkedCts.Token);
                        bytesRead += read;

                        // Log progress every 10%
                        if (totalBytes > 0)
                        {
                            var percent = (int)(bytesRead * 100 / totalBytes);
                            if (percent >= lastLoggedPercent + 10 || percent == 100)
                            {
                                var downloadedMB = bytesRead / (1024.0 * 1024.0);
                                _logger.LogInformation("Downloading EasyTier: {Percent}% ({DownloadedMB:F1} MB / {TotalMB:F1} MB)",
                                    percent, downloadedMB, totalMB);
                                lastLoggedPercent = percent;
                            }
                        }
                    }
                }
                _logger.LogInformation("EasyTier download completed, extracting...");
                if (assetName!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        var destPath = Path.Combine(resourceDir, entry.Name);
                        entry.ExtractToFile(destPath, true);
                    }
                }
                else if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || assetName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                {
                    using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var gz = new GZipStream(fs, CompressionMode.Decompress);
                    using var tar = new TarReader(gz);
                    TarEntry? entry;
                    while ((entry = tar.GetNextEntry()) != null)
                    {
                        if (entry.EntryType == TarEntryType.Directory) continue;
                        var fileName = Path.GetFileName(entry.Name);
                        if (string.IsNullOrEmpty(fileName)) continue;
                        var destPath = Path.Combine(resourceDir, fileName);
                        await using var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        if (entry.DataStream != null)
                        {
                            await entry.DataStream.CopyToAsync(outStream, linkedCts.Token);
                        }
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
            catch (OperationCanceledException)
            {
                _logger.LogWarning("EasyTier download was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EasyTier resource initialization failed");
            }
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
