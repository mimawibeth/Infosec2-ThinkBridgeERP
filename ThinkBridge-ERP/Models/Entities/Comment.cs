using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ThinkBridge_ERP.Models.Entities;

[Table("Comment")]
public class Comment
{
    [Key]
    public int CommentID { get; set; }

    [Required]
    public int PostID { get; set; }

    [Required]
    public int UserID { get; set; }

    [Required]
    [StringLength(5000)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("PostID")]
    public virtual Post Post { get; set; } = null!;

    [ForeignKey("UserID")]
    public virtual User User { get; set; } = null!;
}
