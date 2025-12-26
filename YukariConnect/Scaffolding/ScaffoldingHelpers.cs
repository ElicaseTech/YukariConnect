using System.Security.Cryptography;
using System.Text;
using YukariConnect.Scaffolding.Models;

namespace YukariConnect.Scaffolding;

/// <summary>
/// Helper functions for Scaffolding protocol.
/// </summary>
public static class ScaffoldingHelpers
{
    /// <summary>
    /// Parse center hostname to extract port.
    /// Hostname format: "scaffolding-mc-server-{port}"
    /// </summary>
    public static bool TryParseCenter(string hostname, out ushort port)
    {
        port = 0;
        const string prefix = "scaffolding-mc-server-";

        if (!hostname.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        if (!ushort.TryParse(hostname.AsSpan(prefix.Length), out port))
            return false;

        return port > 1024;
    }

    /// <summary>
    /// Generate center hostname from port.
    /// </summary>
    public static string GenerateCenterHostname(ushort port)
    {
        return $"scaffolding-mc-server-{port}";
    }

    /// <summary>
    /// Generate or load machine ID.
    /// Returns a 32-character hex string (16 bytes).
    /// </summary>
    public static string LoadOrCreateMachineId(string filePath)
    {
        if (File.Exists(filePath))
        {
            var existingHex = File.ReadAllText(filePath).Trim();
            if (existingHex.Length == 32 && existingHex.All(char.IsAsciiHexDigit))
                return existingHex;
        }

        // Generate new machine ID
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        var hex = Convert.ToHexString(bytes).ToLowerInvariant();

        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(filePath, hex);
        return hex;
    }

    /// <summary>
    /// Parse room code to network name and secret.
    /// Room code format: "U/{network_name}/{secret}" or "{network_name}:{secret}"
    /// </summary>
    public static bool TryParseRoomCode(string roomCode, out string networkName, out string networkSecret)
    {
        networkName = string.Empty;
        networkSecret = string.Empty;

        if (string.IsNullOrWhiteSpace(roomCode))
            return false;

        // Format 1: "U/name/secret"
        if (roomCode.StartsWith("U/"))
        {
            var parts = roomCode.Substring(2).Split('/');
            if (parts.Length == 2)
            {
                networkName = parts[0];
                networkSecret = parts[1];
                return !string.IsNullOrWhiteSpace(networkName) &&
                       !string.IsNullOrWhiteSpace(networkSecret);
            }
        }

        // Format 2: "name:secret"
        var colonIndex = roomCode.IndexOf(':');
        if (colonIndex > 0)
        {
            networkName = roomCode[..colonIndex];
            networkSecret = roomCode[(colonIndex + 1)..];
            return !string.IsNullOrWhiteSpace(networkName) &&
                   !string.IsNullOrWhiteSpace(networkSecret);
        }

        return false;
    }

    /// <summary>
    /// Generate room code from network name and secret.
    /// </summary>
    public static string GenerateRoomCode(string networkName, string networkSecret)
    {
        return $"U/{networkName}/{networkSecret}";
    }
}

/// <summary>
/// Extension methods for hex validation.
/// </summary>
internal static class StringExtensions
{
    internal static bool All(this string str, Func<char, bool> predicate)
    {
        foreach (var c in str)
        {
            if (!predicate(c))
                return false;
        }
        return true;
    }

    internal static bool IsAsciiHexDigit(this char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}
