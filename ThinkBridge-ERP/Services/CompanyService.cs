using Microsoft.EntityFrameworkCore;
using ThinkBridge_ERP.Data;
using ThinkBridge_ERP.Models.Entities;
using ThinkBridge_ERP.Services.Interfaces;

namespace ThinkBridge_ERP.Services;

public class CompanyService : ICompanyService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CompanyService> _logger;

    public CompanyService(ApplicationDbContext context, ILogger<CompanyService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<DashboardStatsResult> GetDashboardStatsAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var totalCompanies = await _context.Companies.CountAsync();
            var activeSubscriptions = await _context.Subscriptions
                .Where(s => s.Status == "Active")
                .CountAsync();
            var totalUsers = await _context.Users.CountAsync();
            var monthlyRevenue = await _context.PaymentTransactions
                .Where(p => (p.Status == "Completed" || p.Status == "Paid") && p.CreatedAt >= startOfMonth)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;
            var newCompaniesThisMonth = await _context.Companies
                .Where(c => c.CreatedAt >= startOfMonth)
                .CountAsync();

            var recentCompanies = await _context.Companies
                .OrderByDescending(c => c.CreatedAt)
                .Take(5)
                .Select(c => new CompanyListItem
                {
                    CompanyID = c.CompanyID,
                    CompanyName = c.CompanyName,
                    Industry = c.Industry,
                    Status = c.Status,
                    CreatedAt = c.CreatedAt,
                    UserCount = c.Users.Count(),
                    PlanName = c.Subscriptions
                        .Where(s => s.Status == "Active" || s.Status == "Trial" || s.Status == "GracePeriod")
                        .OrderByDescending(s => s.StartDate)
                        .Select(s => s.Plan.PlanName)
                        .FirstOrDefault(),
                    AdminEmail = c.Users
                        .Where(u => _context.UserRoles.Any(ur => ur.UserID == u.UserID && ur.Role.RoleName == "CompanyAdmin"))
                        .Select(u => u.Email)
                        .FirstOrDefault(),
                    AdminName = c.Users
                        .Where(u => _context.UserRoles.Any(ur => ur.UserID == u.UserID && ur.Role.RoleName == "CompanyAdmin"))
                        .Select(u => u.Fname + " " + u.Lname)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var recentPayments = await _context.PaymentTransactions
                .OrderByDescending(p => p.CreatedAt)
                .Take(5)
                .Select(p => new RecentPayment
                {
                    PaymentId = p.PaymentID,
                    CompanyName = p.Subscription.Company.CompanyName,
                    Amount = p.Amount,
                    Status = p.Status,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();

            return new DashboardStatsResult
            {
                Success = true,
                TotalCompanies = totalCompanies,
                ActiveSubscriptions = activeSubscriptions,
                TotalUsers = totalUsers,
                MonthlyRevenue = monthlyRevenue,
                NewCompaniesThisMonth = newCompaniesThisMonth,
                RecentCompanies = recentCompanies,
                RecentPayments = recentPayments
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard stats");
            return new DashboardStatsResult
            {
                Success = false,
                ErrorMessage = "An error occurred while retrieving dashboard statistics."
            };
        }
    }

    public async Task<CompanyListResult> GetCompaniesAsync(CompanyFilterRequest filter)
    {
        try
        {
            var query = _context.Companies.AsQueryable();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var searchLower = filter.SearchTerm.ToLower();
                query = query.Where(c => c.CompanyName.ToLower().Contains(searchLower)
                    || (c.Industry != null && c.Industry.ToLower().Contains(searchLower)));
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                query = query.Where(c => c.Status.ToLower() == filter.Status.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(filter.PlanName))
            {
                query = query.Where(c => c.Subscriptions.Any(s =>
                    (s.Status == "Active" || s.Status == "Trial" || s.Status == "GracePeriod") &&
                    s.Plan.PlanName.ToLower() == filter.PlanName.ToLower()));
            }

            var totalCount = await query.CountAsync();

            var companies = await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(c => new CompanyListItem
                {
                    CompanyID = c.CompanyID,
                    CompanyName = c.CompanyName,
                    Industry = c.Industry,
                    Status = c.Status,
                    CreatedAt = c.CreatedAt,
                    UserCount = c.Users.Count(),
                    PlanName = c.Subscriptions
                        .Where(s => s.Status == "Active" || s.Status == "Trial" || s.Status == "GracePeriod")
                        .OrderByDescending(s => s.StartDate)
                        .Select(s => s.Plan.PlanName)
                        .FirstOrDefault(),
                    AdminEmail = c.Users
                        .Where(u => _context.UserRoles.Any(ur => ur.UserID == u.UserID && ur.Role.RoleName == "CompanyAdmin"))
                        .Select(u => u.Email)
                        .FirstOrDefault(),
                    AdminName = c.Users
                        .Where(u => _context.UserRoles.Any(ur => ur.UserID == u.UserID && ur.Role.RoleName == "CompanyAdmin"))
                        .Select(u => u.Fname + " " + u.Lname)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return new CompanyListResult
            {
                Success = true,
                Companies = companies,
                TotalCount = totalCount,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting companies list");
            return new CompanyListResult
            {
                Success = false,
                ErrorMessage = "An error occurred while retrieving companies."
            };
        }
    }

    public async Task<CompanyDetailResult> GetCompanyByIdAsync(int companyId)
    {
        try
        {
            var company = await _context.Companies
                .Include(c => c.Users)
                .Include(c => c.Subscriptions)
                    .ThenInclude(s => s.Plan)
                .Include(c => c.Projects)
                .FirstOrDefaultAsync(c => c.CompanyID == companyId);

            if (company == null)
            {
                return new CompanyDetailResult
                {
                    Success = false,
                    ErrorMessage = "Company not found."
                };
            }

            var activeSubscription = company.Subscriptions
                .Where(s => s.Status == "Active" || s.Status == "Trial" || s.Status == "GracePeriod")
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefault();

            var adminUser = await _context.Users
                .Where(u => u.CompanyID == companyId)
                .Where(u => _context.UserRoles.Any(ur => ur.UserID == u.UserID && ur.Role.RoleName == "CompanyAdmin"))
                .FirstOrDefaultAsync();

            return new CompanyDetailResult
            {
                Success = true,
                Company = company,
                ActiveSubscription = activeSubscription,
                UserCount = company.Users.Count,
                ProjectCount = company.Projects.Count,
                AdminUser = adminUser
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting company {CompanyId}", companyId);
            return new CompanyDetailResult
            {
                Success = false,
                ErrorMessage = "An error occurred while retrieving the company."
            };
        }
    }

    public async Task<CreateCompanyResult> CreateCompanyAsync(CreateCompanyRequest request)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Check if admin email already exists
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Admin.Email.ToLower());

                if (existingUser != null)
                {
                    return new CreateCompanyResult
                    {
                        Success = false,
                        ErrorMessage = "A user with this email address already exists."
                    };
                }

                // Create the company
                var company = new Company
                {
                    CompanyName = request.CompanyName,
                    Industry = request.Industry,
                    Status = request.Status,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Companies.Add(company);
                await _context.SaveChangesAsync();

                // Generate temporary password with company name
                var tempPassword = GenerateTemporaryPassword(request.CompanyName);
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(tempPassword);

                // Create the company admin user
                var adminUser = new User
                {
                    CompanyID = company.CompanyID,
                    Fname = request.Admin.FirstName,
                    Lname = request.Admin.LastName,
                    Email = request.Admin.Email,
                    Password = hashedPassword,
                    Phone = request.Admin.Phone,
                    AvatarColor = "#0B4F6C",
                    IsSuperAdmin = false,
                    Status = "Active",
                    MustChangePassword = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Users.Add(adminUser);
                await _context.SaveChangesAsync();

                // Assign CompanyAdmin role
                var companyAdminRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "CompanyAdmin");
                if (companyAdminRole != null)
                {
                    var userRole = new UserRole
                    {
                        UserID = adminUser.UserID,
                        RoleID = companyAdminRole.RoleID,
                        AssignedAt = DateTime.UtcNow
                    };
                    _context.UserRoles.Add(userRole);
                }

                // Create subscription
                var subscription = new Subscription
                {
                    CompanyID = company.CompanyID,
                    PlanID = request.PlanId,
                    Status = request.PlanId == 1 ? "Trial" : "Active", // Plan 1 is Trial
                    StartDate = DateTime.UtcNow,
                    EndDate = request.PlanId == 1
                        ? DateTime.UtcNow.AddDays(14)
                        : DateTime.UtcNow.AddMonths(1)
                };

                _context.Subscriptions.Add(subscription);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation("Created company {CompanyName} with admin {AdminEmail}",
                    company.CompanyName, adminUser.Email);

                return new CreateCompanyResult
                {
                    Success = true,
                    CompanyId = company.CompanyID,
                    AdminUserId = adminUser.UserID,
                    TemporaryPassword = tempPassword
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating company {CompanyName}", request.CompanyName);
                return new CreateCompanyResult
                {
                    Success = false,
                    ErrorMessage = "An error occurred while creating the company."
                };
            }
        });
    }

    public async Task<UpdateCompanyResult> UpdateCompanyAsync(int companyId, UpdateCompanyRequest request)
    {
        try
        {
            var company = await _context.Companies.FindAsync(companyId);
            if (company == null)
            {
                return new UpdateCompanyResult
                {
                    Success = false,
                    ErrorMessage = "Company not found."
                };
            }

            if (!string.IsNullOrWhiteSpace(request.CompanyName))
                company.CompanyName = request.CompanyName;

            if (request.Industry != null)
                company.Industry = request.Industry;

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                company.Status = request.Status;
                await CompanyUserStatusSync.ApplyForCompanyStatusAsync(_context, companyId, request.Status);
            }

            // Update admin email and phone if provided
            if (request.AdminEmail != null || request.AdminPhone != null)
            {
                var admin = await _context.Users
                    .Where(u => u.CompanyID == companyId && _context.UserRoles.Any(ur => ur.UserID == u.UserID && ur.Role.RoleName == "CompanyAdmin"))
                    .FirstOrDefaultAsync();
                if (admin != null)
                {
                    if (!string.IsNullOrWhiteSpace(request.AdminEmail))
                        admin.Email = request.AdminEmail;
                    if (request.AdminPhone != null)
                        admin.Phone = request.AdminPhone;
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated company {CompanyId}", companyId);

            return new UpdateCompanyResult
            {
                Success = true,
                Company = company
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating company {CompanyId}", companyId);
            return new UpdateCompanyResult
            {
                Success = false,
                ErrorMessage = "An error occurred while updating the company."
            };
        }
    }

    public async Task<ServiceResult> UpdateCompanyStatusAsync(int companyId, string status)
    {
        try
        {
            var company = await _context.Companies.FindAsync(companyId);
            if (company == null)
            {
                return new ServiceResult
                {
                    Success = false,
                    ErrorMessage = "Company not found."
                };
            }

            company.Status = status;
            await CompanyUserStatusSync.ApplyForCompanyStatusAsync(_context, companyId, status);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated company {CompanyId} status to {Status}", companyId, status);

            return new ServiceResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating company status {CompanyId}", companyId);
            return new ServiceResult
            {
                Success = false,
                ErrorMessage = "An error occurred while updating company status."
            };
        }
    }

    private static string GenerateTemporaryPassword(string companyName)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        const string specials = "!@#$%";
        var random = new Random();

        // Clean company name - remove spaces and special characters, take first 8 chars
        var cleanName = new string(companyName
            .Where(c => char.IsLetterOrDigit(c))
            .Take(8)
            .ToArray());

        // Generate random suffix (4 chars + 1 special + 1 number)
        var suffix = new char[6];
        for (int i = 0; i < 4; i++)
        {
            suffix[i] = chars[random.Next(chars.Length)];
        }
        suffix[4] = specials[random.Next(specials.Length)];
        suffix[5] = (char)('0' + random.Next(10));

        // Format: CompanyName_RandomChars
        return $"{cleanName}_{new string(suffix)}";
    }
}
