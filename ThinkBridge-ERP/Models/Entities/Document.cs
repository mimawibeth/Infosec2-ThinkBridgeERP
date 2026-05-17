using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ThinkBridge_ERP.Models.Entities;

[Table("Document")]
public class Document
{
    [Key]
    public int DocumentID { get; set; }

    [Required]
    public int CompanyID { get; set; }

    [Required]
    public int FolderID { get; set; }

    public int? ProjectID { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    [StringLength(20)]
    public string FileType { get; set; } = string.Empty;

    [Required]
    public int UploadedBy { get; set; }

    [Required]
    [StringLength(20)]
    public string ApprovalStatus { get; set; } = "Pending";

    public int? ApprovedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? PublishedAt { get; set; }

    // Navigation properties
    [ForeignKey("CompanyID")]
    public virtual Company Company { get; set; } = null!;

    [ForeignKey("FolderID")]
    public virtual Folder Folder { get; set; } = null!;

    [ForeignKey("ProjectID")]
    public virtual Project? Project { get; set; }

    [ForeignKey("UploadedBy")]
    public virtual User Uploader { get; set; } = null!;

    [ForeignKey("ApprovedBy")]
    public virtual User? Approver { get; set; }

    public virtual ICollection<DocumentAccess> DocumentAccesses { get; set; } = new List<DocumentAccess>();
    public virtual ICollection<DocumentTag> DocumentTags { get; set; } = new List<DocumentTag>();
    public virtual ICollection<DocumentReference> DocumentReferences { get; set; } = new List<DocumentReference>();
    public virtual ICollection<DocumentVersion> DocumentVersions { get; set; } = new List<DocumentVersion>();
    public virtual ICollection<PostDocument> PostDocuments { get; set; } = new List<PostDocument>();
}
