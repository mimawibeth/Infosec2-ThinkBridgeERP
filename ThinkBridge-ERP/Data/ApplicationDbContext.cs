using Microsoft.EntityFrameworkCore;
using ThinkBridge_ERP.Models.Entities;
using Task = ThinkBridge_ERP.Models.Entities.Task;

namespace ThinkBridge_ERP.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Core / Security
    public DbSet<Company> Companies { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }
    public DbSet<Module> Modules { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<SystemSetting> SystemSettings { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    // Billing
    public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<PaymentTransaction> PaymentTransactions { get; set; }

    // Project Management
    public DbSet<Project> Projects { get; set; }
    public DbSet<ProjectMember> ProjectMembers { get; set; }
    public DbSet<ProjectCategory> ProjectCategories { get; set; }
    public DbSet<Task> Tasks { get; set; }
    public DbSet<TaskAssignment> TaskAssignments { get; set; }
    public DbSet<TaskUpdate> TaskUpdates { get; set; }
    public DbSet<TaskComment> TaskComments { get; set; }

    // PLM
    public DbSet<Product> Products { get; set; }
    public DbSet<ProductVersion> ProductVersions { get; set; }
    public DbSet<LifecycleStage> LifecycleStages { get; set; }
    public DbSet<ProductHistory> ProductHistories { get; set; }
    public DbSet<ChangeRequest> ChangeRequests { get; set; }

    // KMS
    public DbSet<Folder> Folders { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<DocumentAccess> DocumentAccesses { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<DocumentTag> DocumentTags { get; set; }
    public DbSet<DocumentReference> DocumentReferences { get; set; }
    public DbSet<DocumentVersion> DocumentVersions { get; set; }

    // Collaboration
    public DbSet<Post> Posts { get; set; }
    public DbSet<PostDocument> PostDocuments { get; set; }
    public DbSet<Comment> Comments { get; set; }
    public DbSet<Notification> Notifications { get; set; }

    // Reports
    public DbSet<Report> Reports { get; set; }

    // Calendar
    public DbSet<CalendarEvent> CalendarEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure unique index for User email
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("UX_User_Email");

        modelBuilder.Entity<AuditLog>()
            .Property(a => a.LogType)
            .HasMaxLength(20)
            .HasDefaultValue("System");

        modelBuilder.Entity<User>()
            .Property(u => u.IsTotpEnabled)
            .HasDefaultValue(false);

        modelBuilder.Entity<User>()
            .Property(u => u.TotpFailedAttempts)
            .HasDefaultValue(0);

        // Configure User - Company relationship (optional for SuperAdmin)
        modelBuilder.Entity<User>()
            .HasOne(u => u.Company)
            .WithMany(c => c.Users)
            .HasForeignKey(u => u.CompanyID)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Project - Company relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<Project>()
            .HasOne(p => p.Company)
            .WithMany(c => c.Projects)
            .HasForeignKey(p => p.CompanyID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure Subscription - Company relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<Subscription>()
            .HasOne(s => s.Company)
            .WithMany(c => c.Subscriptions)
            .HasForeignKey(s => s.CompanyID)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Subscription>()
            .Property(s => s.GracePeriodDays)
            .HasDefaultValue(7);

        // Configure AuditLog - Company relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.Company)
            .WithMany(c => c.AuditLogs)
            .HasForeignKey(a => a.CompanyID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure Product - Company relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<Product>()
            .HasOne(p => p.Company)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CompanyID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure Product - Project relationship (optional, NoAction to avoid cascade cycle)
        modelBuilder.Entity<Product>()
            .HasOne(p => p.Project)
            .WithMany(p => p.Products)
            .HasForeignKey(p => p.ProjectID)
            .OnDelete(DeleteBehavior.SetNull);

        // Configure Folder - Company relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<Folder>()
            .HasOne(f => f.Company)
            .WithMany(c => c.Folders)
            .HasForeignKey(f => f.CompanyID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure Tag - Company relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<Tag>()
            .HasOne(t => t.Company)
            .WithMany(c => c.Tags)
            .HasForeignKey(t => t.CompanyID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure Report - Company relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<Report>()
            .HasOne(r => r.Company)
            .WithMany(c => c.Reports)
            .HasForeignKey(r => r.CompanyID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure SystemSetting - Company relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<SystemSetting>()
            .HasOne(s => s.Company)
            .WithMany(c => c.SystemSettings)
            .HasForeignKey(s => s.CompanyID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure Project - Creator relationship
        modelBuilder.Entity<Project>()
            .HasOne(p => p.Creator)
            .WithMany(u => u.CreatedProjects)
            .HasForeignKey(p => p.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Task - Creator relationship
        modelBuilder.Entity<Task>()
            .HasOne(t => t.Creator)
            .WithMany(u => u.CreatedTasks)
            .HasForeignKey(t => t.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Task - Project relationship
        modelBuilder.Entity<Task>()
            .HasOne(t => t.Project)
            .WithMany(p => p.Tasks)
            .HasForeignKey(t => t.ProjectID)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure Document - Company relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<Document>()
            .HasOne(d => d.Company)
            .WithMany(c => c.Documents)
            .HasForeignKey(d => d.CompanyID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure Document - Folder relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<Document>()
            .HasOne(d => d.Folder)
            .WithMany(f => f.Documents)
            .HasForeignKey(d => d.FolderID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure Document - Uploader relationship
        modelBuilder.Entity<Document>()
            .HasOne(d => d.Uploader)
            .WithMany(u => u.UploadedDocuments)
            .HasForeignKey(d => d.UploadedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Document - Approver relationship
        modelBuilder.Entity<Document>()
            .HasOne(d => d.Approver)
            .WithMany(u => u.ApprovedDocuments)
            .HasForeignKey(d => d.ApprovedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Document - Project relationship
        modelBuilder.Entity<Document>()
            .HasOne(d => d.Project)
            .WithMany(p => p.Documents)
            .HasForeignKey(d => d.ProjectID)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DocumentReference>()
            .HasOne(r => r.Document)
            .WithMany(d => d.DocumentReferences)
            .HasForeignKey(r => r.DocumentID)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentReference>()
            .Property(r => r.SortOrder)
            .HasDefaultValue(0);

        modelBuilder.Entity<DocumentReference>()
            .HasIndex(r => new { r.DocumentID, r.SortOrder })
            .HasDatabaseName("IX_DocumentReference_DocumentID_SortOrder");

        // Configure Folder self-reference
        modelBuilder.Entity<Folder>()
            .HasOne(f => f.ParentFolder)
            .WithMany(f => f.SubFolders)
            .HasForeignKey(f => f.ParentFolderID)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Post - Company relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<Post>()
            .HasOne(p => p.Company)
            .WithMany(c => c.Posts)
            .HasForeignKey(p => p.CompanyID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure Post - Creator relationship
        modelBuilder.Entity<Post>()
            .HasOne(p => p.Creator)
            .WithMany(u => u.Posts)
            .HasForeignKey(p => p.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Post - Project relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<Post>()
            .HasOne(p => p.Project)
            .WithMany(pr => pr.Posts)
            .HasForeignKey(p => p.ProjectID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure Comment - User relationship
        modelBuilder.Entity<Comment>()
            .HasOne(c => c.User)
            .WithMany(u => u.Comments)
            .HasForeignKey(c => c.UserID)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Comment - Post relationship
        modelBuilder.Entity<Comment>()
            .HasOne(c => c.Post)
            .WithMany(p => p.Comments)
            .HasForeignKey(c => c.PostID)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure AuditLog - User relationship
        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.User)
            .WithMany(u => u.AuditLogs)
            .HasForeignKey(a => a.UserID)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Report - Creator relationship
        modelBuilder.Entity<Report>()
            .HasOne(r => r.Creator)
            .WithMany(u => u.Reports)
            .HasForeignKey(r => r.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure ChangeRequest - Requester relationship
        modelBuilder.Entity<ChangeRequest>()
            .HasOne(cr => cr.Requester)
            .WithMany(u => u.ChangeRequests)
            .HasForeignKey(cr => cr.RequestedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure ProductHistory - User relationship
        modelBuilder.Entity<ProductHistory>()
            .HasOne(ph => ph.User)
            .WithMany(u => u.ProductHistories)
            .HasForeignKey(ph => ph.ChangedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure TaskAssignment relationships
        modelBuilder.Entity<TaskAssignment>()
            .HasOne(ta => ta.User)
            .WithMany(u => u.TaskAssignments)
            .HasForeignKey(ta => ta.UserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskAssignment>()
            .HasOne(ta => ta.Task)
            .WithMany(t => t.TaskAssignments)
            .HasForeignKey(ta => ta.TaskID)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure TaskUpdate relationships
        modelBuilder.Entity<TaskUpdate>()
            .HasOne(tu => tu.User)
            .WithMany(u => u.TaskUpdates)
            .HasForeignKey(tu => tu.UserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskUpdate>()
            .HasOne(tu => tu.Task)
            .WithMany(t => t.TaskUpdates)
            .HasForeignKey(tu => tu.TaskID)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure TaskComment relationships
        modelBuilder.Entity<TaskComment>()
            .HasOne(tc => tc.User)
            .WithMany(u => u.TaskComments)
            .HasForeignKey(tc => tc.UserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskComment>()
            .HasOne(tc => tc.Task)
            .WithMany(t => t.TaskComments)
            .HasForeignKey(tc => tc.TaskID)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure ProjectMember relationships
        modelBuilder.Entity<ProjectMember>()
            .HasOne(pm => pm.User)
            .WithMany(u => u.ProjectMemberships)
            .HasForeignKey(pm => pm.UserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProjectMember>()
            .HasOne(pm => pm.Project)
            .WithMany(p => p.ProjectMembers)
            .HasForeignKey(pm => pm.ProjectID)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure DocumentVersion - Uploader relationship
        modelBuilder.Entity<DocumentVersion>()
            .HasOne(dv => dv.Uploader)
            .WithMany(u => u.DocumentVersions)
            .HasForeignKey(dv => dv.UploadedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure CalendarEvent - Company relationship (NoAction to avoid cascade cycle)
        modelBuilder.Entity<CalendarEvent>()
            .HasOne(ce => ce.Company)
            .WithMany(c => c.CalendarEvents)
            .HasForeignKey(ce => ce.CompanyID)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure CalendarEvent - Creator relationship
        modelBuilder.Entity<CalendarEvent>()
            .HasOne(ce => ce.Creator)
            .WithMany(u => u.CalendarEvents)
            .HasForeignKey(ce => ce.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure CalendarEvent - Project relationship
        modelBuilder.Entity<CalendarEvent>()
            .HasOne(ce => ce.Project)
            .WithMany(p => p.CalendarEvents)
            .HasForeignKey(ce => ce.ProjectID)
            .OnDelete(DeleteBehavior.SetNull);

        // Seed default Roles
        modelBuilder.Entity<Role>().HasData(
            new Role { RoleID = 1, RoleName = "SuperAdmin", Description = "Platform administrator with full access" },
            new Role { RoleID = 2, RoleName = "CompanyAdmin", Description = "Company administrator with full company access" },
            new Role { RoleID = 3, RoleName = "ProjectManager", Description = "Manages projects and team members" },
            new Role { RoleID = 4, RoleName = "TeamMember", Description = "Regular team member" }
        );

        // Seed Lifecycle Stages
        modelBuilder.Entity<LifecycleStage>().HasData(
            new LifecycleStage { StageID = 1, StageName = "Concept", StageOrder = 1 },
            new LifecycleStage { StageID = 2, StageName = "Design", StageOrder = 2 },
            new LifecycleStage { StageID = 3, StageName = "Development", StageOrder = 3 },
            new LifecycleStage { StageID = 4, StageName = "Testing", StageOrder = 4 },
            new LifecycleStage { StageID = 5, StageName = "Production", StageOrder = 5 }
        );

        // Seed Subscription Plans
        modelBuilder.Entity<SubscriptionPlan>().HasData(
            new SubscriptionPlan { PlanID = 1, PlanName = "Trial", Price = 0.00m, BillingCycle = "Monthly", MaxUsers = 5, MaxProjects = 3, IsActive = false },
            new SubscriptionPlan { PlanID = 2, PlanName = "Starter", Price = 999.00m, BillingCycle = "Monthly", MaxUsers = 35, MaxProjects = 100, IsActive = true },
            new SubscriptionPlan { PlanID = 3, PlanName = "Professional", Price = 2499.00m, BillingCycle = "Monthly", MaxUsers = 75, MaxProjects = 500, IsActive = true },
            new SubscriptionPlan { PlanID = 4, PlanName = "Enterprise", Price = 4999.00m, BillingCycle = "Monthly", MaxUsers = null, MaxProjects = null, IsActive = true }
        );
    }
}
