using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ThinkBridge_ERP.Models.Entities;

[Table("AuditLog")]
public class AuditLog
{
    [Key]
    public int LogID { get; set; }

    public int? CompanyID { get; set; }

    [Required]
    public int UserID { get; set; }

    [Required]
    [StringLength(120)]
    public string Action { get; set; } = string.Empty;

    [Required]
    [StringLength(60)]
    public string EntityName { get; set; } = string.Empty;

    [Required]
    public int EntityID { get; set; }

    [StringLength(45)]
    public string? IPAddress { get; set; }

    [Required]
    [StringLength(20)]
    public string LogType { get; set; } = "System";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("CompanyID")]
    public virtual Company? Company { get; set; }

    [ForeignKey("UserID")]
    public virtual User User { get; set; } = null!;
}
