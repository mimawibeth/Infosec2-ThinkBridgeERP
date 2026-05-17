namespace ThinkBridge_ERP.Services;

public class PasswordValidationService
{
    private static readonly string[] BlockedPatterns =
    {
        "123456789012",
        "password123456",
        "admin1234567",
        "qwertyuiop12",
        "111111111111",
        "000000000000"
    };

    public bool IsBlacklisted(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var normalizedPassword = password.ToLowerInvariant();

        foreach (var pattern in BlockedPatterns)
        {
            if (normalizedPassword.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}