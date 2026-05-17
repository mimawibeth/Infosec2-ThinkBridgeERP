namespace ThinkBridge_ERP.Models.Configuration;

public class TotpSettings
{
    public string Issuer { get; set; } = "ThinkBridge ERP";
    public int Digits { get; set; } = 6;
    public int PeriodSeconds { get; set; } = 30;
    public int AllowedDriftWindows { get; set; } = 1;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 10;
    public int ChallengeTokenMinutes { get; set; } = 10;
    public int BackupCodeCount { get; set; } = 8;
    public int BackupCodeLength { get; set; } = 10;
}
