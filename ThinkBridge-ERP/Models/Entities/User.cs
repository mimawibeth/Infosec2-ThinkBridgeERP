using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ThinkBridge_ERP.Models.Entities;

[Table("User")]
public class User
{
    [Key]
    public int UserID { get; set; }

    public int? CompanyID { get; set; }

    [Required]
    [StringLength(150)]
    public string Fname { get; set; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Lname { get; set; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string Password { get; set; } = string.Empty;

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(255)]
    public string? AvatarUrl { get; set; }

    [StringLength(7)]
    public string AvatarColor { get; set; } = "#0B4F6C";

    public bool IsSuperAdmin { get; set; } = false;

    [Required]
    [StringLength(20)]
    public string Status { get; set; } = "Active";

    public bool MustChangePassword { get; set; } = true;

    public bool HasCompletedOnboarding { get; set; } = false;

    public int FailedLoginAttempts { get; set; } = 0;

    public DateTime? LockoutEnd { get; set; }

    public int LockoutLevel { get; set; } = 0;

    public bool IsPermanentlyLocked { get; set; } = false;

    public DateTime? PermanentlyLockedAt { get; set; }

    public bool IsTotpEnabled { get; set; } = false;

    [StringLength(128)]
    public string? TotpSecret { get; set; }

    public string? TotpBackupCodes { get; set; }

    public int TotpFailedAttempts { get; set; } = 0;

    public DateTime? TotpLockoutUntil { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("CompanyID")]
    public virtual Company? Company { get; set; }

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public virtual ICollection<Project> CreatedProjects { get; set; } = new List<Project>();
    public virtual ICollection<ProjectMember> ProjectMemberships { get; set; } = new List<ProjectMember>();
    public virtual ICollection<Task> CreatedTasks { get; set; } = new List<Task>();
    public virtual ICollection<TaskAssignment> TaskAssignments { get; set; } = new List<TaskAssignment>();
    public virtual ICollection<TaskUpdate> TaskUpdates { get; set; } = new List<TaskUpdate>();
    public virtual ICollection<ProductHistory> ProductHistories { get; set; } = new List<ProductHistory>();
    public virtual ICollection<ChangeRequest> ChangeRequests { get; set; } = new List<ChangeRequest>();
    public virtual ICollection<Document> UploadedDocuments { get; set; } = new List<Document>();
    public virtual ICollection<Document> ApprovedDocuments { get; set; } = new List<Document>();
    public virtual ICollection<DocumentVersion> DocumentVersions { get; set; } = new List<DocumentVersion>();
    public virtual ICollection<Post> Posts { get; set; } = new List<Post>();
    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public virtual ICollection<TaskComment> TaskComments { get; set; } = new List<TaskComment>();
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();
    public virtual ICollection<CalendarEvent> CalendarEvents { get; set; } = new List<CalendarEvent>();

    // Computed property
    [NotMapped]
    public string FullName => $"{Fname} {Lname}";
}
