using Microsoft.EntityFrameworkCore;
using ThinkBridge_ERP.Data;
using ThinkBridge_ERP.Models.Entities;
using ThinkBridge_ERP.Services.Interfaces;
using Task = System.Threading.Tasks.Task;

namespace ThinkBridge_ERP.Services;

public class SuperAdminService : ISuperAdminService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SuperAdminService> _logger;
    private readonly IConfiguration _configuration;

    public SuperAdminService(ApplicationDbContext context, ILogger<SuperAdminService> logger, IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
    }

    // ========================
    // SUBSCRIPTION PLAN MANAGEMENT
    // ========================

    public async Task<List<SubscriptionPlanItem>> GetSubscriptionPlansAsync()
    {
        try
        {
            return await _context.SubscriptionPlans
                .Select(p => new SubscriptionPlanItem
                {
                    PlanID = p.PlanID,
                    PlanName = p.PlanName,
                    Price = p.Price,
                    BillingCycle = p.BillingCycle,
                    MaxUsers = p.MaxUsers,
                    MaxProjects = p.MaxProjects,
                    IsActive = p.IsActive,
                    ActiveSubscriptions = p.Subscriptions.Count(s => s.Status == "Active" || s.Status == "Trial" || s.Status == "GracePeriod")
                })
                .OrderBy(p => p.Price)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription plans");
            return new List<SubscriptionPlanItem>();
        }
    }

    public async Task<ServiceResult> CreateSubscriptionPlanAsync(CreatePlanRequest request, int performedByUserId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.PlanName))
                return new ServiceResult { Success = false, ErrorMessage = "Plan name is required." };

            var exists = await _context.SubscriptionPlans.AnyAsync(p => p.PlanName.ToLower() == request.PlanName.ToLower());
            if (exists)
                return new ServiceResult { Success = false, ErrorMessage = "A plan with this name already exists." };

            var plan = new SubscriptionPlan
            {
                PlanName = request.PlanName,
                Price = request.Price,
                BillingCycle = request.BillingCycle,
                MaxUsers = request.MaxUsers,
                MaxProjects = request.MaxProjects,
                IsActive = request.IsActive
            };

            _context.SubscriptionPlans.Add(plan);
            await _context.SaveChangesAsync();

            await LogActionInternalAsync(performedByUserId, null,
                $"Created subscription plan: {plan.PlanName}", "SubscriptionPlan", plan.PlanID);

            return new ServiceResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription plan");
            return new ServiceResult { Success = false, ErrorMessage = "An error occurred while creating the plan." };
        }
    }

    public async Task<ServiceResult> UpdateSubscriptionPlanDetailsAsync(int planId, UpdatePlanRequest request, int performedByUserId)
    {
        try
        {
            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null)
                return new ServiceResult { Success = false, ErrorMessage = "Plan not found." };

            if (!string.IsNullOrWhiteSpace(request.PlanName) && request.PlanName != plan.PlanName)
            {
                var exists = await _context.SubscriptionPlans.AnyAsync(p => p.PlanID != planId && p.PlanName.ToLower() == request.PlanName.ToLower());
                if (exists)
                    return new ServiceResult { Success = false, ErrorMessage = "A plan with this name already exists." };
                plan.PlanName = request.PlanName;
            }

            if (request.Price.HasValue) plan.Price = request.Price.Value;
            if (!string.IsNullOrWhiteSpace(request.BillingCycle)) plan.BillingCycle = request.BillingCycle;
            if (request.MaxUsers.HasValue) plan.MaxUsers = request.MaxUsers.Value == 0 ? null : request.MaxUsers.Value;
            if (request.MaxProjects.HasValue) plan.MaxProjects = request.MaxProjects.Value == 0 ? null : request.MaxProjects.Value;
            if (request.IsActive.HasValue) plan.IsActive = request.IsActive.Value;

            await _context.SaveChangesAsync();

            await LogActionInternalAsync(performedByUserId, null,
                $"Updated subscription plan: {plan.PlanName}", "SubscriptionPlan", plan.PlanID);

            return new ServiceResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating subscription plan {PlanId}", planId);
            return new ServiceResult { Success = false, ErrorMessage = "An error occurred while updating the plan." };
        }
    }

    // ========================
    // SUBSCRIPTION MANAGEMENT
    // ========================

    public async Task<SubscriptionListResult> GetSubscriptionsAsync(SubscriptionFilterRequest filter)
    {
        try
        {
            var query = _context.Subscriptions
                .Include(s => s.Company)
                .Include(s => s.Plan)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var search = filter.SearchTerm.ToLower();
                query = query.Where(s => s.Company.CompanyName.ToLower().Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                query = query.Where(s => s.Status.ToLower() == filter.Status.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(filter.PlanName))
            {
                query = query.Where(s => s.Plan.PlanName.ToLower() == filter.PlanName.ToLower());
            }

            var totalCount = await query.CountAsync();
            var now = DateTime.UtcNow;

            var subscriptions = await query
                .OrderByDescending(s => s.StartDate)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(s => new SubscriptionListItem
                {
                    SubscriptionID = s.SubscriptionID,
                    CompanyID = s.CompanyID,
                    CompanyName = s.Company.CompanyName,
                    AdminEmail = s.Company.Users
                        .Where(u => _context.UserRoles.Any(ur => ur.UserID == u.UserID && ur.Role.RoleName == "CompanyAdmin"))
                        .Select(u => u.Email)
                        .FirstOrDefault(),
                    PlanName = s.Plan.PlanName,
                    PlanID = s.PlanID,
                    PlanPrice = s.Plan.Price,
                    BillingCycle = s.Plan.BillingCycle,
                    Status = s.Status,
                    StartDate = s.StartDate,
                    EndDate = s.EndDate,
                    DaysRemaining = s.EndDate.HasValue ? (int)(s.EndDate.Value - now).TotalDays : -1
                })
                .ToListAsync();

            return new SubscriptionListResult
            {
                Success = true,
                Subscriptions = subscriptions,
                TotalCount = totalCount,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions list");
            return new SubscriptionListResult
            {
                Success = false,
                ErrorMessage = "An error occurred while retrieving subscriptions."
            };
        }
    }

    public async Task<SubscriptionDetailResult> GetSubscriptionByIdAsync(int subscriptionId)
    {
        try
        {
            var subscription = await _context.Subscriptions
                .Include(s => s.Company)
                .Include(s => s.Plan)
                .Include(s => s.Invoices)
                .Include(s => s.PaymentTransactions)
                .FirstOrDefaultAsync(s => s.SubscriptionID == subscriptionId);

            if (subscription == null)
            {
                return new SubscriptionDetailResult { Success = false, ErrorMessage = "Subscription not found." };
            }

            var adminUser = await _context.Users
                .Where(u => u.CompanyID == subscription.CompanyID)
                .Where(u => _context.UserRoles.Any(ur => ur.UserID == u.UserID && ur.Role.RoleName == "CompanyAdmin"))
                .FirstOrDefaultAsync();

            return new SubscriptionDetailResult
            {
                Success = true,
                Subscription = subscription,
                Company = subscription.Company,
                AdminUser = adminUser,
                Invoices = subscription.Invoices.OrderByDescending(i => i.CreatedAt).ToList(),
                Payments = subscription.PaymentTransactions.OrderByDescending(p => p.CreatedAt).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription {SubscriptionId}", subscriptionId);
            return new SubscriptionDetailResult { Success = false, ErrorMessage = "An error occurred." };
        }
    }

    public async Task<ServiceResult> CreateSubscriptionAsync(CreateSubscriptionRequest request, int performedByUserId)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var company = await _context.Companies.FindAsync(request.CompanyID);
                if (company == null)
                    return new ServiceResult { Success = false, ErrorMessage = "Company not found." };

                var plan = await _context.SubscriptionPlans.FindAsync(request.PlanID);
                if (plan == null)
                    return new ServiceResult { Success = false, ErrorMessage = "Plan not found." };

                if (!plan.IsActive)
                    return new ServiceResult { Success = false, ErrorMessage = "Selected plan is not active." };

                // Check if company already has an active subscription
                var existingSub = await _context.Subscriptions
                    .AnyAsync(s => s.CompanyID == request.CompanyID && (s.Status == "Active" || s.Status == "Trial" || s.Status == "GracePeriod"));
                if (existingSub)
                    return new ServiceResult { Success = false, ErrorMessage = "Company already has an active subscription. Cancel or expire it first." };

                var subscription = new Subscription
                {
                    CompanyID = request.CompanyID,
                    PlanID = request.PlanID,
                    Status = request.Status,
                    StartDate = request.StartDate,
                    EndDate = request.EndDate
                };

                _context.Subscriptions.Add(subscription);

                // If status is Active, ensure company and users can access the system
                if (request.Status == "Active" && company.Status != "Active")
                {
                    company.Status = "Active";
                }

                if (request.Status is "Active" or "Trial" or "GracePeriod")
                {
                    await CompanyUserStatusSync.ApplyForCompanyStatusAsync(_context, request.CompanyID, "Active");
                }

                await _context.SaveChangesAsync();

                await LogActionInternalAsync(performedByUserId, request.CompanyID,
                    $"Created subscription for {company.CompanyName} on plan {plan.PlanName}",
                    "Subscription", subscription.SubscriptionID);

                await transaction.CommitAsync();

                _logger.LogInformation("Created subscription {SubscriptionId} for company {CompanyName}",
                    subscription.SubscriptionID, company.CompanyName);

                return new ServiceResult { Success = true };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating subscription");
                return new ServiceResult { Success = false, ErrorMessage = "An error occurred while creating the subscription." };
            }
        });
    }

    public async Task<ServiceResult> UpdateSubscriptionAsync(int subscriptionId, UpdateSubscriptionRequest request, int performedByUserId)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var subscription = await _context.Subscriptions
                    .Include(s => s.Plan)
                    .Include(s => s.Company)
                    .FirstOrDefaultAsync(s => s.SubscriptionID == subscriptionId);

                if (subscription == null)
                    return new ServiceResult { Success = false, ErrorMessage = "Subscription not found." };

                var changes = new List<string>();

                if (request.PlanID.HasValue && request.PlanID.Value != subscription.PlanID)
                {
                    var newPlan = await _context.SubscriptionPlans.FindAsync(request.PlanID.Value);
                    if (newPlan == null)
                        return new ServiceResult { Success = false, ErrorMessage = "Plan not found." };
                    if (!newPlan.IsActive)
                        return new ServiceResult { Success = false, ErrorMessage = "Selected plan is not active." };
                    changes.Add($"plan from {subscription.Plan.PlanName} to {newPlan.PlanName}");
                    subscription.PlanID = request.PlanID.Value;
                }

                if (!string.IsNullOrWhiteSpace(request.Status) && request.Status != subscription.Status)
                {
                    changes.Add($"status from {subscription.Status} to {request.Status}");
                    subscription.Status = request.Status;

                    // Sync company and user access based on subscription status
                    if (request.Status == "Cancelled" || request.Status == "Expired")
                    {
                        subscription.Company.Status = "Suspended";
                        await CompanyUserStatusSync.ApplyForCompanyStatusAsync(_context, subscription.CompanyID, "Suspended");
                    }
                    else if (request.Status == "Active" || request.Status == "Trial" || request.Status == "GracePeriod")
                    {
                        subscription.Company.Status = "Active";
                        await CompanyUserStatusSync.ApplyForCompanyStatusAsync(_context, subscription.CompanyID, "Active");
                    }

                    // Sync related payment status
                    var pendingPayments = await _context.PaymentTransactions
                        .Where(p => p.SubscriptionID == subscriptionId && p.Status == "Pending")
                        .ToListAsync();

                    if (request.Status == "Active")
                    {
                        foreach (var payment in pendingPayments)
                        {
                            payment.Status = "Completed";
                            payment.PaidAt = DateTime.UtcNow;
                        }
                    }
                    else if (request.Status == "Cancelled" || request.Status == "Expired")
                    {
                        foreach (var payment in pendingPayments)
                        {
                            payment.Status = "Cancelled";
                        }
                    }
                }

                if (request.StartDate.HasValue)
                {
                    changes.Add($"start date to {request.StartDate.Value:yyyy-MM-dd}");
                    subscription.StartDate = request.StartDate.Value;
                }

                if (request.EndDate.HasValue)
                {
                    changes.Add($"end date to {request.EndDate.Value:yyyy-MM-dd}");
                    subscription.EndDate = request.EndDate.Value;
                }

                if (changes.Count == 0)
                    return new ServiceResult { Success = false, ErrorMessage = "No changes provided." };

                await _context.SaveChangesAsync();

                await LogActionInternalAsync(performedByUserId, subscription.CompanyID,
                    $"Updated subscription for {subscription.Company.CompanyName}: {string.Join(", ", changes)}",
                    "Subscription", subscriptionId);

                await transaction.CommitAsync();

                _logger.LogInformation("Updated subscription {SubscriptionId}: {Changes}",
                    subscriptionId, string.Join(", ", changes));

                return new ServiceResult { Success = true };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error updating subscription {SubscriptionId}", subscriptionId);
                return new ServiceResult { Success = false, ErrorMessage = "An error occurred while updating the subscription." };
            }
        });
    }

    public async Task<ServiceResult> UpdateSubscriptionPlanAsync(int subscriptionId, int newPlanId, int performedByUserId)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var subscription = await _context.Subscriptions
                    .Include(s => s.Plan)
                    .Include(s => s.Company)
                    .FirstOrDefaultAsync(s => s.SubscriptionID == subscriptionId);

                if (subscription == null)
                    return new ServiceResult { Success = false, ErrorMessage = "Subscription not found." };

                var newPlan = await _context.SubscriptionPlans.FindAsync(newPlanId);
                if (newPlan == null)
                    return new ServiceResult { Success = false, ErrorMessage = "Plan not found." };

                if (!newPlan.IsActive)
                    return new ServiceResult { Success = false, ErrorMessage = "Selected plan is not active." };

                var oldPlanName = subscription.Plan.PlanName;
                subscription.PlanID = newPlanId;

                // If changing from Trial to paid, update status
                if (subscription.Status == "Trial" && newPlan.PlanName != "Trial")
                {
                    subscription.Status = "Active";
                    subscription.EndDate = DateTime.UtcNow.AddMonths(newPlan.BillingCycle == "Annual" ? 12 : 1);
                }

                await _context.SaveChangesAsync();

                // Log the action
                await LogActionInternalAsync(performedByUserId, subscription.CompanyID,
                    $"Changed subscription plan from {oldPlanName} to {newPlan.PlanName}",
                    "Subscription", subscriptionId);

                await transaction.CommitAsync();

                _logger.LogInformation("Updated subscription {SubscriptionId} plan to {PlanName}",
                    subscriptionId, newPlan.PlanName);

                return new ServiceResult { Success = true };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error updating subscription plan {SubscriptionId}", subscriptionId);
                return new ServiceResult { Success = false, ErrorMessage = "An error occurred while updating the subscription." };
            }
        });
    }

    public async Task<ServiceResult> CancelSubscriptionAsync(int subscriptionId, int performedByUserId)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var subscription = await _context.Subscriptions
                    .Include(s => s.Company)
                    .FirstOrDefaultAsync(s => s.SubscriptionID == subscriptionId);

                if (subscription == null)
                    return new ServiceResult { Success = false, ErrorMessage = "Subscription not found." };

                if (subscription.Status == "Cancelled")
                    return new ServiceResult { Success = false, ErrorMessage = "Subscription is already cancelled." };

                // Soft cancel: set status, keep records
                subscription.Status = "Cancelled";
                subscription.EndDate = DateTime.UtcNow;

                // Suspend the company
                subscription.Company.Status = "Suspended";

                // Cancel any pending payments for this subscription
                var pendingPayments = await _context.PaymentTransactions
                    .Where(p => p.SubscriptionID == subscriptionId && p.Status == "Pending")
                    .ToListAsync();

                foreach (var payment in pendingPayments)
                {
                    payment.Status = "Cancelled";
                }

                await CompanyUserStatusSync.ApplyForCompanyStatusAsync(_context, subscription.CompanyID, "Suspended");

                await _context.SaveChangesAsync();

                await LogActionInternalAsync(performedByUserId, subscription.CompanyID,
                    $"Cancelled subscription for {subscription.Company.CompanyName}",
                    "Subscription", subscriptionId);

                await transaction.CommitAsync();

                _logger.LogInformation("Cancelled subscription {SubscriptionId} for company {CompanyName}",
                    subscriptionId, subscription.Company.CompanyName);

                return new ServiceResult { Success = true };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error cancelling subscription {SubscriptionId}", subscriptionId);
                return new ServiceResult { Success = false, ErrorMessage = "An error occurred while cancelling the subscription." };
            }
        });
    }

    public async Task<SubscriptionStatsResult> GetSubscriptionStatsAsync()
    {
        try
        {
            var allSubs = await _context.Subscriptions
                .Include(s => s.Plan)
                .ToListAsync();

            var activeSubs = allSubs.Where(s => s.Status == "Active" || s.Status == "GracePeriod").ToList();
            var mrr = activeSubs.Sum(s => s.Plan.BillingCycle == "Annual" ? s.Plan.Price / 12 : s.Plan.Price);

            var planDistribution = allSubs
                .Where(s => s.Status == "Active" || s.Status == "GracePeriod")
                .GroupBy(s => s.Plan.PlanName)
                .ToDictionary(g => g.Key, g => g.Count());

            return new SubscriptionStatsResult
            {
                Success = true,
                TotalSubscriptions = allSubs.Count,
                ActiveSubscriptions = allSubs.Count(s => s.Status == "Active"),
                TrialSubscriptions = allSubs.Count(s => s.Status == "Trial"),
                ExpiredSubscriptions = allSubs.Count(s => s.Status == "Expired"),
                CancelledSubscriptions = allSubs.Count(s => s.Status == "Cancelled"),
                MonthlyRecurringRevenue = mrr,
                PlanDistribution = planDistribution
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription stats");
            return new SubscriptionStatsResult { Success = false, ErrorMessage = "An error occurred." };
        }
    }

    // ========================
    // PAYMENT MANAGEMENT
    // ========================

    public async Task<PaymentListResult> GetPaymentsAsync(PaymentFilterRequest filter)
    {
        try
        {
            // Auto-sync: fix orphaned pending payments whose subscription is already cancelled/expired
            var orphanedPayments = await _context.PaymentTransactions
                .Include(p => p.Subscription)
                .Where(p => p.Status == "Pending" &&
                    (p.Subscription.Status == "Cancelled" || p.Subscription.Status == "Expired"))
                .ToListAsync();

            if (orphanedPayments.Count > 0)
            {
                foreach (var payment in orphanedPayments)
                {
                    payment.Status = "Cancelled";
                }
                await _context.SaveChangesAsync();
                _logger.LogInformation("Auto-synced {Count} orphaned pending payments to Cancelled", orphanedPayments.Count);
            }

            var query = _context.PaymentTransactions
                .Include(p => p.Subscription)
                    .ThenInclude(s => s.Company)
                .Include(p => p.Invoice)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var search = filter.SearchTerm.ToLower();
                query = query.Where(p =>
                    p.Subscription.Company.CompanyName.ToLower().Contains(search) ||
                    (p.Invoice != null && p.Invoice.InvoiceNumber.ToLower().Contains(search)) ||
                    (p.ExternalTransactionID != null && p.ExternalTransactionID.ToLower().Contains(search)));
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                query = query.Where(p => p.Status.ToLower() == filter.Status.ToLower());
            }

            if (filter.Year.HasValue)
            {
                query = query.Where(p => p.CreatedAt.Year == filter.Year.Value);
            }

            if (filter.Month.HasValue)
            {
                query = query.Where(p => p.CreatedAt.Month == filter.Month.Value);
            }

            if (filter.DateFrom.HasValue)
            {
                var from = filter.DateFrom.Value.Date;
                query = query.Where(p => p.CreatedAt >= from);
            }

            if (filter.DateTo.HasValue)
            {
                var to = filter.DateTo.Value.Date.AddDays(1);
                query = query.Where(p => p.CreatedAt < to);
            }

            var totalCount = await query.CountAsync();

            var payments = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(p => new PaymentListItem
                {
                    PaymentID = p.PaymentID,
                    SubscriptionID = p.SubscriptionID,
                    InvoiceID = p.InvoiceID,
                    InvoiceNumber = p.Invoice != null ? p.Invoice.InvoiceNumber : null,
                    CompanyName = p.Subscription.Company.CompanyName,
                    CompanyID = p.Subscription.CompanyID,
                    Provider = p.Provider,
                    PaymentMethod = p.PaymentMethod,
                    Amount = p.Amount,
                    Currency = p.Currency,
                    Status = p.Status,
                    PaidAt = p.PaidAt,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();

            return new PaymentListResult
            {
                Success = true,
                Payments = payments,
                TotalCount = totalCount,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payments list");
            return new PaymentListResult { Success = false, ErrorMessage = "An error occurred while retrieving payments." };
        }
    }

    public async Task<PaymentDetailResult> GetPaymentByIdAsync(int paymentId)
    {
        try
        {
            var payment = await _context.PaymentTransactions
                .Include(p => p.Subscription)
                    .ThenInclude(s => s.Company)
                .Include(p => p.Subscription)
                    .ThenInclude(s => s.Plan)
                .Include(p => p.Invoice)
                .FirstOrDefaultAsync(p => p.PaymentID == paymentId);

            if (payment == null)
                return new PaymentDetailResult { Success = false, ErrorMessage = "Payment not found." };

            return new PaymentDetailResult
            {
                Success = true,
                Payment = payment,
                CompanyName = payment.Subscription.Company.CompanyName,
                PlanName = payment.Subscription.Plan.PlanName,
                InvoiceNumber = payment.Invoice?.InvoiceNumber
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment {PaymentId}", paymentId);
            return new PaymentDetailResult { Success = false, ErrorMessage = "An error occurred." };
        }
    }

    public async Task<PaymentStatsResult> GetPaymentStatsAsync(int? year = null, int? month = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        try
        {
            // Auto-sync: fix orphaned pending invoices whose subscription is cancelled/expired
            var orphanedInvoices = await _context.Invoices
                .Include(i => i.Subscription)
                .Where(i => i.Status == "Pending" &&
                    (i.Subscription.Status == "Cancelled" || i.Subscription.Status == "Expired"))
                .ToListAsync();

            if (orphanedInvoices.Count > 0)
            {
                foreach (var invoice in orphanedInvoices)
                {
                    invoice.Status = "Cancelled";
                }
                await _context.SaveChangesAsync();
            }

            var now = DateTime.UtcNow;

            // Determine the date window for payments
            DateTime startDate, endDate;
            if (dateFrom.HasValue && dateTo.HasValue)
            {
                startDate = dateFrom.Value.Date;
                endDate = dateTo.Value.Date.AddDays(1);
            }
            else
            {
                var targetYear = year ?? now.Year;
                var targetMonth = month ?? now.Month;
                startDate = new DateTime(targetYear, targetMonth, 1, 0, 0, 0, DateTimeKind.Utc);
                endDate = startDate.AddMonths(1);
            }

            var periodPayments = await _context.PaymentTransactions
                .Where(p => p.CreatedAt >= startDate && p.CreatedAt < endDate)
                .ToListAsync();

            var pendingInvoices = await _context.Invoices
                .Where(i => i.Status == "Pending")
                .ToListAsync();

            var overdueInvoices = pendingInvoices
                .Where(i => i.DueDate < now)
                .ToList();

            return new PaymentStatsResult
            {
                Success = true,
                TotalRevenue = periodPayments.Where(p => p.Status == "Completed" || p.Status == "Paid").Sum(p => p.Amount),
                PendingAmount = pendingInvoices.Sum(i => i.Amount),
                OverdueAmount = overdueInvoices.Sum(i => i.Amount),
                CollectedAmount = periodPayments.Where(p => p.Status == "Completed" || p.Status == "Paid").Sum(p => p.Amount),
                PendingCount = pendingInvoices.Count,
                OverdueCount = overdueInvoices.Count,
                CompletedCount = periodPayments.Count(p => p.Status == "Completed" || p.Status == "Paid"),
                FailedCount = periodPayments.Count(p => p.Status == "Failed")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment stats");
            return new PaymentStatsResult { Success = false, ErrorMessage = "An error occurred." };
        }
    }

    public async Task<ServiceResult> RecordManualPaymentAsync(ManualPaymentRequest request, int performedByUserId)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Validate financial input
                if (request.Amount <= 0)
                    return new ServiceResult { Success = false, ErrorMessage = "Amount must be greater than zero." };

                if (request.Amount > 1_000_000)
                    return new ServiceResult { Success = false, ErrorMessage = "Amount exceeds maximum allowed value." };

                var subscription = await _context.Subscriptions
                    .Include(s => s.Company)
                    .FirstOrDefaultAsync(s => s.SubscriptionID == request.SubscriptionID);

                if (subscription == null)
                    return new ServiceResult { Success = false, ErrorMessage = "Subscription not found." };

                // Create payment transaction
                var payment = new PaymentTransaction
                {
                    SubscriptionID = request.SubscriptionID,
                    InvoiceID = request.InvoiceID,
                    Provider = "Manual",
                    PaymentMethod = request.PaymentMethod ?? "Manual Entry",
                    Amount = request.Amount,
                    Currency = request.Currency,
                    Status = "Completed",
                    PaidAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };

                _context.PaymentTransactions.Add(payment);

                // If linked to an invoice, mark it as paid
                if (request.InvoiceID.HasValue)
                {
                    var invoice = await _context.Invoices.FindAsync(request.InvoiceID.Value);
                    if (invoice != null)
                    {
                        // Prevent modification of already-paid invoices
                        if (invoice.Status == "Paid")
                            return new ServiceResult { Success = false, ErrorMessage = "Invoice is already paid. Cannot modify paid records." };

                        invoice.Status = "Paid";
                        invoice.PaidDate = DateTime.UtcNow;
                    }
                }

                // Ensure subscription is active
                if (subscription.Status == "Expired" || subscription.Status == "Suspended")
                {
                    subscription.Status = "Active";
                    subscription.EndDate = DateTime.UtcNow.AddMonths(1);
                    subscription.Company.Status = "Active";
                    await CompanyUserStatusSync.ApplyForCompanyStatusAsync(_context, subscription.CompanyID, "Active");
                }

                await _context.SaveChangesAsync();

                await LogActionInternalAsync(performedByUserId, subscription.CompanyID,
                    $"Recorded manual payment of {request.Amount:C} for {subscription.Company.CompanyName}",
                    "PaymentTransaction", payment.PaymentID);

                await transaction.CommitAsync();

                _logger.LogInformation("Recorded manual payment {PaymentId} for subscription {SubscriptionId}",
                    payment.PaymentID, request.SubscriptionID);

                return new ServiceResult { Success = true };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error recording manual payment");
                return new ServiceResult { Success = false, ErrorMessage = "An error occurred while recording the payment." };
            }
        });
    }

    // ========================
    // AUDIT LOG
    // ========================

    public async Task<AuditLogListResult> GetAuditLogsAsync(AuditLogFilterRequest filter)
    {
        try
        {
            var query = _context.AuditLogs
                .Include(a => a.User)
                .Include(a => a.Company)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var search = filter.SearchTerm.ToLower();
                query = query.Where(a =>
                    a.Action.ToLower().Contains(search) ||
                    a.EntityName.ToLower().Contains(search) ||
                    a.User.Fname.ToLower().Contains(search) ||
                    a.User.Lname.ToLower().Contains(search) ||
                    a.User.Email.ToLower().Contains(search) ||
                    (a.Company != null && a.Company.CompanyName.ToLower().Contains(search)));
            }

            if (!string.IsNullOrWhiteSpace(filter.EntityName))
            {
                query = query.Where(a => a.EntityName.ToLower() == filter.EntityName.ToLower());
            }

            if (filter.CompanyId.HasValue)
            {
                query = query.Where(a => a.CompanyID == filter.CompanyId.Value);
            }

            if (filter.UserId.HasValue)
            {
                query = query.Where(a => a.UserID == filter.UserId.Value);
            }

            if (filter.DateFrom.HasValue)
            {
                query = query.Where(a => a.CreatedAt >= filter.DateFrom.Value);
            }

            if (filter.DateTo.HasValue)
            {
                var endDate = filter.DateTo.Value.Date.AddDays(1);
                query = query.Where(a => a.CreatedAt < endDate);
            }

            var totalCount = await query.CountAsync();

            var logs = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(a => new AuditLogItem
                {
                    LogID = a.LogID,
                    CompanyID = a.CompanyID,
                    CompanyName = a.Company != null ? a.Company.CompanyName : null,
                    UserID = a.UserID,
                    UserName = a.User.Fname + " " + a.User.Lname,
                    UserEmail = a.User.Email,
                    Action = a.Action,
                    EntityName = a.EntityName,
                    EntityID = a.EntityID,
                    IPAddress = a.IPAddress,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync();

            return new AuditLogListResult
            {
                Success = true,
                Logs = logs,
                TotalCount = totalCount,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting audit logs");
            return new AuditLogListResult { Success = false, ErrorMessage = "An error occurred while retrieving audit logs." };
        }
    }

    public async Task LogActionAsync(int userId, int? companyId, string action, string entityName, int entityId, string? ipAddress = null)
    {
        await LogActionInternalAsync(userId, companyId, action, entityName, entityId, ipAddress);
    }

    private async Task LogActionInternalAsync(int userId, int? companyId, string action, string entityName, int entityId, string? ipAddress = null)
    {
        try
        {
            var log = new AuditLog
            {
                UserID = userId,
                CompanyID = companyId,
                Action = action.Length > 120 ? action[..120] : action,
                EntityName = entityName.Length > 60 ? entityName[..60] : entityName,
                EntityID = entityId,
                IPAddress = ipAddress,
                CreatedAt = DateTime.UtcNow
            };

            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging audit action: {Action}", action);
        }
    }

    // ========================
    // EMERGENCY ADMIN SECURITY CONTROLS
    // ========================

    public async Task<EmergencyAdminStatusResult> GetEmergencyAdminStatusAsync()
    {
        try
        {
            var settings = GetEmergencyAdminSettings();
            var normalizedPrimaryEmail = NormalizeEmail(settings.PrimarySuperAdminEmail);

            var primary = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedPrimaryEmail);

            var primaryUserId = primary?.UserID;
            var lastActionAt = primaryUserId.HasValue
                ? await _context.AuditLogs
                    .AsNoTracking()
                    .Where(a => a.EntityName == "User"
                        && a.EntityID == primaryUserId.Value
                        && (a.Action.StartsWith("EmergencySuspendPrimarySuperAdmin")
                            || a.Action.StartsWith("EmergencyReactivatePrimarySuperAdmin")))
                    .OrderByDescending(a => a.CreatedAt)
                    .Select(a => (DateTime?)a.CreatedAt)
                    .FirstOrDefaultAsync()
                : null;

            return new EmergencyAdminStatusResult
            {
                Success = true,
                PrimarySuperAdminEmail = settings.PrimarySuperAdminEmail,
                BackupSuperAdminEmail = settings.BackupSuperAdminEmail,
                ExpectedConfirmationPhrase = settings.ConfirmationPhrase,
                CooldownSeconds = settings.CooldownSeconds,
                PrimaryUserFound = primary != null,
                PrimaryStatus = primary?.Status ?? "NotFound",
                IsPrimarySuspended = primary != null && !string.Equals(primary.Status, "Active", StringComparison.OrdinalIgnoreCase),
                IsPrimaryPermanentlyLocked = primary?.IsPermanentlyLocked ?? false,
                LastEmergencyActionAt = lastActionAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting emergency admin security status");
            return new EmergencyAdminStatusResult
            {
                Success = false,
                ErrorMessage = "Unable to load emergency security status right now."
            };
        }
    }

    public async Task<ServiceResult> SuspendPrimarySuperAdminAsync(int performedByUserId, string performedByEmail, EmergencySuperAdminActionRequest request, string? ipAddress = null)
    {
        return await ExecuteEmergencyActionAsync(
            performedByUserId,
            performedByEmail,
            request,
            ipAddress,
            actionName: "EmergencySuspendPrimarySuperAdmin",
            applyAction: primary =>
            {
                primary.Status = "Suspended";
                primary.FailedLoginAttempts = 0;
                primary.LockoutLevel = 0;
                primary.LockoutEnd = null;
                primary.IsPermanentlyLocked = true;
                primary.PermanentlyLockedAt = DateTime.UtcNow;
                primary.MustChangePassword = true;
            },
            alreadyAppliedCheck: primary => !string.Equals(primary.Status, "Active", StringComparison.OrdinalIgnoreCase) && primary.IsPermanentlyLocked,
            alreadyAppliedMessage: "Primary super admin is already suspended.",
            successMessage: "Primary super admin has been suspended.");
    }

    public async Task<ServiceResult> ReactivatePrimarySuperAdminAsync(int performedByUserId, string performedByEmail, EmergencySuperAdminActionRequest request, string? ipAddress = null)
    {
        return await ExecuteEmergencyActionAsync(
            performedByUserId,
            performedByEmail,
            request,
            ipAddress,
            actionName: "EmergencyReactivatePrimarySuperAdmin",
            applyAction: primary =>
            {
                primary.Status = "Active";
                primary.FailedLoginAttempts = 0;
                primary.LockoutLevel = 0;
                primary.LockoutEnd = null;
                primary.IsPermanentlyLocked = false;
                primary.PermanentlyLockedAt = null;
                primary.MustChangePassword = true;
            },
            alreadyAppliedCheck: primary => string.Equals(primary.Status, "Active", StringComparison.OrdinalIgnoreCase) && !primary.IsPermanentlyLocked,
            alreadyAppliedMessage: "Primary super admin is already active.",
            successMessage: "Primary super admin has been reactivated.");
    }

    private async Task<ServiceResult> ExecuteEmergencyActionAsync(
        int performedByUserId,
        string performedByEmail,
        EmergencySuperAdminActionRequest request,
        string? ipAddress,
        string actionName,
        Action<User> applyAction,
        Func<User, bool> alreadyAppliedCheck,
        string alreadyAppliedMessage,
        string successMessage)
    {
        try
        {
            if (request == null)
            {
                return new ServiceResult { Success = false, ErrorMessage = "Invalid request." };
            }

            var settings = GetEmergencyAdminSettings();
            var normalizedBackupEmail = NormalizeEmail(settings.BackupSuperAdminEmail);
            var normalizedPrimaryEmail = NormalizeEmail(settings.PrimarySuperAdminEmail);
            var normalizedActorEmail = NormalizeEmail(performedByEmail);

            var actor = await _context.Users.FirstOrDefaultAsync(u => u.UserID == performedByUserId);
            if (actor == null)
            {
                return new ServiceResult { Success = false, ErrorMessage = "Unable to validate backup super admin account." };
            }

            var actorEmail = NormalizeEmail(actor.Email);
            var isBackupCaller = actorEmail == normalizedBackupEmail && normalizedActorEmail == normalizedBackupEmail;
            if (!isBackupCaller)
            {
                await WriteSecurityAuditAsync(
                    actor.UserID,
                    actor.CompanyID,
                    actionName + "Denied:CallerNotBackup",
                    actor.UserID,
                    ipAddress,
                    request.Reason);

                return new ServiceResult { Success = false, ErrorMessage = "Only the backup super admin can perform this action." };
            }

            if (NormalizePhrase(request.ConfirmationPhrase) != NormalizePhrase(settings.ConfirmationPhrase))
            {
                await WriteSecurityAuditAsync(
                    actor.UserID,
                    actor.CompanyID,
                    actionName + "Denied:InvalidConfirmationPhrase",
                    actor.UserID,
                    ipAddress,
                    request.Reason);

                return new ServiceResult { Success = false, ErrorMessage = "Confirmation phrase does not match." };
            }

            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 8)
            {
                return new ServiceResult { Success = false, ErrorMessage = "A detailed reason is required (minimum 8 characters)." };
            }

            if (settings.CooldownSeconds > 0)
            {
                var cooldownStart = DateTime.UtcNow.AddSeconds(-settings.CooldownSeconds);
                var hasRecentAction = await _context.AuditLogs
                    .AsNoTracking()
                    .AnyAsync(a => a.UserID == actor.UserID
                        && a.CreatedAt >= cooldownStart
                        && (a.Action.StartsWith("EmergencySuspendPrimarySuperAdmin")
                            || a.Action.StartsWith("EmergencyReactivatePrimarySuperAdmin")));

                if (hasRecentAction)
                {
                    return new ServiceResult
                    {
                        Success = false,
                        ErrorMessage = $"Please wait {settings.CooldownSeconds} seconds before running another emergency action."
                    };
                }
            }

            var primary = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedPrimaryEmail);
            if (primary == null)
            {
                return new ServiceResult { Success = false, ErrorMessage = "Primary super admin account was not found." };
            }

            if (NormalizeEmail(primary.Email) == normalizedBackupEmail || primary.UserID == actor.UserID)
            {
                await WriteSecurityAuditAsync(
                    actor.UserID,
                    actor.CompanyID,
                    actionName + "Denied:TargetIsBackupOrSelf",
                    primary.UserID,
                    ipAddress,
                    request.Reason);

                return new ServiceResult { Success = false, ErrorMessage = "Emergency action cannot target the backup super admin account." };
            }

            if (alreadyAppliedCheck(primary))
            {
                return new ServiceResult { Success = false, ErrorMessage = alreadyAppliedMessage };
            }

            if (!VerifyStepUp(actor, request, out var stepUpError))
            {
                await WriteSecurityAuditAsync(
                    actor.UserID,
                    actor.CompanyID,
                    actionName + "Denied:StepUpFailed",
                    primary.UserID,
                    ipAddress,
                    request.Reason);

                return new ServiceResult { Success = false, ErrorMessage = stepUpError };
            }

            await using var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync()
                : null;

            applyAction(primary);

            _context.AuditLogs.Add(new AuditLog
            {
                UserID = actor.UserID,
                CompanyID = actor.CompanyID,
                Action = BuildEmergencyActionMessage(actionName, request.Reason),
                EntityName = "User",
                EntityID = primary.UserID,
                IPAddress = ipAddress,
                LogType = "Security",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            if (transaction != null)
            {
                await transaction.CommitAsync();
            }

            return new ServiceResult { Success = true, ErrorMessage = successMessage };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing emergency super admin action {ActionName}", actionName);
            return new ServiceResult { Success = false, ErrorMessage = "Unable to complete this emergency action right now." };
        }
    }

    private async Task WriteSecurityAuditAsync(int userId, int? companyId, string action, int entityId, string? ipAddress, string? reason)
    {
        try
        {
            _context.AuditLogs.Add(new AuditLog
            {
                UserID = userId,
                CompanyID = companyId,
                Action = BuildEmergencyActionMessage(action, reason),
                EntityName = "User",
                EntityID = entityId,
                IPAddress = ipAddress,
                LogType = "Security",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write security audit action {Action}", action);
        }
    }

    private bool VerifyStepUp(User actor, EmergencySuperAdminActionRequest request, out string error)
    {
        error = "Step-up verification is required.";

        var providedPassword = !string.IsNullOrWhiteSpace(request.CurrentPassword);
        var providedTotp = !string.IsNullOrWhiteSpace(request.TotpCode);

        if (!providedPassword && !providedTotp)
        {
            return false;
        }

        if (providedTotp
            && actor.IsTotpEnabled
            && !string.IsNullOrWhiteSpace(actor.TotpSecret)
            && VerifyTotpCode(actor.TotpSecret!, request.TotpCode!))
        {
            return true;
        }

        if (providedPassword && BCrypt.Net.BCrypt.Verify(request.CurrentPassword, actor.Password))
        {
            return true;
        }

        error = "Step-up verification failed. Provide a valid backup admin password or authenticator code.";
        return false;
    }

    private bool VerifyTotpCode(string secret, string code)
    {
        var digits = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:Digits") ?? 6, 6, 8);
        var periodSeconds = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:PeriodSeconds") ?? 30, 15, 90);
        var allowedDriftWindows = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:AllowedDriftWindows") ?? 1, 0, 5);

        return TotpService.VerifyTotpCode(secret, code, digits, periodSeconds, allowedDriftWindows);
    }

    private EmergencyAdminSettings GetEmergencyAdminSettings()
    {
        var primary = _configuration["Security:EmergencyAdmin:PrimarySuperAdminEmail"];
        var backup = _configuration["Security:EmergencyAdmin:BackupSuperAdminEmail"];
        var phrase = _configuration["Security:EmergencyAdmin:ConfirmationPhrase"];
        var cooldown = _configuration.GetValue<int?>("Security:EmergencyAdmin:CooldownSeconds") ?? 30;

        var normalizedPrimary = string.IsNullOrWhiteSpace(primary) ? string.Empty : primary.Trim();
        var normalizedBackup = string.IsNullOrWhiteSpace(backup) ? string.Empty : backup.Trim();

        return new EmergencyAdminSettings(
            normalizedPrimary,
            normalizedBackup,
            string.IsNullOrWhiteSpace(phrase) ? "SUSPEND PRIMARY SUPERADMIN" : phrase.Trim(),
            Math.Clamp(cooldown, 0, 600));
    }

    private static string NormalizeEmail(string? email)
    {
        return (email ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string NormalizePhrase(string? phrase)
    {
        return (phrase ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string BuildEmergencyActionMessage(string actionName, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return actionName;
        }

        var normalizedReason = reason.Trim();
        var reasonSuffix = normalizedReason.Length > 80 ? normalizedReason[..80] : normalizedReason;
        return $"{actionName}: {reasonSuffix}";
    }

    private sealed record EmergencyAdminSettings(
        string PrimarySuperAdminEmail,
        string BackupSuperAdminEmail,
        string ConfirmationPhrase,
        int CooldownSeconds);

    // ========================
    // REVENUE
    // ========================

    public async Task<RevenueOverviewResult> GetRevenueOverviewAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var previousMonthStart = currentMonthStart.AddMonths(-1);

            var allCompletedPayments = await _context.PaymentTransactions
                .Where(p => p.Status == "Completed" || p.Status == "Paid")
                .ToListAsync();

            var totalRevenue = allCompletedPayments.Sum(p => p.Amount);
            var currentMonthRevenue = allCompletedPayments
                .Where(p => p.CreatedAt >= currentMonthStart)
                .Sum(p => p.Amount);
            var previousMonthRevenue = allCompletedPayments
                .Where(p => p.CreatedAt >= previousMonthStart && p.CreatedAt < currentMonthStart)
                .Sum(p => p.Amount);

            var growthPercent = previousMonthRevenue > 0
                ? Math.Round(((currentMonthRevenue - previousMonthRevenue) / previousMonthRevenue) * 100, 1)
                : 0;

            // Build monthly trend for last 6 months
            var monthlyTrend = new List<MonthlyRevenueItem>();
            for (int i = 5; i >= 0; i--)
            {
                var monthStart = currentMonthStart.AddMonths(-i);
                var monthEnd = monthStart.AddMonths(1);
                var monthPayments = allCompletedPayments
                    .Where(p => p.CreatedAt >= monthStart && p.CreatedAt < monthEnd)
                    .ToList();

                monthlyTrend.Add(new MonthlyRevenueItem
                {
                    Year = monthStart.Year,
                    Month = monthStart.Month,
                    Label = monthStart.ToString("MMM yyyy"),
                    Revenue = monthPayments.Sum(p => p.Amount),
                    PaymentCount = monthPayments.Count
                });
            }

            return new RevenueOverviewResult
            {
                Success = true,
                TotalAllTimeRevenue = totalRevenue,
                CurrentMonthRevenue = currentMonthRevenue,
                PreviousMonthRevenue = previousMonthRevenue,
                RevenueGrowthPercent = growthPercent,
                MonthlyTrend = monthlyTrend
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting revenue overview");
            return new RevenueOverviewResult { Success = false, ErrorMessage = "An error occurred." };
        }
    }

    // ========================
    // PLATFORM REPORTS
    // ========================

    public async Task<PlatformReportResult> GetPlatformReportAsync(PlatformReportRequest request)
    {
        try
        {
            var now = DateTime.UtcNow;
            var periodEnd = request.DateTo?.Date.AddDays(1) ?? now;
            var periodStart = request.DateFrom?.Date ?? periodEnd.AddMonths(-12);

            // ---- Company Overview ----
            var companies = await _context.Companies.ToListAsync();
            var totalCompanies = companies.Count;
            var activeCompanies = companies.Count(c => c.Status == "Active");
            var inactiveCompanies = companies.Count(c => c.Status != "Active");
            var newCompaniesThisPeriod = companies.Count(c => c.CreatedAt >= periodStart && c.CreatedAt < periodEnd);

            // ---- User Overview ----
            var totalUsers = await _context.Users.CountAsync(u => !u.IsSuperAdmin);
            var activeUsers = await _context.Users.CountAsync(u => !u.IsSuperAdmin && u.Status == "Active");

            // ---- Subscription Overview ----
            var subscriptions = await _context.Subscriptions
                .Include(s => s.Plan)
                .ToListAsync();

            var totalSubscriptions = subscriptions.Count;
            var activeSubs = subscriptions.Count(s => s.Status == "Active");
            var trialSubs = subscriptions.Count(s => s.Status == "Trial");
            var expiredSubs = subscriptions.Count(s => s.Status == "Expired");
            var cancelledSubs = subscriptions.Count(s => s.Status == "Cancelled");

            var mrr = subscriptions
                .Where(s => (s.Status == "Active" || s.Status == "GracePeriod") && s.Plan != null)
                .Sum(s => s.Plan!.BillingCycle == "Annual" ? s.Plan.Price / 12m : s.Plan.Price);

            var planDistribution = subscriptions
                .Where(s => s.Plan != null && s.Plan.PlanName != "Trial")
                .GroupBy(s => s.Plan!.PlanName)
                .Select(g => new PlanDistributionItem
                {
                    PlanName = g.Key,
                    Count = g.Count(),
                    Revenue = g.Where(s => s.Status == "Active" || s.Status == "GracePeriod").Sum(s => s.Plan!.Price)
                })
                .OrderByDescending(p => p.Count)
                .ToList();

            // ---- Payment Overview ----
            var payments = await _context.PaymentTransactions.ToListAsync();
            var completedPayments = payments.Where(p => p.Status == "Completed" || p.Status == "Paid").ToList();

            // Total Revenue (Yearly) - sum of completed payments in the current calendar year
            var yearStart = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var totalRevenue = completedPayments
                .Where(p => p.CreatedAt >= yearStart)
                .Sum(p => p.Amount);
            var pendingAmount = payments.Where(p => p.Status == "Pending").Sum(p => p.Amount);
            var completedCount = completedPayments.Count;
            var pendingCount = payments.Count(p => p.Status == "Pending");
            var failedCount = payments.Count(p => p.Status == "Failed");

            // Monthly revenue for the period (up to 12 months)
            var monthlyRevenue = new List<MonthlyRevenueItem>();
            var monthCount = Math.Min(12, (int)Math.Ceiling((periodEnd - periodStart).TotalDays / 30.0));
            for (int i = monthCount - 1; i >= 0; i--)
            {
                var monthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-i);
                var monthEnd = monthStart.AddMonths(1);
                var monthPayments = completedPayments
                    .Where(p => p.CreatedAt >= monthStart && p.CreatedAt < monthEnd)
                    .ToList();

                monthlyRevenue.Add(new MonthlyRevenueItem
                {
                    Year = monthStart.Year,
                    Month = monthStart.Month,
                    Label = monthStart.ToString("MMM yyyy"),
                    Revenue = monthPayments.Sum(p => p.Amount),
                    PaymentCount = monthPayments.Count
                });
            }

            // ---- Platform Usage ----
            var totalProjects = await _context.Projects.CountAsync();
            var activeProjects = await _context.Projects.CountAsync(p => p.Status == "In Progress" || p.Status == "Active");
            var totalTasks = await _context.Tasks.CountAsync();
            var completedTasks = await _context.Tasks.CountAsync(t => t.Status == "Completed" || t.Status == "Done");
            var overdueTasks = await _context.Tasks.CountAsync(t =>
                t.DueDate.HasValue && t.DueDate < now && t.Status != "Completed" && t.Status != "Done");

            // ---- Top Companies ----
            var companyUsers = await _context.Users
                .Where(u => u.CompanyID != null && !u.IsSuperAdmin)
                .GroupBy(u => u.CompanyID)
                .Select(g => new { CompanyID = g.Key!.Value, UserCount = g.Count() })
                .OrderByDescending(x => x.UserCount)
                .Take(10)
                .ToListAsync();

            var topCompanyIds = companyUsers.Select(c => c.CompanyID).ToList();
            var topCompanyEntities = await _context.Companies
                .Where(c => topCompanyIds.Contains(c.CompanyID))
                .ToListAsync();

            var topCompanySubs = await _context.Subscriptions
                .Include(s => s.Plan)
                .Where(s => topCompanyIds.Contains(s.CompanyID))
                .ToListAsync();

            var topCompaniesByUsers = companyUsers.Select(cu =>
            {
                var comp = topCompanyEntities.FirstOrDefault(c => c.CompanyID == cu.CompanyID);
                var sub = topCompanySubs.FirstOrDefault(s => s.CompanyID == cu.CompanyID && (s.Status == "Active" || s.Status == "GracePeriod"));
                return new TopCompanyItem
                {
                    CompanyID = cu.CompanyID,
                    CompanyName = comp?.CompanyName ?? "Unknown",
                    Industry = comp?.Industry ?? "-",
                    PlanName = sub?.Plan?.PlanName ?? "No Plan",
                    UserCount = cu.UserCount
                };
            }).ToList();

            // Top companies by revenue
            var companyRevenue = await _context.PaymentTransactions
                .Where(p => p.Status == "Completed" || p.Status == "Paid")
                .Join(_context.Subscriptions, p => p.SubscriptionID, s => s.SubscriptionID, (p, s) => new { s.CompanyID, p.Amount })
                .GroupBy(x => x.CompanyID)
                .Select(g => new { CompanyID = g.Key, TotalRevenue = g.Sum(x => x.Amount) })
                .OrderByDescending(x => x.TotalRevenue)
                .Take(10)
                .ToListAsync();

            var revenueCompanyIds = companyRevenue.Select(c => c.CompanyID).ToList();
            var revenueCompanyEntities = await _context.Companies
                .Where(c => revenueCompanyIds.Contains(c.CompanyID))
                .ToListAsync();

            var revenueCompanySubs = await _context.Subscriptions
                .Include(s => s.Plan)
                .Where(s => revenueCompanyIds.Contains(s.CompanyID))
                .ToListAsync();

            var topCompaniesByRevenue = companyRevenue.Select(cr =>
            {
                var comp = revenueCompanyEntities.FirstOrDefault(c => c.CompanyID == cr.CompanyID);
                var sub = revenueCompanySubs.FirstOrDefault(s => s.CompanyID == cr.CompanyID && (s.Status == "Active" || s.Status == "GracePeriod"));
                var userCount = companyUsers.FirstOrDefault(u => u.CompanyID == cr.CompanyID)?.UserCount ?? 0;
                return new TopCompanyItem
                {
                    CompanyID = cr.CompanyID,
                    CompanyName = comp?.CompanyName ?? "Unknown",
                    Industry = comp?.Industry ?? "-",
                    PlanName = sub?.Plan?.PlanName ?? "No Plan",
                    UserCount = userCount,
                    TotalRevenue = cr.TotalRevenue
                };
            }).ToList();

            return new PlatformReportResult
            {
                Success = true,
                TotalCompanies = totalCompanies,
                ActiveCompanies = activeCompanies,
                InactiveCompanies = inactiveCompanies,
                NewCompaniesThisPeriod = newCompaniesThisPeriod,
                TotalUsers = totalUsers,
                ActiveUsers = activeUsers,
                TotalSubscriptions = totalSubscriptions,
                ActiveSubscriptions = activeSubs,
                TrialSubscriptions = trialSubs,
                ExpiredSubscriptions = expiredSubs,
                CancelledSubscriptions = cancelledSubs,
                MonthlyRecurringRevenue = mrr,
                PlanDistribution = planDistribution,
                TotalRevenue = totalRevenue,
                PendingAmount = pendingAmount,
                CompletedPayments = completedCount,
                PendingPayments = pendingCount,
                FailedPayments = failedCount,
                MonthlyRevenue = monthlyRevenue,
                TotalProjects = totalProjects,
                ActiveProjects = activeProjects,
                TotalTasks = totalTasks,
                CompletedTasks = completedTasks,
                OverdueTasks = overdueTasks,
                TopCompaniesByUsers = topCompaniesByUsers,
                TopCompaniesByRevenue = topCompaniesByRevenue,
                GeneratedAt = now,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating platform report");
            return new PlatformReportResult { Success = false, ErrorMessage = "An error occurred generating the report." };
        }
    }
}
