using System.Security.Cryptography;
using System.Text;
using YukariConnect.Scaffolding.Models;

namespace YukariConnect.Scaffolding;

/// <summary>
/// Helper functions for Scaffolding protocol.
/// </summary>
public static class ScaffoldingHelpers
{
    // Character set for room code (no I, O to avoid confusion)
    // Maps to [0,32]: 0-9 (0-9), A-H (10-17), J-N (18-22), P-Z (23-32)
    private const string CharSet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // 33 characters

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
    /// Room code format: "U/NNNN-NNNN-SSSS-SSSS"
    /// Maps to network name: "scaffolding-mc-NNNN-NNNN"
    /// Maps to network secret: "SSSS-SSSS"
    /// </summary>
    public static bool TryParseRoomCode(string roomCode, out string networkName, out string networkSecret)
    {
        networkName = string.Empty;
        networkSecret = string.Empty;

        if (string.IsNullOrWhiteSpace(roomCode))
            return false;

        // Format: "U/NNNN-NNNN-SSSS-SSSS"
        if (!roomCode.StartsWith("U/"))
            return false;

        var code = roomCode.Substring(2); // Remove "U/" prefix

        // Expected format: NNNN-NNNN-SSSS-SSSS (18 chars)
        if (code.Length != 18)
            return false;

        // Validate format: XXXX-XXXX-XXXX-XXXX
        if (code[4] != '-' || code[9] != '-' || code[14] != '-')
            return false;

        var parts = code.Split('-');
        if (parts.Length != 4)
            return false;

        // Validate each part
        foreach (var part in parts)
        {
            if (part.Length != 4)
                return false;
            foreach (var c in part)
            {
                if (CharToValue(c) < 0)
                    return false;
            }
        }

        // Validate checksum: N and S values (little-endian) must be divisible by 7
        if (!ValidateChecksum(code))
            return false;

        // Network name: scaffolding-mc-NNNN-NNNN
        networkName = $"scaffolding-mc-{parts[0]}-{parts[1]}";

        // Network secret: SSSS-SSSS
        networkSecret = $"{parts[2]}-{parts[3]}";

        return true;
    }

    /// <summary>
    /// Generate room code in format U/NNNN-NNNN-SSSS-SSSS.
    /// The checksum ensures the sum of mapped values is divisible by 7.
    /// </summary>
    public static string GenerateRoomCode()
    {
        Span<byte> randomBytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(randomBytes);

        // Convert random bytes to character indices (mod 33)
        var indices = new int[16];
        for (int i = 0; i < 16; i++)
        {
            indices[i] = randomBytes[i] % 33;
        }

        // Calculate sum mod 7
        int sum = 0;
        for (int i = 0; i < 16; i++)
        {
            sum += indices[i];
        }
        int remainder = sum % 7;
        int adjustment = (7 - remainder) % 7;

        // Apply adjustment to last index to make sum divisible by 7
        indices[15] = (indices[15] + adjustment) % 33;

        // Build room code: NNNN-NNNN-SSSS-SSSS
        var n1 = ValueToChars(indices[0], indices[1], indices[2], indices[3]);
        var n2 = ValueToChars(indices[4], indices[5], indices[6], indices[7]);
        var s1 = ValueToChars(indices[8], indices[9], indices[10], indices[11]);
        var s2 = ValueToChars(indices[12], indices[13], indices[14], indices[15]);

        return $"U/{n1}-{n2}-{s1}-{s2}";
    }

    /// <summary>
    /// Validate checksum: map chars to [0,32], sum must be divisible by 7.
    /// Format: NNNN-NNNN-SSSS-SSSS
    /// </summary>
    private static bool ValidateChecksum(string code)
    {
        // Remove dashes
        var chars = code.Replace("-", "").AsSpan();

        if (chars.Length != 16)
            return false;

        int sum = 0;
        for (int i = 0; i < 16; i++)
        {
            int v = CharToValue(chars[i]);
            if (v < 0)
                return false;
            sum += v;
        }

        return sum % 7 == 0;
    }

    /// <summary>
    /// Map character to its value [0-32].
    /// Returns -1 if invalid.
    /// </summary>
    private static int CharToValue(char c)
    {
        if (c >= '0' && c <= '9')
            return c - '0';
        if (c >= 'A' && c <= 'H')
            return 10 + (c - 'A');
        if (c >= 'J' && c <= 'N')
            return 18 + (c - 'J');
        if (c >= 'P' && c <= 'Z')
            return 23 + (c - 'P');
        return -1;
    }

    /// <summary>
    /// Convert 4 values to 4-character string.
    /// </summary>
    private static string ValueToChars(int v0, int v1, int v2, int v3)
    {
        return new string(new[] { CharSet[v0], CharSet[v1], CharSet[v2], CharSet[v3] });
    }

    /// <summary>
    /// Get vendor string for YukariConnect.
    /// Format: "YukariConnect ET版本号 [启动器自定义字符串]"
    /// </summary>
    public static string GetVendorString(string etVersion, string? launcherCustomString = null)
    {
        var baseVendor = $"YukariConnect {etVersion}";
        return string.IsNullOrEmpty(launcherCustomString)
            ? baseVendor
            : $"{baseVendor} {launcherCustomString}";
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
