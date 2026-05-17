using System.Security.Cryptography;
using System.Text.Json;
using OtpNet;

namespace ThinkBridge_ERP.Services;

public static class TotpService
{
    private const string BackupCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string GenerateSecret(int byteLength = 20)
    {
        var secretBytes = KeyGeneration.GenerateRandomKey(byteLength);
        return Base32Encoding.ToString(secretBytes);
    }

    public static string BuildProvisioningUri(string issuer, string account, string secret, int digits, int periodSeconds)
    {
        var issuerPart = Uri.EscapeDataString(issuer);
        var accountPart = Uri.EscapeDataString(account);
        return $"otpauth://totp/{issuerPart}:{accountPart}?secret={secret}&issuer={issuerPart}&digits={digits}&period={periodSeconds}";
    }

    public static bool VerifyTotpCode(string secret, string inputCode, int digits, int periodSeconds, int allowedDriftWindows)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var normalizedCode = NormalizeTotpCode(inputCode);
        if (normalizedCode.Length != digits || !normalizedCode.All(char.IsDigit))
        {
            return false;
        }

        var totp = new Totp(Base32Encoding.ToBytes(secret), step: periodSeconds, totpSize: digits);
        return totp.VerifyTotp(
            normalizedCode,
            out _,
            new VerificationWindow(previous: allowedDriftWindows, future: allowedDriftWindows));
    }

    public static List<string> GenerateBackupCodes(int count, int codeLength)
    {
        var codes = new List<string>();
        var normalizedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (codes.Count < count)
        {
            var chars = new char[codeLength];
            for (var i = 0; i < codeLength; i++)
            {
                chars[i] = BackupCodeAlphabet[RandomNumberGenerator.GetInt32(BackupCodeAlphabet.Length)];
            }

            var rawCode = new string(chars);
            var normalized = NormalizeBackupCode(rawCode);
            if (!normalizedCodes.Add(normalized))
            {
                continue;
            }

            var split = codeLength / 2;
            codes.Add($"{rawCode[..split]}-{rawCode[split..]}");
        }

        return codes;
    }

    public static string SerializeHashedBackupCodes(IEnumerable<string> plainCodes)
    {
        var hashedCodes = plainCodes
            .Select(NormalizeBackupCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => BCrypt.Net.BCrypt.HashPassword(code))
            .ToList();

        return JsonSerializer.Serialize(hashedCodes);
    }

    public static int CountBackupCodes(string? serializedHashedCodes)
    {
        return DeserializeHashedBackupCodes(serializedHashedCodes).Count;
    }

    public static bool TryConsumeBackupCode(string inputCode, string? serializedHashedCodes, out string updatedSerializedHashedCodes)
    {
        updatedSerializedHashedCodes = serializedHashedCodes ?? "[]";

        var normalizedInput = NormalizeBackupCode(inputCode);
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return false;
        }

        var codes = DeserializeHashedBackupCodes(serializedHashedCodes);
        for (var i = 0; i < codes.Count; i++)
        {
            if (!BCrypt.Net.BCrypt.Verify(normalizedInput, codes[i]))
            {
                continue;
            }

            codes.RemoveAt(i);
            updatedSerializedHashedCodes = JsonSerializer.Serialize(codes);
            return true;
        }

        return false;
    }

    public static string NormalizeTotpCode(string input)
    {
        return new string((input ?? string.Empty).Where(char.IsDigit).ToArray());
    }

    private static string NormalizeBackupCode(string input)
    {
        return new string((input ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static List<string> DeserializeHashedBackupCodes(string? serializedHashedCodes)
    {
        if (string.IsNullOrWhiteSpace(serializedHashedCodes))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(serializedHashedCodes) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
