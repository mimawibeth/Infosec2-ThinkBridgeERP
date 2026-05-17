namespace ThinkBridge_ERP.Services;

public static class SessionSecurityEvaluator
{
    public static bool IsAccountSessionValid(string? status, bool isPermanentlyLocked, DateTime? lockoutEndUtc, DateTime utcNow)
    {
        if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (isPermanentlyLocked)
        {
            return false;
        }

        if (lockoutEndUtc.HasValue && lockoutEndUtc.Value > utcNow)
        {
            return false;
        }

        return true;
    }
}
