namespace ThinkBridge_ERP.Services;

public sealed class EmailSendResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static EmailSendResult Ok() => new() { Success = true };

    public static EmailSendResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
