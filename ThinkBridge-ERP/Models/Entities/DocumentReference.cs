using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ThinkBridge_ERP.Models.Entities;

[Table("DocumentReference")]
public class DocumentReference
{
    [Key]
    public int ReferenceID { get; set; }

    [Required]
    public int DocumentID { get; set; }

    [Required]
    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2048)]
    public string? Url { get; set; }

    [StringLength(200)]
    public string? Author { get; set; }

    [StringLength(200)]
    public string? SourceName { get; set; }

    [StringLength(50)]
    public string? PublishedDateText { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("DocumentID")]
    public virtual Document Document { get; set; } = null!;
}
