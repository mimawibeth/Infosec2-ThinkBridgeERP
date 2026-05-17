using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using ThinkBridge_ERP.Data;
using ThinkBridge_ERP.Services;
using ThinkBridge_ERP.Services.Interfaces;

namespace ThinkBridge_ERP.Controllers;

public class SubscriptionController : Controller
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IPayMongoService _payMongoService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SubscriptionController> _logger;
    private readonly PasswordValidationService _passwordValidationService;

    public SubscriptionController(
        ISubscriptionService subscriptionService,
        IPayMongoService payMongoService,
        ApplicationDbContext context,
        ILogger<SubscriptionController> logger,
        PasswordValidationService passwordValidationService)
    {
        _subscriptionService = subscriptionService;
        _payMongoService = payMongoService;
        _context = context;
        _logger = logger;
        _passwordValidationService = passwordValidationService;
    }

    private static string GetDashboardUrl(string role)
    {
        return role.ToLower() switch
        {
            "superadmin" => "/Web/SuperAdminDashboard",
            "companyadmin" => "/Web/Dashboard",
            "projectmanager" => "/Web/ProjectManagerDashboard",
            "teammember" => "/Web/TeamMemberDashboard",
            _ => "/Web/Dashboard"
        };
    }

    // ==========================================
    // PAGE ROUTES
    // ==========================================

    /// <summary>
    /// Landing page - public, no auth required
    /// Authenticated users are redirected to their dashboard
    /// </summary>
    [HttpGet]
    [Route("/")]
    [Route("/Landing")]
    public async Task<IActionResult> Landing()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var role = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value ?? "TeamMember";
            return Redirect(GetDashboardUrl(role));
        }

        ViewData["HideLayout"] = true;
        ViewData["Title"] = "ThinkBridge ERP - Enterprise Resource Planning";
        var plans = await _subscriptionService.GetActivePlansAsync();
        return View("~/Views/Subscription/Landing.cshtml", plans);
    }

    /// <summary>
    /// Sign-up page - public
    /// Authenticated users are redirected to their dashboard
    /// </summary>
    [HttpGet]
    [Route("/Subscription/SignUp")]
    public async Task<IActionResult> SignUp(int? planId)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var role = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value ?? "TeamMember";
            return Redirect(GetDashboardUrl(role));
        }

        ViewData["HideLayout"] = true;
        ViewData["Title"] = "Sign Up - ThinkBridge ERP";
        var plans = await _subscriptionService.GetActivePlansAsync();
        ViewData["Plans"] = plans;
        ViewData["SelectedPlanId"] = planId;
        return View("~/Views/Subscription/SignUp.cshtml");
    }

    /// <summary>
    /// Payment success callback page
    /// </summary>
    [HttpGet]
    [Route("/Subscription/PaymentSuccess")]
    public async Task<IActionResult> PaymentSuccess(int? subscription_id)
    {
        ViewData["HideLayout"] = true;
        ViewData["Title"] = "Payment Successful - ThinkBridge ERP";

        if (subscription_id.HasValue)
        {
            var subscription = await _context.Subscriptions
                .Include(s => s.Company)
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.SubscriptionID == subscription_id.Value);

            if (subscription != null)
            {
                // Get the user's generated email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.CompanyID == subscription.CompanyID);

                ViewData["CompanyName"] = subscription.Company?.CompanyName;
                ViewData["PlanName"] = subscription.Plan?.PlanName;
                ViewData["UserEmail"] = user?.Email;
                ViewData["SubscriptionStatus"] = subscription.Status;
            }
        }

        return View("~/Views/Subscription/PaymentSuccess.cshtml");
    }

    /// <summary>
    /// Payment cancelled callback page
    /// </summary>
    [HttpGet]
    [Route("/Subscription/PaymentCancelled")]
    public IActionResult PaymentCancelled(int? subscription_id)
    {
        ViewData["HideLayout"] = true;
        ViewData["Title"] = "Payment Cancelled - ThinkBridge ERP";
        ViewData["SubscriptionId"] = subscription_id;
        return View("~/Views/Subscription/PaymentCancelled.cshtml");
    }

    // ==========================================
    // API ENDPOINTS
    // ==========================================

    /// <summary>
    /// Get available subscription plans
    /// </summary>
    [HttpGet]
    [Route("/api/subscription/plans")]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _subscriptionService.GetActivePlansAsync();
        return Json(plans.Select(p => new
        {
            p.PlanID,
            p.PlanName,
            p.Price,
            p.BillingCycle,
            p.MaxUsers,
            p.MaxProjects
        }));
    }

    /// <summary>
    /// Register a new company and create subscription
    /// </summary>
    [HttpPost]
    [Route("/api/subscription/register")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (request == null)
            return Json(new { success = false, message = "Invalid request." });

        request.CompanyName = request.CompanyName?.Trim() ?? string.Empty;
        request.AdminFirstName = request.AdminFirstName?.Trim() ?? string.Empty;
        request.AdminLastName = request.AdminLastName?.Trim() ?? string.Empty;
        request.AdminEmail = request.AdminEmail?.Trim() ?? string.Empty;
        request.Industry = request.Industry?.Trim();
        request.AdminPhone = request.AdminPhone?.Trim();

        var fieldErrors = new Dictionary<string, string>();

        var companyValidationError = ValidateCompanyName(request.CompanyName);
        if (!string.IsNullOrEmpty(companyValidationError))
            fieldErrors["companyName"] = companyValidationError;

        var firstNameValidationError = ValidatePersonName(request.AdminFirstName, "First name");
        if (!string.IsNullOrEmpty(firstNameValidationError))
            fieldErrors["adminFirstName"] = firstNameValidationError;

        var lastNameValidationError = ValidatePersonName(request.AdminLastName, "Last name");
        if (!string.IsNullOrEmpty(lastNameValidationError))
            fieldErrors["adminLastName"] = lastNameValidationError;

        var emailValidationError = ValidateEmail(request.AdminEmail);
        if (!string.IsNullOrEmpty(emailValidationError))
            fieldErrors["adminEmail"] = emailValidationError;

        if (fieldErrors.Count > 0)
        {
            return Json(new
            {
                success = false,
                message = "Please correct the highlighted fields.",
                fieldErrors
            });
        }

        if (string.IsNullOrWhiteSpace(request.AdminPassword))
            return Json(new { success = false, message = "Password is required." });

        if (request.AdminPassword.Length < 12)
            return Json(new { success = false, message = "Password must be minimum 12 characters." });

        if (!request.AdminPassword.Any(char.IsUpper))
            return Json(new { success = false, message = "Password must include uppercase letters." });

        if (!request.AdminPassword.Any(char.IsLower))
            return Json(new { success = false, message = "Password must include lowercase letters." });

        if (!request.AdminPassword.Any(char.IsDigit))
            return Json(new { success = false, message = "Password must include numeric characters." });

        if (!System.Text.RegularExpressions.Regex.IsMatch(request.AdminPassword, @"[^a-zA-Z0-9]"))
            return Json(new { success = false, message = "Password must include special characters." });

        if (_passwordValidationService.IsBlacklisted(request.AdminPassword))
            return Json(new { success = false, message = "This password is too common and has been blocked for security. Please choose a stronger password." });

        if (request.PlanId <= 0)
            return Json(new { success = false, message = "Please select a subscription plan." });

        var result = await _subscriptionService.RegisterCompanyAsync(new CompanyRegistrationRequest
        {
            CompanyName = request.CompanyName,
            Industry = request.Industry ?? "",
            AdminEmail = request.AdminEmail,
            AdminFirstName = request.AdminFirstName,
            AdminLastName = request.AdminLastName,
            AdminPhone = request.AdminPhone ?? "",
            AdminPassword = request.AdminPassword,
            PlanId = request.PlanId
        });

        if (!result.Success)
            return Json(new { success = false, message = result.ErrorMessage });

        // If free trial, no payment needed - return success with login details
        if (result.Amount == 0)
        {
            return Json(new
            {
                success = true,
                isTrial = true,
                email = result.GeneratedEmail,
                planName = result.PlanName,
                message = "Account activated! You can log in now."
            });
        }

        // For paid plans, create PayMongo checkout session
        var checkoutResult = await _payMongoService.CreateCheckoutSessionAsync(new PayMongoCheckoutRequest
        {
            SubscriptionId = result.SubscriptionId,
            Amount = result.Amount,
            Description = $"ThinkBridge ERP - {result.PlanName} Plan (Monthly)",
            CompanyName = request.CompanyName,
            CustomerEmail = result.GeneratedEmail,
            BaseUrl = $"{Request.Scheme}://{Request.Host}"
        });

        if (!checkoutResult.Success)
            return Json(new { success = false, message = checkoutResult.ErrorMessage });

        return Json(new
        {
            success = true,
            isTrial = false,
            checkoutUrl = checkoutResult.CheckoutUrl,
            email = result.GeneratedEmail,
            planName = result.PlanName,
            message = "Redirecting to payment..."
        });
    }

    private static readonly Regex SignupEmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PersonNameRegex = new(@"^[A-Za-z][A-Za-z' -]{1,49}$", RegexOptions.Compiled);
    private static readonly Regex CompanyNameRegex = new(@"^[A-Za-z0-9][A-Za-z0-9 .,'&()\-]{1,99}$", RegexOptions.Compiled);
    private static readonly HashSet<string> FakeNameTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "test", "asdf", "qwerty", "sample", "demo", "unknown", "none", "firstname", "lastname"
    };

    private static string? ValidateCompanyName(string companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return "Company name is required.";

        if (companyName.Length < 2)
            return "Company name must be at least 2 characters.";

        if (companyName.Length > 100)
            return "Company name must be 100 characters or fewer.";

        if (!companyName.Any(char.IsLetter))
            return "Company name must include at least one letter.";

        if (!CompanyNameRegex.IsMatch(companyName))
            return "Company name contains invalid characters.";

        return null;
    }

    private static string? ValidatePersonName(string name, string label)
    {
        if (string.IsNullOrWhiteSpace(name))
            return $"{label} is required.";

        if (name.Length < 2)
            return $"{label} must be at least 2 characters.";

        if (name.Length > 50)
            return $"{label} must be 50 characters or fewer.";

        if (!PersonNameRegex.IsMatch(name))
            return $"{label} contains invalid characters.";

        var token = NormalizeNameToken(name);
        if (FakeNameTokens.Contains(token))
            return $"Please enter a valid {label.ToLowerInvariant()}.";

        return null;
    }

    private static string? ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "Email address is required.";

        if (!SignupEmailRegex.IsMatch(email))
            return "Please enter a valid email address.";

        return null;
    }

    private static string NormalizeNameToken(string name)
    {
        return new string(name.Where(char.IsLetter).ToArray()).ToLowerInvariant();
    }

    /// <summary>
    /// PayMongo webhook endpoint - receives payment confirmations
    /// </summary>
    [HttpPost]
    [Route("/api/subscription/webhook/paymongo")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PayMongoWebhook()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();
            var signature = Request.Headers["Paymongo-Signature"].FirstOrDefault() ?? "";

            _logger.LogInformation("PayMongo webhook received");

            var result = await _payMongoService.ProcessWebhookAsync(payload, signature);

            if (!result.Success)
            {
                _logger.LogWarning("Webhook processing failed: {Error}", result.ErrorMessage);
                return Ok(); // Return 200 to prevent retries
            }

            // If payment was confirmed, activate or renew the subscription
            if (result.EventType == "checkout_session.payment.paid" && !string.IsNullOrEmpty(result.CheckoutSessionId))
            {
                // Check if this is a renewal or initial activation
                var payment = await _context.PaymentTransactions
                    .Include(p => p.Subscription)
                    .FirstOrDefaultAsync(p => p.CheckoutSessionID == result.CheckoutSessionId);

                if (payment != null)
                {
                    var subStatus = payment.Subscription.Status;
                    if (subStatus == "GracePeriod" || subStatus == "Expired" || subStatus == "Active")
                    {
                        // Renewal
                        var renewed = await _subscriptionService.RenewSubscriptionAsync(result.CheckoutSessionId);
                        if (renewed)
                        {
                            _logger.LogInformation("Subscription renewed via webhook for session {SessionId}", result.CheckoutSessionId);
                        }
                    }
                    else
                    {
                        // Initial activation
                        var activated = await _subscriptionService.ActivateSubscriptionAsync(result.CheckoutSessionId);
                        if (activated)
                        {
                            _logger.LogInformation("Subscription activated via webhook for session {SessionId}", result.CheckoutSessionId);
                        }
                    }
                }
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling PayMongo webhook");
            return Ok(); // Always return 200
        }
    }

    /// <summary>
    /// Manual activation check - called by the success page to poll for activation
    /// </summary>
    [HttpGet]
    [Route("/api/subscription/status/{subscriptionId}")]
    public async Task<IActionResult> CheckStatus(int subscriptionId)
    {
        var subscription = await _context.Subscriptions
            .Include(s => s.Company)
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.SubscriptionID == subscriptionId);

        if (subscription == null)
            return Json(new { success = false, message = "Subscription not found." });

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.CompanyID == subscription.CompanyID);

        return Json(new
        {
            success = true,
            status = subscription.Status,
            companyStatus = subscription.Company?.Status,
            email = user?.Email,
            planName = subscription.Plan?.PlanName,
            isActive = subscription.Status == "Active" || subscription.Status == "Trial"
        });
    }

    /// <summary>
    /// Manually activate subscription (for success page polling when webhook is delayed).
    /// Checks PayMongo directly for payment status.
    /// </summary>
    [HttpPost]
    [Route("/api/subscription/verify-payment")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> VerifyPayment([FromBody] VerifyPaymentRequest request)
    {
        if (request?.SubscriptionId == null || request.SubscriptionId <= 0)
            return Json(new { success = false, message = "Invalid subscription." });

        // Find payment for this subscription
        var payment = await _context.PaymentTransactions
            .Include(p => p.Subscription)
            .FirstOrDefaultAsync(p => p.SubscriptionID == request.SubscriptionId && p.Status == "Pending");

        if (payment == null)
        {
            // Check if already activated
            var sub = await _context.Subscriptions.FindAsync(request.SubscriptionId);
            if (sub?.Status == "Active")
                return Json(new { success = true, isActive = true, message = "Already activated." });

            return Json(new { success = false, message = "No pending payment found." });
        }

        // Try to activate (the user returned from PayMongo success URL, so payment likely went through)
        if (!string.IsNullOrEmpty(payment.CheckoutSessionID))
        {
            var activated = await _subscriptionService.ActivateSubscriptionAsync(payment.CheckoutSessionID);
            if (activated)
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.CompanyID == payment.Subscription.CompanyID);

                return Json(new
                {
                    success = true,
                    isActive = true,
                    email = user?.Email,
                    message = "Account activated successfully!"
                });
            }
        }

        return Json(new { success = false, isActive = false, message = "Payment not yet confirmed. Please wait a moment." });
    }

    /// <summary>
    /// Create a renewal checkout session for an existing subscription.
    /// Called by authenticated CompanyAdmin users.
    /// </summary>
    [HttpPost]
    [Route("/api/subscription/renew")]
    [IgnoreAntiforgeryToken]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "CompanyAdminOnly")]
    public async Task<IActionResult> RenewSubscription([FromBody] RenewSubscriptionRequest? request)
    {
        if (request == null || request.SubscriptionId <= 0)
            return Json(new { success = false, message = "Invalid subscription." });

        var renewRequest = request;

        var companyIdClaim = User.Claims.FirstOrDefault(c => c.Type == "CompanyID")?.Value;
        if (!int.TryParse(companyIdClaim, out var companyId) || companyId == 0)
            return Json(new { success = false, message = "Invalid company context." });

        var subscription = await _context.Subscriptions
            .Include(s => s.Plan)
            .Include(s => s.Company)
            .FirstOrDefaultAsync(s => s.SubscriptionID == renewRequest.SubscriptionId
                && s.CompanyID == companyId);

        if (subscription == null)
            return Json(new { success = false, message = "Subscription not found." });

        if (subscription.Plan.Price == 0)
            return Json(new { success = false, message = "Free trial plans cannot be renewed via payment." });

        var checkoutResult = await _payMongoService.CreateCheckoutSessionAsync(new PayMongoCheckoutRequest
        {
            SubscriptionId = subscription.SubscriptionID,
            Amount = subscription.Plan.Price,
            Description = $"ThinkBridge ERP - {subscription.Plan.PlanName} Plan Renewal (Monthly)",
            CompanyName = subscription.Company.CompanyName,
            CustomerEmail = "",
            BaseUrl = $"{Request.Scheme}://{Request.Host}"
        });

        if (!checkoutResult.Success)
            return Json(new { success = false, message = checkoutResult.ErrorMessage });

        return Json(new
        {
            success = true,
            checkoutUrl = checkoutResult.CheckoutUrl,
            message = "Redirecting to payment..."
        });
    }

    /// <summary>
    /// Payment success callback for renewals
    /// </summary>
    [HttpGet]
    [Route("/Subscription/RenewalSuccess")]
    public async Task<IActionResult> RenewalSuccess(int? subscription_id)
    {
        if (subscription_id.HasValue)
        {
            // Try to activate the renewal
            var payment = await _context.PaymentTransactions
                .Where(p => p.SubscriptionID == subscription_id.Value && p.Status == "Pending")
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            if (payment?.CheckoutSessionID != null)
            {
                await _subscriptionService.RenewSubscriptionAsync(payment.CheckoutSessionID);
            }
        }

        // Redirect to MySubscription page
        return Redirect("/Web/MySubscription");
    }
}

// Request DTOs
public class RegisterRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminFirstName { get; set; } = string.Empty;
    public string AdminLastName { get; set; } = string.Empty;
    public string? AdminPhone { get; set; }
    public string AdminPassword { get; set; } = string.Empty;
    public int PlanId { get; set; }
}

public class VerifyPaymentRequest
{
    public int SubscriptionId { get; set; }
}

public class RenewSubscriptionRequest
{
    public int SubscriptionId { get; set; }
}
