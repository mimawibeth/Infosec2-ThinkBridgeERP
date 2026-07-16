using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using System.Security.Claims;
using System.Text.Json.Serialization;
using ThinkBridge_ERP.Data;
using UserEntity = ThinkBridge_ERP.Models.Entities.User;
using UserRoleEntity = ThinkBridge_ERP.Models.Entities.UserRole;
using ThinkBridge_ERP.Services;
using ThinkBridge_ERP.Services.Interfaces;

// QuestPDF community license (free for < $1M annual revenue)
QuestPDF.Settings.License = LicenseType.Community;
QuestPDF.Settings.UseEnvironmentFonts = false;
QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
    {
        options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

// Add Entity Framework Core with SQL Server
var defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(defaultConnectionString))
{
    if (builder.Environment.IsDevelopment())
    {
        defaultConnectionString = "Server=db41842.databaseasp.net; Database=db41842; User Id=db41842; Password=D@p5g8%W_Z7z; Encrypt=False; MultipleActiveResultSets=True;";
    }
    else
    {
        throw new InvalidOperationException(
            "The ConnectionString property has not been initialized. " +
            "Set ConnectionStrings__DefaultConnection in your hosting environment.");
    }
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(defaultConnectionString,
        sqlServerOptions => sqlServerOptions.EnableRetryOnFailure(
            maxRetryCount: 2,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null)));

// Register application services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IProductLifecycleService, ProductLifecycleService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ICollaborationService, CollaborationService>();
builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
builder.Services.AddScoped<ICalendarService, CalendarService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<ISuperAdminService, SuperAdminService>();
builder.Services.AddScoped<IPayMongoService, PayMongoService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddSingleton<PasswordValidationService>();
builder.Services.AddScoped<PdfReportService>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<SubscriptionExpirationService>();

// Configure cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Name = "ThinkBridge.Auth";
        options.Cookie.IsEssential = true;

        // Return 401 for API requests instead of redirecting to login page
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnValidatePrincipal = async context =>
            {
                var userIdValue = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdValue, out var userId) || userId == 0)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                try
                {
                    var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var userState = await db.Users
                        .AsNoTracking()
                        .Where(u => u.UserID == userId)
                        .Select(u => new
                        {
                            u.Status,
                            u.IsPermanentlyLocked,
                            u.LockoutEnd
                        })
                        .FirstOrDefaultAsync();

                    var isValid = userState != null
                        && SessionSecurityEvaluator.IsAccountSessionValid(userState.Status, userState.IsPermanentlyLocked, userState.LockoutEnd, DateTime.UtcNow);

                    if (!isValid)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                }
                catch (Exception ex)
                {
                    var loggerFactory = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("CookieSessionValidation");
                    logger.LogWarning(ex, "Failed to validate auth session for user {UserId}", userId);

                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminOnly", policy => policy.RequireRole("SuperAdmin"));
    options.AddPolicy("CompanyAdminOnly", policy => policy.RequireRole("CompanyAdmin", "SuperAdmin"));
    options.AddPolicy("ProjectManagerOnly", policy => policy.RequireRole("ProjectManager", "CompanyAdmin", "SuperAdmin"));
    options.AddPolicy("TeamMemberOnly", policy => policy.RequireRole("TeamMember", "ProjectManager", "CompanyAdmin", "SuperAdmin"));
});

var app = builder.Build();

await EnsureBootstrapSuperAdminsAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "landing",
    pattern: "",
    defaults: new { controller = "Subscription", action = "Landing" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

// Fix existing subscriptions with GracePeriodDays = 0 (migration default mismatch)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ThinkBridge_ERP.Data.ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE [Subscription] SET GracePeriodDays = 7 WHERE GracePeriodDays = 0");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Startup subscription normalization skipped (database not ready).");
    }
}

static async Task EnsureBootstrapSuperAdminsAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("BootstrapSuperAdmin");

    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    try
    {
        if (!await db.Database.CanConnectAsync())
        {
            logger.LogWarning("Bootstrap super admin creation skipped because the database is not reachable.");
            return;
        }

        if (await db.Users.AnyAsync(u => u.IsSuperAdmin))
        {
            return;
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Bootstrap super admin creation skipped because the database/schema is not ready (apply migrations and verify connection string).");
        return;
    }

    var primaryEmail = config["Security:Bootstrap:PrimaryEmail"];
    var primaryPassword = config["Security:Bootstrap:PrimaryPassword"];
    var backupEmail = config["Security:Bootstrap:BackupEmail"];
    var backupPassword = config["Security:Bootstrap:BackupPassword"];

    if (string.IsNullOrWhiteSpace(primaryEmail)
        || string.IsNullOrWhiteSpace(primaryPassword)
        || string.IsNullOrWhiteSpace(backupEmail)
        || string.IsNullOrWhiteSpace(backupPassword))
    {
        logger.LogWarning("Bootstrap super admin creation skipped because required configuration values are missing.");
        return;
    }

    var normalizedPrimaryEmail = primaryEmail.Trim().ToLowerInvariant();
    var normalizedBackupEmail = backupEmail.Trim().ToLowerInvariant();

    if (normalizedPrimaryEmail == normalizedBackupEmail)
    {
        logger.LogWarning("Bootstrap super admin creation skipped because primary and backup emails are identical.");
        return;
    }

    var emailAlreadyExists = await db.Users.AnyAsync(u =>
        u.Email.ToLower() == normalizedPrimaryEmail || u.Email.ToLower() == normalizedBackupEmail);

    if (emailAlreadyExists)
    {
        logger.LogWarning("Bootstrap super admin creation skipped because one or more emails already exist.");
        return;
    }

    var superAdminRoleId = await db.Roles
        .Where(r => r.RoleName == "SuperAdmin")
        .Select(r => r.RoleID)
        .FirstOrDefaultAsync();

    if (superAdminRoleId == 0)
    {
        logger.LogWarning("Bootstrap super admin creation skipped because the SuperAdmin role was not found.");
        return;
    }

    var now = DateTime.UtcNow;

    var primaryUser = new UserEntity
    {
        CompanyID = null,
        Fname = "Super",
        Lname = "Admin",
        Email = normalizedPrimaryEmail,
        Password = BCrypt.Net.BCrypt.HashPassword(primaryPassword),
        IsSuperAdmin = true,
        Status = "Active",
        MustChangePassword = true,
        CreatedAt = now
    };

    var backupUser = new UserEntity
    {
        CompanyID = null,
        Fname = "Backup",
        Lname = "Admin",
        Email = normalizedBackupEmail,
        Password = BCrypt.Net.BCrypt.HashPassword(backupPassword),
        IsSuperAdmin = true,
        Status = "Active",
        MustChangePassword = true,
        CreatedAt = now
    };

    db.Users.AddRange(primaryUser, backupUser);
    await db.SaveChangesAsync();

    db.UserRoles.AddRange(
        new UserRoleEntity
        {
            UserID = primaryUser.UserID,
            RoleID = superAdminRoleId,
            AssignedAt = now
        },
        new UserRoleEntity
        {
            UserID = backupUser.UserID,
            RoleID = superAdminRoleId,
            AssignedAt = now
        });

    await db.SaveChangesAsync();
    logger.LogInformation("Bootstrap super admin accounts created from configuration.");
}

app.Run();
