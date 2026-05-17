using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkBridge_ERP.Services;
using ThinkBridge_ERP.Services.Interfaces;

namespace ThinkBridge_ERP.Controllers;

[ApiController]
[Route("api/superadmin")]
[Authorize(Policy = "SuperAdminOnly")]
public class SuperAdminController : ControllerBase
{
    private readonly ICompanyService _companyService;
    private readonly ISuperAdminService _superAdminService;
    private readonly PdfReportService _pdfReportService;
    private readonly ILogger<SuperAdminController> _logger;
    private readonly IConfiguration _configuration;

    public SuperAdminController(
        ICompanyService companyService,
        ISuperAdminService superAdminService,
        PdfReportService pdfReportService,
        ILogger<SuperAdminController> logger,
        IConfiguration configuration)
    {
        _companyService = companyService;
        _superAdminService = superAdminService;
        _pdfReportService = pdfReportService;
        _logger = logger;
        _configuration = configuration;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("UserId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var id) ? id : 0;
    }

    private string? GetClientIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private string GetCurrentUserEmail()
    {
        return User.FindFirst(ClaimTypes.Email)?.Value?.Trim() ?? string.Empty;
    }

    private string GetConfiguredBackupEmail()
    {
        var configured = _configuration["Security:EmergencyAdmin:BackupSuperAdminEmail"];
        return string.IsNullOrWhiteSpace(configured)
            ? string.Empty
            : configured.Trim().ToLowerInvariant();
    }

    // ========================
    // DASHBOARD
    // ========================

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardStats()
    {
        var result = await _companyService.GetDashboardStatsAsync();
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = result });
    }

    [HttpGet("dashboard/revenue")]
    public async Task<IActionResult> GetRevenueOverview()
    {
        var result = await _superAdminService.GetRevenueOverviewAsync();
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = result });
    }

    // ========================
    // COMPANY MANAGEMENT
    // ========================

    [HttpGet("companies")]
    public async Task<IActionResult> GetCompanies(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? plan = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var filter = new CompanyFilterRequest
        {
            SearchTerm = search,
            Status = status,
            PlanName = plan,
            Page = page,
            PageSize = pageSize
        };

        var result = await _companyService.GetCompaniesAsync(filter);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new
        {
            success = true,
            data = result.Companies,
            pagination = new
            {
                totalCount = result.TotalCount,
                page = result.Page,
                pageSize = result.PageSize,
                totalPages = result.TotalPages
            }
        });
    }

    [HttpGet("companies/{id}")]
    public async Task<IActionResult> GetCompany(int id)
    {
        var result = await _companyService.GetCompanyByIdAsync(id);
        if (!result.Success)
            return NotFound(new { success = false, message = result.ErrorMessage });

        return Ok(new
        {
            success = true,
            data = new
            {
                company = new
                {
                    result.Company!.CompanyID,
                    result.Company.CompanyName,
                    result.Company.Industry,
                    result.Company.Status,
                    result.Company.CreatedAt
                },
                subscription = result.ActiveSubscription != null ? new
                {
                    result.ActiveSubscription.SubscriptionID,
                    result.ActiveSubscription.PlanID,
                    planName = result.ActiveSubscription.Plan?.PlanName,
                    result.ActiveSubscription.Status,
                    startDate = result.ActiveSubscription.StartDate.ToString("yyyy-MM-dd"),
                    endDate = result.ActiveSubscription.EndDate?.ToString("yyyy-MM-dd")
                } : null,
                result.UserCount,
                result.ProjectCount,
                admin = result.AdminUser != null ? new
                {
                    result.AdminUser.UserID,
                    name = $"{result.AdminUser.Fname} {result.AdminUser.Lname}",
                    result.AdminUser.Email,
                    result.AdminUser.Phone
                } : null
            }
        });
    }

    [HttpPost("companies")]
    public async Task<IActionResult> CreateCompany([FromBody] CreateCompanyApiRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "Invalid request data." });

        var createRequest = new CreateCompanyRequest
        {
            CompanyName = request.CompanyName,
            Industry = request.Industry,
            PlanId = GetPlanIdFromName(request.PlanId),
            Status = request.Status ?? "Pending",
            Admin = new CreateAdminRequest
            {
                FirstName = request.Admin.FirstName,
                LastName = request.Admin.LastName,
                Email = request.Admin.Email,
                Phone = request.Admin.Phone
            }
        };

        var result = await _companyService.CreateCompanyAsync(createRequest);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        var userId = GetCurrentUserId();
        await _superAdminService.LogActionAsync(userId, result.CompanyId,
            $"Created company '{request.CompanyName}' with admin {request.Admin.Email}",
            "Company", result.CompanyId ?? 0, GetClientIpAddress());

        _logger.LogInformation("SuperAdmin created company {CompanyId} with admin {AdminId}",
            result.CompanyId, result.AdminUserId);

        return Ok(new
        {
            success = true,
            message = "Company created successfully.",
            data = new
            {
                companyId = result.CompanyId,
                adminUserId = result.AdminUserId,
                temporaryPassword = result.TemporaryPassword
            }
        });
    }

    [HttpPut("companies/{id}")]
    public async Task<IActionResult> UpdateCompany(int id, [FromBody] UpdateCompanyRequest request)
    {
        var result = await _companyService.UpdateCompanyAsync(id, request);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        var userId = GetCurrentUserId();
        await _superAdminService.LogActionAsync(userId, id,
            $"Updated company info for '{result.Company!.CompanyName}'",
            "Company", id, GetClientIpAddress());

        return Ok(new
        {
            success = true,
            message = "Company updated successfully.",
            data = new
            {
                result.Company!.CompanyID,
                result.Company.CompanyName,
                result.Company.Industry,
                result.Company.Status
            }
        });
    }

    [HttpPatch("companies/{id}/status")]
    public async Task<IActionResult> UpdateCompanyStatus(int id, [FromBody] UpdateStatusRequest request)
    {
        var result = await _companyService.UpdateCompanyStatusAsync(id, request.Status);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        var userId = GetCurrentUserId();
        await _superAdminService.LogActionAsync(userId, id,
            $"Changed company status to '{request.Status}'",
            "Company", id, GetClientIpAddress());

        return Ok(new { success = true, message = $"Company status updated to {request.Status}." });
    }

    // ========================
    // SUBSCRIPTION PLAN MANAGEMENT
    // ========================

    [HttpGet("subscription-plans")]
    public async Task<IActionResult> GetSubscriptionPlans()
    {
        var plans = await _superAdminService.GetSubscriptionPlansAsync();
        return Ok(new { success = true, data = plans });
    }

    [HttpPost("subscription-plans")]
    public async Task<IActionResult> CreateSubscriptionPlan([FromBody] CreatePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlanName))
            return BadRequest(new { success = false, message = "Plan name is required." });

        var userId = GetCurrentUserId();
        var result = await _superAdminService.CreateSubscriptionPlanAsync(request, userId);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, message = "Subscription plan created successfully." });
    }

    [HttpPut("subscription-plans/{id}")]
    public async Task<IActionResult> UpdateSubscriptionPlan(int id, [FromBody] UpdatePlanRequest request)
    {
        var userId = GetCurrentUserId();
        var result = await _superAdminService.UpdateSubscriptionPlanDetailsAsync(id, request, userId);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, message = "Subscription plan updated successfully." });
    }

    // ========================
    // SUBSCRIPTION MANAGEMENT
    // ========================

    [HttpGet("subscriptions")]
    public async Task<IActionResult> GetSubscriptions(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? plan = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var filter = new SubscriptionFilterRequest
        {
            SearchTerm = search,
            Status = status,
            PlanName = plan,
            Page = page,
            PageSize = pageSize
        };

        var result = await _superAdminService.GetSubscriptionsAsync(filter);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new
        {
            success = true,
            data = result.Subscriptions,
            pagination = new
            {
                totalCount = result.TotalCount,
                page = result.Page,
                pageSize = result.PageSize,
                totalPages = result.TotalPages
            }
        });
    }

    [HttpGet("subscriptions/{id}")]
    public async Task<IActionResult> GetSubscription(int id)
    {
        var result = await _superAdminService.GetSubscriptionByIdAsync(id);
        if (!result.Success)
            return NotFound(new { success = false, message = result.ErrorMessage });

        return Ok(new
        {
            success = true,
            data = new
            {
                subscription = new
                {
                    result.Subscription!.SubscriptionID,
                    result.Subscription.CompanyID,
                    result.Subscription.PlanID,
                    planName = result.Subscription.Plan?.PlanName,
                    planPrice = result.Subscription.Plan?.Price,
                    billingCycle = result.Subscription.Plan?.BillingCycle,
                    result.Subscription.Status,
                    startDate = result.Subscription.StartDate.ToString("yyyy-MM-dd"),
                    endDate = result.Subscription.EndDate?.ToString("yyyy-MM-dd")
                },
                company = new
                {
                    result.Company!.CompanyID,
                    result.Company.CompanyName,
                    result.Company.Industry,
                    result.Company.Status
                },
                admin = result.AdminUser != null ? new
                {
                    result.AdminUser.UserID,
                    name = $"{result.AdminUser.Fname} {result.AdminUser.Lname}",
                    result.AdminUser.Email
                } : null,
                invoices = result.Invoices.Select(i => new
                {
                    i.InvoiceID,
                    i.InvoiceNumber,
                    i.Amount,
                    dueDate = i.DueDate.ToString("yyyy-MM-dd"),
                    paidDate = i.PaidDate?.ToString("yyyy-MM-dd"),
                    i.Status
                }),
                payments = result.Payments.Select(p => new
                {
                    p.PaymentID,
                    p.Amount,
                    p.Status,
                    p.Provider,
                    p.PaymentMethod,
                    paidAt = p.PaidAt?.ToString("yyyy-MM-dd HH:mm"),
                    createdAt = p.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                })
            }
        });
    }

    [HttpGet("subscriptions/stats")]
    public async Task<IActionResult> GetSubscriptionStats()
    {
        var result = await _superAdminService.GetSubscriptionStatsAsync();
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = result });
    }

    [HttpPost("subscriptions")]
    public async Task<IActionResult> CreateSubscription([FromBody] CreateSubscriptionRequest request)
    {
        if (request.CompanyID <= 0 || request.PlanID <= 0)
            return BadRequest(new { success = false, message = "Company and Plan are required." });

        var userId = GetCurrentUserId();
        var result = await _superAdminService.CreateSubscriptionAsync(request, userId);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, message = "Subscription created successfully." });
    }

    [HttpPut("subscriptions/{id}")]
    public async Task<IActionResult> UpdateSubscription(int id, [FromBody] UpdateSubscriptionRequest request)
    {
        var userId = GetCurrentUserId();
        var result = await _superAdminService.UpdateSubscriptionAsync(id, request, userId);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, message = "Subscription updated successfully." });
    }

    [HttpPut("subscriptions/{id}/plan")]
    public async Task<IActionResult> UpdateSubscriptionPlan(int id, [FromBody] UpdateSubscriptionPlanRequest request)
    {
        var userId = GetCurrentUserId();
        var result = await _superAdminService.UpdateSubscriptionPlanAsync(id, request.PlanId, userId);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, message = "Subscription plan updated successfully." });
    }

    [HttpPost("subscriptions/{id}/cancel")]
    public async Task<IActionResult> CancelSubscription(int id)
    {
        var userId = GetCurrentUserId();
        var result = await _superAdminService.CancelSubscriptionAsync(id, userId);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, message = "Subscription cancelled successfully." });
    }

    // ========================
    // PAYMENT MANAGEMENT
    // ========================

    [HttpGet("payments")]
    public async Task<IActionResult> GetPayments(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var filter = new PaymentFilterRequest
        {
            SearchTerm = search,
            Status = status,
            Year = year,
            Month = month,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Page = page,
            PageSize = pageSize
        };

        var result = await _superAdminService.GetPaymentsAsync(filter);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        var pht = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        return Ok(new
        {
            success = true,
            data = result.Payments.Select(p => new
            {
                p.PaymentID,
                p.SubscriptionID,
                p.InvoiceID,
                p.InvoiceNumber,
                p.CompanyName,
                p.CompanyID,
                p.Provider,
                p.PaymentMethod,
                p.Amount,
                p.Currency,
                p.Status,
                paidAt = p.PaidAt.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(p.PaidAt.Value, pht).ToString("yyyy-MM-dd hh:mm tt")
                    : null,
                createdAt = TimeZoneInfo.ConvertTimeFromUtc(p.CreatedAt, pht).ToString("yyyy-MM-dd hh:mm tt")
            }),
            pagination = new
            {
                totalCount = result.TotalCount,
                page = result.Page,
                pageSize = result.PageSize,
                totalPages = result.TotalPages
            }
        });
    }

    [HttpGet("payments/{id}")]
    public async Task<IActionResult> GetPayment(int id)
    {
        var result = await _superAdminService.GetPaymentByIdAsync(id);
        if (!result.Success)
            return NotFound(new { success = false, message = result.ErrorMessage });

        var pht = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        return Ok(new
        {
            success = true,
            data = new
            {
                payment = new
                {
                    result.Payment!.PaymentID,
                    result.Payment.SubscriptionID,
                    result.Payment.InvoiceID,
                    result.Payment.Provider,
                    result.Payment.ExternalTransactionID,
                    result.Payment.PaymentMethod,
                    result.Payment.Amount,
                    result.Payment.Currency,
                    result.Payment.Status,
                    paidAt = result.Payment.PaidAt.HasValue
                        ? TimeZoneInfo.ConvertTimeFromUtc(result.Payment.PaidAt.Value, pht).ToString("yyyy-MM-dd hh:mm tt")
                        : null,
                    createdAt = TimeZoneInfo.ConvertTimeFromUtc(result.Payment.CreatedAt, pht).ToString("yyyy-MM-dd hh:mm tt")
                },
                companyName = result.CompanyName,
                planName = result.PlanName,
                invoiceNumber = result.InvoiceNumber
            }
        });
    }

    [HttpGet("payments/stats")]
    public async Task<IActionResult> GetPaymentStats(
        [FromQuery] int? year = null, [FromQuery] int? month = null,
        [FromQuery] DateTime? dateFrom = null, [FromQuery] DateTime? dateTo = null)
    {
        var result = await _superAdminService.GetPaymentStatsAsync(year, month, dateFrom, dateTo);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = result });
    }

    [HttpGet("payments/download-pdf")]
    public async Task<IActionResult> DownloadPaymentPdf([FromQuery] DateTime? dateFrom, [FromQuery] DateTime? dateTo)
    {
        var fullName = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? "Super Admin";

        try
        {
            var pdfBytes = await _pdfReportService.GeneratePaymentReportPdfAsync(fullName, dateFrom, dateTo);
            var fileName = $"ThinkBridge-Payment-Report-{DateTime.UtcNow:yyyy-MM-dd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Payment Report PDF.");
            return StatusCode(500, "Failed to generate PDF report.");
        }
    }

    [HttpPost("payments/manual")]
    public async Task<IActionResult> RecordManualPayment([FromBody] ManualPaymentRequest request)
    {
        if (request.Amount <= 0)
            return BadRequest(new { success = false, message = "Amount must be greater than zero." });

        var userId = GetCurrentUserId();
        var result = await _superAdminService.RecordManualPaymentAsync(request, userId);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, message = "Payment recorded successfully." });
    }

    // ========================
    // PLATFORM REPORTS
    // ========================

    [HttpGet("reports")]
    public async Task<IActionResult> GetPlatformReport([FromQuery] DateTime? dateFrom, [FromQuery] DateTime? dateTo)
    {
        var result = await _superAdminService.GetPlatformReportAsync(new PlatformReportRequest
        {
            DateFrom = dateFrom,
            DateTo = dateTo
        });

        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = result });
    }

    /// <summary>
    /// Download Super Admin PDF report (system-level financial)
    /// </summary>
    [HttpGet("reports/download-pdf")]
    public async Task<IActionResult> DownloadSuperAdminPdf([FromQuery] DateTime? dateFrom, [FromQuery] DateTime? dateTo)
    {
        var fullName = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? "Super Admin";

        try
        {
            var pdfBytes = await _pdfReportService.GenerateSuperAdminPdfAsync(fullName, dateFrom, dateTo);
            var fileName = $"ThinkBridge-Platform-Report-{DateTime.UtcNow:yyyy-MM-dd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Super Admin PDF report.");
            return StatusCode(500, "Failed to generate PDF report.");
        }
    }

    // ========================
    // EMERGENCY SECURITY CONTROLS
    // ========================

    [HttpGet("security/status")]
    public async Task<IActionResult> GetEmergencySecurityStatus()
    {
        var result = await _superAdminService.GetEmergencyAdminStatusAsync();
        if (!result.Success)
        {
            return BadRequest(new { success = false, message = result.ErrorMessage });
        }

        return Ok(new { success = true, data = result });
    }

    [HttpPost("security/suspend-primary")]
    public async Task<IActionResult> SuspendPrimarySuperAdmin([FromBody] EmergencySuperAdminActionRequest? request)
    {
        if (request == null)
        {
            return BadRequest(new { success = false, message = "Invalid request." });
        }

        var userId = GetCurrentUserId();
        if (userId == 0)
        {
            return BadRequest(new { success = false, message = "Unable to identify current super admin." });
        }

        var callerEmail = GetCurrentUserEmail().ToLowerInvariant();
        if (!string.Equals(callerEmail, GetConfiguredBackupEmail(), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { success = false, message = "Only the backup super admin can perform this action." });
        }

        var result = await _superAdminService.SuspendPrimarySuperAdminAsync(userId, callerEmail, request, GetClientIpAddress());
        if (!result.Success)
        {
            return BadRequest(new { success = false, message = result.ErrorMessage });
        }

        return Ok(new { success = true, message = "Primary super admin has been suspended." });
    }

    [HttpPost("security/reactivate-primary")]
    public async Task<IActionResult> ReactivatePrimarySuperAdmin([FromBody] EmergencySuperAdminActionRequest? request)
    {
        if (request == null)
        {
            return BadRequest(new { success = false, message = "Invalid request." });
        }

        var userId = GetCurrentUserId();
        if (userId == 0)
        {
            return BadRequest(new { success = false, message = "Unable to identify current super admin." });
        }

        var callerEmail = GetCurrentUserEmail().ToLowerInvariant();
        if (!string.Equals(callerEmail, GetConfiguredBackupEmail(), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { success = false, message = "Only the backup super admin can perform this action." });
        }

        var result = await _superAdminService.ReactivatePrimarySuperAdminAsync(userId, callerEmail, request, GetClientIpAddress());
        if (!result.Success)
        {
            return BadRequest(new { success = false, message = result.ErrorMessage });
        }

        return Ok(new { success = true, message = "Primary super admin has been reactivated." });
    }

    // ========================
    // HELPERS
    // ========================

    private static int GetPlanIdFromName(string planName)
    {
        return planName?.ToLower() switch
        {
            "trial" => 1,
            "starter" => 2,
            "professional" => 3,
            "enterprise" => 4,
            _ => 1
        };
    }
}

// API-specific request models
public class CreateCompanyApiRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string PlanId { get; set; } = "trial";
    public string? Status { get; set; }
    public CreateAdminApiRequest Admin { get; set; } = new();
}

public class CreateAdminApiRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
}

public class UpdateStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public class UpdateSubscriptionPlanRequest
{
    public int PlanId { get; set; }
}
