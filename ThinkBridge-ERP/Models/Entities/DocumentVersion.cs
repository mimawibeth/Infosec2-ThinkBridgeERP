using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ThinkBridge_ERP.Models.Entities;

[Table("DocumentVersion")]
public class DocumentVersion
{
    [Key]
    public int DocVersionID { get; set; }

    [Required]
    public int DocumentID { get; set; }

    [Required]
    [StringLength(30)]
    public string VersionLabel { get; set; } = string.Empty;

    [Required]
    [MaxLength(200000)]
    public string FilePath { get; set; } = string.Empty;

    public long? FileSize { get; set; }

    [Required]
    public int UploadedBy { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("DocumentID")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("UploadedBy")]
    public virtual User Uploader { get; set; } = null!;
}
