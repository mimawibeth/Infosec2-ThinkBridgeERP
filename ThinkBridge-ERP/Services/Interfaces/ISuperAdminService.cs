using ThinkBridge_ERP.Models.Entities;
using Task = System.Threading.Tasks.Task;

namespace ThinkBridge_ERP.Services.Interfaces;

public interface ISuperAdminService
{
    // Subscription Plan Management
    Task<List<SubscriptionPlanItem>> GetSubscriptionPlansAsync();
    Task<ServiceResult> CreateSubscriptionPlanAsync(CreatePlanRequest request, int performedByUserId);
    Task<ServiceResult> UpdateSubscriptionPlanDetailsAsync(int planId, UpdatePlanRequest request, int performedByUserId);

    // Subscription Management
    Task<SubscriptionListResult> GetSubscriptionsAsync(SubscriptionFilterRequest filter);
    Task<SubscriptionDetailResult> GetSubscriptionByIdAsync(int subscriptionId);
    Task<ServiceResult> CreateSubscriptionAsync(CreateSubscriptionRequest request, int performedByUserId);
    Task<ServiceResult> UpdateSubscriptionAsync(int subscriptionId, UpdateSubscriptionRequest request, int performedByUserId);
    Task<ServiceResult> UpdateSubscriptionPlanAsync(int subscriptionId, int newPlanId, int performedByUserId);
    Task<ServiceResult> CancelSubscriptionAsync(int subscriptionId, int performedByUserId);
    Task<SubscriptionStatsResult> GetSubscriptionStatsAsync();

    // Payment Management
    Task<PaymentListResult> GetPaymentsAsync(PaymentFilterRequest filter);
    Task<PaymentDetailResult> GetPaymentByIdAsync(int paymentId);
    Task<PaymentStatsResult> GetPaymentStatsAsync(int? year = null, int? month = null, DateTime? dateFrom = null, DateTime? dateTo = null);
    Task<ServiceResult> RecordManualPaymentAsync(ManualPaymentRequest request, int performedByUserId);

    // Audit Log
    Task<AuditLogListResult> GetAuditLogsAsync(AuditLogFilterRequest filter);
    Task LogActionAsync(int userId, int? companyId, string action, string entityName, int entityId, string? ipAddress = null);

    // Revenue
    Task<RevenueOverviewResult> GetRevenueOverviewAsync();

    // Platform Reports
    Task<PlatformReportResult> GetPlatformReportAsync(PlatformReportRequest request);

    // Emergency Admin Security Controls
    Task<EmergencyAdminStatusResult> GetEmergencyAdminStatusAsync();
    Task<ServiceResult> SuspendPrimarySuperAdminAsync(int performedByUserId, string performedByEmail, EmergencySuperAdminActionRequest request, string? ipAddress = null);
    Task<ServiceResult> ReactivatePrimarySuperAdminAsync(int performedByUserId, string performedByEmail, EmergencySuperAdminActionRequest request, string? ipAddress = null);
}

public class EmergencySuperAdminActionRequest
{
    public string ConfirmationPhrase { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? CurrentPassword { get; set; }
    public string? TotpCode { get; set; }
}

public class EmergencyAdminStatusResult : ServiceResult
{
    public string PrimarySuperAdminEmail { get; set; } = string.Empty;
    public string BackupSuperAdminEmail { get; set; } = string.Empty;
    public string ExpectedConfirmationPhrase { get; set; } = string.Empty;
    public int CooldownSeconds { get; set; }
    public bool PrimaryUserFound { get; set; }
    public string PrimaryStatus { get; set; } = "Unknown";
    public bool IsPrimarySuspended { get; set; }
    public bool IsPrimaryPermanentlyLocked { get; set; }
    public DateTime? LastEmergencyActionAt { get; set; }
}

// === Platform Report DTOs ===
public class PlatformReportRequest
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

public class PlatformReportResult : ServiceResult
{
    // Company Overview
    public int TotalCompanies { get; set; }
    public int ActiveCompanies { get; set; }
    public int InactiveCompanies { get; set; }
    public int NewCompaniesThisPeriod { get; set; }

    // User Overview
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }

    // Subscription Overview
    public int TotalSubscriptions { get; set; }
    public int ActiveSubscriptions { get; set; }
    public int TrialSubscriptions { get; set; }
    public int ExpiredSubscriptions { get; set; }
    public int CancelledSubscriptions { get; set; }
    public decimal MonthlyRecurringRevenue { get; set; }
    public List<PlanDistributionItem> PlanDistribution { get; set; } = new();

    // Payment Overview
    public decimal TotalRevenue { get; set; }
    public decimal PendingAmount { get; set; }
    public int CompletedPayments { get; set; }
    public int PendingPayments { get; set; }
    public int FailedPayments { get; set; }
    public List<MonthlyRevenueItem> MonthlyRevenue { get; set; } = new();

    // Platform Usage
    public int TotalProjects { get; set; }
    public int ActiveProjects { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int OverdueTasks { get; set; }

    // Top Companies
    public List<TopCompanyItem> TopCompaniesByUsers { get; set; } = new();
    public List<TopCompanyItem> TopCompaniesByRevenue { get; set; } = new();

    // Report metadata
    public DateTime GeneratedAt { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
}

public class PlanDistributionItem
{
    public string PlanName { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Revenue { get; set; }
}

public class TopCompanyItem
{
    public int CompanyID { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int UserCount { get; set; }
    public decimal TotalRevenue { get; set; }
}

// === Subscription DTOs ===
public class SubscriptionPlanItem
{
    public int PlanID { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingCycle { get; set; } = string.Empty;
    public int? MaxUsers { get; set; }
    public int? MaxProjects { get; set; }
    public bool IsActive { get; set; }
    public int ActiveSubscriptions { get; set; }
}

public class CreatePlanRequest
{
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingCycle { get; set; } = "Monthly";
    public int? MaxUsers { get; set; }
    public int? MaxProjects { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdatePlanRequest
{
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public string? BillingCycle { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxProjects { get; set; }
    public bool? IsActive { get; set; }
}

public class CreateSubscriptionRequest
{
    public int CompanyID { get; set; }
    public int PlanID { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class UpdateSubscriptionRequest
{
    public int? PlanID { get; set; }
    public string? Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class SubscriptionFilterRequest
{
    public string? SearchTerm { get; set; }
    public string? Status { get; set; }
    public string? PlanName { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class SubscriptionListResult : ServiceResult
{
    public List<SubscriptionListItem> Subscriptions { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

public class SubscriptionListItem
{
    public int SubscriptionID { get; set; }
    public int CompanyID { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? AdminEmail { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public int PlanID { get; set; }
    public decimal PlanPrice { get; set; }
    public string BillingCycle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int DaysRemaining { get; set; }
}

public class SubscriptionDetailResult : ServiceResult
{
    public Subscription? Subscription { get; set; }
    public Company? Company { get; set; }
    public User? AdminUser { get; set; }
    public List<Invoice> Invoices { get; set; } = new();
    public List<PaymentTransaction> Payments { get; set; } = new();
}

public class SubscriptionStatsResult : ServiceResult
{
    public int TotalSubscriptions { get; set; }
    public int ActiveSubscriptions { get; set; }
    public int TrialSubscriptions { get; set; }
    public int ExpiredSubscriptions { get; set; }
    public int CancelledSubscriptions { get; set; }
    public decimal MonthlyRecurringRevenue { get; set; }
    public Dictionary<string, int> PlanDistribution { get; set; } = new();
}

// === Payment DTOs ===
public class PaymentFilterRequest
{
    public string? SearchTerm { get; set; }
    public string? Status { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class PaymentListResult : ServiceResult
{
    public List<PaymentListItem> Payments { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

public class PaymentListItem
{
    public int PaymentID { get; set; }
    public int SubscriptionID { get; set; }
    public int? InvoiceID { get; set; }
    public string? InvoiceNumber { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int CompanyID { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PaymentDetailResult : ServiceResult
{
    public PaymentTransaction? Payment { get; set; }
    public string? CompanyName { get; set; }
    public string? PlanName { get; set; }
    public string? InvoiceNumber { get; set; }
}

public class PaymentStatsResult : ServiceResult
{
    public decimal TotalRevenue { get; set; }
    public decimal PendingAmount { get; set; }
    public decimal OverdueAmount { get; set; }
    public decimal CollectedAmount { get; set; }
    public int PendingCount { get; set; }
    public int OverdueCount { get; set; }
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
}

public class ManualPaymentRequest
{
    public int SubscriptionID { get; set; }
    public int? InvoiceID { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PHP";
    public string? PaymentMethod { get; set; }
    public string? Notes { get; set; }
}

// === Audit Log DTOs ===
public class AuditLogFilterRequest
{
    public string? SearchTerm { get; set; }
    public string? EntityName { get; set; }
    public int? CompanyId { get; set; }
    public int? UserId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class AuditLogListResult : ServiceResult
{
    public List<AuditLogItem> Logs { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

public class AuditLogItem
{
    public int LogID { get; set; }
    public int? CompanyID { get; set; }
    public string? CompanyName { get; set; }
    public int UserID { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public int EntityID { get; set; }
    public string? IPAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

// === Revenue DTOs ===
public class RevenueOverviewResult : ServiceResult
{
    public decimal TotalAllTimeRevenue { get; set; }
    public decimal CurrentMonthRevenue { get; set; }
    public decimal PreviousMonthRevenue { get; set; }
    public decimal RevenueGrowthPercent { get; set; }
    public List<MonthlyRevenueItem> MonthlyTrend { get; set; } = new();
}

public class MonthlyRevenueItem
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int PaymentCount { get; set; }
}
