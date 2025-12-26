using System.Diagnostics;
using System.Text.Json;

namespace YukariConnect.Services
{
    public class EasyTierCliService
    {
        private readonly IHostEnvironment _env;
        private readonly ILogger<EasyTierCliService> _logger;
        private readonly string _cliPath;
        private readonly string _rpcPortal;
        private string? _cachedVersion;

        public EasyTierCliService(IHostEnvironment env, ILogger<EasyTierCliService> logger)
        {
            _env = env;
            _logger = logger;
            var resourceDir = Path.Combine(_env.ContentRootPath, "resource");
            var exeName = OperatingSystem.IsWindows() ? "easytier-cli.exe" : "easytier-cli";
            _cliPath = Path.Combine(resourceDir, exeName);
            _rpcPortal = "127.0.0.1:15888";
        }

        /// <summary>
        /// Get EasyTier CLI version string.
        /// </summary>
        public async Task<string> GetVersionAsync(CancellationToken ct = default)
        {
            if (_cachedVersion != null)
                return _cachedVersion;

            if (!File.Exists(_cliPath))
            {
                return "unknown";
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _cliPath,
                    ArgumentList = { "--version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();
                var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    _cachedVersion = stdout.Trim();
                    return _cachedVersion;
                }
            }
            catch
            {
                // Ignore errors, return "unknown"
            }

            _cachedVersion = "unknown";
            return _cachedVersion;
        }

        public async Task<JsonDocument?> NodeAsync(CancellationToken ct = default) =>
            await RunAsync(["node"], ct);

        public async Task<JsonDocument?> PeersAsync(CancellationToken ct = default) =>
            await RunAsync(["peer"], ct);

        public async Task<JsonDocument?> RoutesAsync(CancellationToken ct = default) =>
            await RunAsync(["route"], ct);

        public async Task<JsonDocument?> StatsAsync(CancellationToken ct = default) =>
            await RunAsync(["stats"], ct);

        public async Task<JsonDocument?> RunAsync(IEnumerable<string> subArgs, CancellationToken ct = default)
        {
            if (!File.Exists(_cliPath))
            {
                _logger.LogWarning("EasyTier CLI not found at {Path}", _cliPath);
                return null;
            }
            var psi = new ProcessStartInfo
            {
                FileName = _cliPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_cliPath)!
            };
            psi.ArgumentList.Add("--output");
            psi.ArgumentList.Add("json");
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(_rpcPortal);
            foreach (var a in subArgs)
            {
                psi.ArgumentList.Add(a);
            }
            using var proc = new Process { StartInfo = psi };
            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start EasyTier CLI");
                return null;
            }
            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0)
            {
                _logger.LogWarning("CLI exited with code {Code}. Error: {Error}", proc.ExitCode, stderr);
                return null;
            }
            try
            {
                return JsonDocument.Parse(stdout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse CLI JSON output");
                return null;
            }
        }
    }
}
