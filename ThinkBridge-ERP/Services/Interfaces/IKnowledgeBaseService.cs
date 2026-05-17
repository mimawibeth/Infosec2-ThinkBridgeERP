namespace ThinkBridge_ERP.Services.Interfaces;

public interface IKnowledgeBaseService
{
    // Articles (Documents)
    Task<ArticleListResult> GetArticlesAsync(int companyId, int userId, string userRole, ArticleFilterRequest filter);
    Task<ArticleDetailResult> GetArticleByIdAsync(int companyId, int userId, string userRole, int documentId);
    Task<CreateArticleResult> CreateArticleAsync(int companyId, int userId, string userRole, CreateArticleRequest request);
    Task<ServiceResult> UpdateArticleAsync(int companyId, int userId, string userRole, int documentId, UpdateArticleRequest request);
    Task<ServiceResult> ArchiveArticleAsync(int companyId, int userId, string userRole, int documentId);
    Task<ServiceResult> RestoreArticleAsync(int companyId, int userId, string userRole, int documentId);

    // Approval Workflow
    Task<ArticleListResult> GetPendingArticlesAsync(int companyId, int userId, string userRole, ArticleFilterRequest filter);
    Task<ServiceResult> ApproveArticleAsync(int companyId, int userId, string userRole, int documentId);
    Task<ServiceResult> RejectArticleAsync(int companyId, int userId, string userRole, int documentId, string? reason);
    Task<ServiceResult> RequestRevisionAsync(int companyId, int userId, string userRole, int documentId, string? reason);

    // Categories (Folders)
    Task<CategoryListResult> GetCategoriesAsync(int companyId);
    Task<CreateCategoryResult> CreateCategoryAsync(int companyId, int userId, string userRole, string categoryName);

    // Tags
    Task<TagListResult> GetTagsAsync(int companyId);

    // Comments (feedback on articles)
    Task<ArticleCommentListResult> GetArticleCommentsAsync(int companyId, int documentId);
    Task<CreateArticleCommentResult> AddArticleCommentAsync(int companyId, int userId, int documentId, string content);

    // Stats
    Task<KbStatsResult> GetKbStatsAsync(int companyId, int userId, string userRole);
}

// ─── Request DTOs ──────────────────────────────────────

public class ArticleFilterRequest
{
    public int? FolderId { get; set; }
    public string? Status { get; set; } // Draft, Pending, Approved, Rejected
    public string? SearchTerm { get; set; }
    public string? Tag { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class CreateArticleRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Content { get; set; } = string.Empty;
    public int FolderId { get; set; }
    public int? ProjectId { get; set; }
    public string FileType { get; set; } = "Article";
    public List<string> Tags { get; set; } = new();
    public List<ArticleReferenceRequest> References { get; set; } = new();
    public bool SaveAsDraft { get; set; } = false;
}

public class UpdateArticleRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Content { get; set; }
    public int? FolderId { get; set; }
    public int? ProjectId { get; set; }
    public List<string>? Tags { get; set; }
    public List<ArticleReferenceRequest>? References { get; set; }
    public bool? SubmitForApproval { get; set; }
}

public class ArticleReferenceRequest
{
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Author { get; set; }
    public string? SourceName { get; set; }
    public string? PublishedDateText { get; set; }
    public string? Notes { get; set; }
    public int? SortOrder { get; set; }
}

// ─── Response DTOs ──────────────────────────────────────

public class ArticleListResult : ServiceResult
{
    public List<ArticleItem> Articles { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

public class ArticleItem
{
    public int DocumentID { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int FolderID { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorInitials { get; set; } = string.Empty;
    public string AuthorAvatarColor { get; set; } = "#0B4F6C";
    public string? ApproverName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public List<string> Tags { get; set; } = new();
    public int ReferenceCount { get; set; }
    public int CommentCount { get; set; }
}

public class ArticleDetailResult : ServiceResult
{
    public ArticleDetailItem? Article { get; set; }
}

public class ArticleDetailItem : ArticleItem
{
    public string Content { get; set; } = string.Empty;
    public int? ProjectID { get; set; }
    public string? ProjectName { get; set; }
    public int UploadedBy { get; set; }
    public string FileType { get; set; } = string.Empty;
    public List<ArticleReferenceItem> References { get; set; } = new();
}

public class ArticleReferenceItem
{
    public int ReferenceID { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Author { get; set; }
    public string? SourceName { get; set; }
    public string? PublishedDateText { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}

public class CreateArticleResult : ServiceResult
{
    public int? DocumentId { get; set; }
}

public class CategoryListResult : ServiceResult
{
    public List<CategoryItem> Categories { get; set; } = new();
}

public class CategoryItem
{
    public int FolderID { get; set; }
    public string FolderName { get; set; } = string.Empty;
    public int ArticleCount { get; set; }
}

public class CreateCategoryResult : ServiceResult
{
    public int? FolderId { get; set; }
}

public class TagListResult : ServiceResult
{
    public List<TagItem> Tags { get; set; } = new();
}

public class TagItem
{
    public int TagID { get; set; }
    public string TagName { get; set; } = string.Empty;
}

public class ArticleCommentListResult : ServiceResult
{
    public List<ArticleCommentItem> Comments { get; set; } = new();
}

public class ArticleCommentItem
{
    public int CommentID { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorInitials { get; set; } = string.Empty;
    public string AuthorAvatarColor { get; set; } = "#0B4F6C";
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateArticleCommentResult : ServiceResult
{
    public int? CommentId { get; set; }
}

public class KbStatsResult : ServiceResult
{
    public int TotalArticles { get; set; }
    public int ApprovedArticles { get; set; }
    public int PendingArticles { get; set; }
    public int DraftArticles { get; set; }
    public int RejectedArticles { get; set; }
    public int TotalCategories { get; set; }
}
