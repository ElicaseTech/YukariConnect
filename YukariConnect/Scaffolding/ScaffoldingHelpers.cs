using System.Security.Cryptography;
using System.Text;
using YukariConnect.Scaffolding.Models;

namespace YukariConnect.Scaffolding;

/// <summary>
/// Error codes for room code parsing.
/// </summary>
public enum RoomCodeParseError
{
    None = 0,
    Empty,
    BadPrefix,
    BadLength,
    BadDash,
    BadPartCount,
    BadPartLength,
    BadChar,
    BadChecksum,
}

/// <summary>
/// Helper functions for Scaffolding protocol.
/// </summary>
public static class ScaffoldingHelpers
{
    /// <summary>
    /// Build stamp for verifying correct assembly is loaded.
    /// </summary>
    public const string BuildStamp = "ScaffoldingHelpers 2025-12-27 checksum-le-base34";

    // Character set for room code (no I, O to avoid confusion)
    // Maps to [0,33]: 0-9 (0-9), A-H (10-17), J-N (18-22), P-Z (23-33)
    private const string CharSet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // 34 characters
    private static readonly int CharSetSize = CharSet.Length; // 34

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
        return TryParseRoomCode(roomCode, out networkName, out networkSecret, out _);
    }

    /// <summary>
    /// Parse room code to network name and secret with detailed error reporting.
    /// Room code format: "U/NNNN-NNNN-SSSS-SSSS"
    /// Maps to network name: "scaffolding-mc-NNNN-NNNN"
    /// Maps to network secret: "SSSS-SSSS"
    /// </summary>
    public static bool TryParseRoomCode(string roomCode, out string networkName, out string networkSecret, out RoomCodeParseError error)
    {
        networkName = string.Empty;
        networkSecret = string.Empty;
        error = RoomCodeParseError.None;

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            error = RoomCodeParseError.Empty;
            return false;
        }

        roomCode = roomCode.Trim();

        if (!roomCode.StartsWith("U/", StringComparison.OrdinalIgnoreCase))
        {
            error = RoomCodeParseError.BadPrefix;
            return false;
        }

        var code = roomCode.Substring(2); // Remove "U/" prefix

        // Expected format: NNNN-NNNN-SSSS-SSSS (19 chars: 4+1+4+1+4+1+4)
        // Debug: Log actual length for troubleshooting
        System.Diagnostics.Debug.WriteLine($"[DEBUG] roomCode length after Substring(2): {code.Length}, code: '{code}'");
        if (code.Length != 19)
        {
            error = RoomCodeParseError.BadLength;
            return false;
        }

        // Validate format: XXXX-XXXX-XXXX-XXXX
        if (code[4] != '-' || code[9] != '-' || code[14] != '-')
        {
            error = RoomCodeParseError.BadDash;
            return false;
        }

        var parts = code.Split('-');
        if (parts.Length != 4)
        {
            error = RoomCodeParseError.BadPartCount;
            return false;
        }

        // Validate each part
        foreach (var part in parts)
        {
            if (part.Length != 4)
            {
                error = RoomCodeParseError.BadPartLength;
                return false;
            }
            foreach (var c in part)
            {
                if (CharToValue(c) < 0)
                {
                    error = RoomCodeParseError.BadChar;
                    return false;
                }
            }
        }

        // Validate checksum: N and S values (little-endian) must be divisible by 7
        if (!ValidateChecksum(code))
        {
            error = RoomCodeParseError.BadChecksum;
            return false;
        }

        // Network name: scaffolding-mc-NNNN-NNNN
        networkName = $"scaffolding-mc-{parts[0]}-{parts[1]}";

        // Network secret: SSSS-SSSS
        networkSecret = $"{parts[2]}-{parts[3]}";

        return true;
    }

    /// <summary>
    /// Generate room code in format U/NNNN-NNNN-SSSS-SSSS.
    /// The checksum ensures the little-endian base-34 integer is divisible by 7.
    /// </summary>
    public static string GenerateRoomCode()
    {
        Span<byte> randomBytes = stackalloc byte[16];
        Span<int> indices = stackalloc int[16];

        while (true)
        {
            RandomNumberGenerator.Fill(randomBytes);

            // Convert random bytes to character indices
            for (int i = 0; i < 16; i++)
            {
                indices[i] = randomBytes[i] % CharSetSize;
            }

            // Build room code: NNNN-NNNN-SSSS-SSSS
            var n1 = ValueToChars(indices[0], indices[1], indices[2], indices[3]);
            var n2 = ValueToChars(indices[4], indices[5], indices[6], indices[7]);
            var s1 = ValueToChars(indices[8], indices[9], indices[10], indices[11]);
            var s2 = ValueToChars(indices[12], indices[13], indices[14], indices[15]);

            var code = $"{n1}-{n2}-{s1}-{s2}";

            // Validate checksum - retry if not valid
            if (ValidateChecksum(code))
                return $"U/{code}";
        }
    }

    /// <summary>
    /// Validate checksum: map chars to [0,33], interpret as little-endian base-34 integer, must be divisible by 7.
    /// Format: NNNN-NNNN-SSSS-SSSS
    /// </summary>
    private static bool ValidateChecksum(string code)
    {
        // Remove dashes
        var chars = code.Replace("-", "").AsSpan();

        if (chars.Length != 16)
            return false;

        // Calculate little-endian base-34 integer mod 7
        int baseMod = CharSetSize % 7; // 34 % 7 = 6
        int pow = 1;   // (CharSetSize^i) % 7
        int mod = 0;

        for (int i = 0; i < 16; i++)
        {
            int v = CharToValue(chars[i]);
            if (v < 0)
                return false;

            mod = (mod + (v % 7) * pow) % 7;
            pow = (pow * baseMod) % 7;
        }

        return mod == 0;
    }

    /// <summary>
    /// Map character to its value [0-33].
    /// Returns -1 if invalid.
    /// </summary>
    private static int CharToValue(char c)
    {
        int index = CharSet.IndexOf(c);
        return index >= 0 ? index : -1;
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
