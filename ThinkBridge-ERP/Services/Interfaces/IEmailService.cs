using ThinkBridge_ERP.Services;

namespace ThinkBridge_ERP.Services.Interfaces;

public interface IEmailService
{
    Task<EmailSendResult> SendPasswordResetEmailAsync(string toEmail, string firstName, string temporaryPassword);
}
