using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkBridge_ERP.Services.Interfaces;

namespace ThinkBridge_ERP.Controllers;

[ApiController]
[Route("api/knowledgebase")]
[Authorize(Policy = "TeamMemberOnly")]
[RequestSizeLimit(250_000)]
public class KnowledgeBaseController : ControllerBase
{
    private readonly IKnowledgeBaseService _kbService;
    private readonly ILogger<KnowledgeBaseController> _logger;

    public KnowledgeBaseController(IKnowledgeBaseService kbService, ILogger<KnowledgeBaseController> logger)
    {
        _kbService = kbService;
        _logger = logger;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    private int GetCurrentCompanyId()
    {
        var companyIdClaim = User.Claims.FirstOrDefault(c => c.Type == "CompanyID")?.Value;
        return int.TryParse(companyIdClaim, out var companyId) ? companyId : 0;
    }

    private string GetCurrentUserRole()
    {
        return User.Claims.FirstOrDefault(c => c.Type == "PrimaryRole")?.Value ?? "TeamMember";
    }

    // ──────────────────────────────────────────────
    // Articles
    // ──────────────────────────────────────────────

    /// <summary>
    /// Get articles with filtering and role-based access
    /// </summary>
    [HttpGet("articles")]
    public async Task<IActionResult> GetArticles(
        [FromQuery] int? folderId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? tag,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var filter = new ArticleFilterRequest
        {
            FolderId = folderId,
            Status = status,
            SearchTerm = search,
            Tag = tag,
            Page = page,
            PageSize = pageSize
        };

        var result = await _kbService.GetArticlesAsync(companyId, userId, role, filter);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new
        {
            success = true,
            data = result.Articles,
            pagination = new { page = result.Page, pageSize = result.PageSize, totalCount = result.TotalCount, totalPages = result.TotalPages }
        });
    }

    /// <summary>
    /// Get a single article by ID
    /// </summary>
    [HttpGet("articles/{id}")]
    public async Task<IActionResult> GetArticle(int id)
    {
        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var result = await _kbService.GetArticleByIdAsync(companyId, userId, role, id);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = result.Article });
    }

    /// <summary>
    /// Create a new article (CompanyAdmin & ProjectManager only)
    /// </summary>
    [HttpPost("articles")]
    [Authorize(Policy = "ProjectManagerOnly")]
    public async Task<IActionResult> CreateArticle([FromBody] CreateArticleRequest request)
    {
        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { success = false, message = "Title is required." });

        if (request.FolderId <= 0)
            return BadRequest(new { success = false, message = "Category is required." });

        if (!request.SaveAsDraft)
        {
            var validRefs = request.References?.Where(r =>
                !string.IsNullOrWhiteSpace(r.Title) && !string.IsNullOrWhiteSpace(r.Url)).ToList();
            if (validRefs == null || validRefs.Count == 0)
                return BadRequest(new { success = false, message = "At least one reference with a title and URL is required before submitting." });
        }

        var result = await _kbService.CreateArticleAsync(companyId, userId, role, request);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = new { documentId = result.DocumentId } });
    }

    /// <summary>
    /// Update an article (Author PM or CompanyAdmin only)
    /// </summary>
    [HttpPut("articles/{id}")]
    [Authorize(Policy = "ProjectManagerOnly")]
    public async Task<IActionResult> UpdateArticle(int id, [FromBody] UpdateArticleRequest request)
    {
        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var result = await _kbService.UpdateArticleAsync(companyId, userId, role, id, request);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true });
    }

    /// <summary>
    /// Archive an article (CompanyAdmin, or PM for own non-approved)
    /// </summary>
    [HttpPost("articles/{id}/archive")]
    [Authorize(Policy = "ProjectManagerOnly")]
    public async Task<IActionResult> ArchiveArticle(int id)
    {
        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var result = await _kbService.ArchiveArticleAsync(companyId, userId, role, id);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true });
    }

    /// <summary>
    /// Restore an archived article (CompanyAdmin only)
    /// </summary>
    [HttpPost("articles/{id}/restore")]
    [Authorize(Policy = "CompanyAdminOnly")]
    public async Task<IActionResult> RestoreArticle(int id)
    {
        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var result = await _kbService.RestoreArticleAsync(companyId, userId, role, id);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true });
    }

    // ──────────────────────────────────────────────
    // Approval Workflow (CompanyAdmin only)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Get pending articles for admin review
    /// </summary>
    [HttpGet("pending")]
    [Authorize(Policy = "CompanyAdminOnly")]
    public async Task<IActionResult> GetPendingArticles(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var filter = new ArticleFilterRequest { SearchTerm = search, Page = page, PageSize = pageSize };
        var result = await _kbService.GetPendingArticlesAsync(companyId, userId, role, filter);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new
        {
            success = true,
            data = result.Articles,
            pagination = new { page = result.Page, pageSize = result.PageSize, totalCount = result.TotalCount, totalPages = result.TotalPages }
        });
    }

    /// <summary>
    /// Approve an article
    /// </summary>
    [HttpPost("articles/{id}/approve")]
    [Authorize(Policy = "CompanyAdminOnly")]
    public async Task<IActionResult> ApproveArticle(int id)
    {
        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var result = await _kbService.ApproveArticleAsync(companyId, userId, role, id);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, message = "Article approved successfully." });
    }

    /// <summary>
    /// Reject an article
    /// </summary>
    [HttpPost("articles/{id}/reject")]
    [Authorize(Policy = "CompanyAdminOnly")]
    public async Task<IActionResult> RejectArticle(int id, [FromBody] ApprovalActionRequest? request)
    {
        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var result = await _kbService.RejectArticleAsync(companyId, userId, role, id, request?.Reason);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, message = "Article rejected." });
    }

    /// <summary>
    /// Request revision on an article
    /// </summary>
    [HttpPost("articles/{id}/request-revision")]
    [Authorize(Policy = "CompanyAdminOnly")]
    public async Task<IActionResult> RequestRevision(int id, [FromBody] ApprovalActionRequest? request)
    {
        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var result = await _kbService.RequestRevisionAsync(companyId, userId, role, id, request?.Reason);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, message = "Revision requested." });
    }

    // ──────────────────────────────────────────────
    // Categories
    // ──────────────────────────────────────────────

    /// <summary>
    /// Get all categories/folders
    /// </summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var companyId = GetCurrentCompanyId();
        if (companyId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var result = await _kbService.GetCategoriesAsync(companyId);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = result.Categories });
    }

    /// <summary>
    /// Create a new category (CompanyAdmin & PM only)
    /// </summary>
    [HttpPost("categories")]
    [Authorize(Policy = "ProjectManagerOnly")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { success = false, message = "Category name is required." });

        var result = await _kbService.CreateCategoryAsync(companyId, userId, role, request.Name);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = new { folderId = result.FolderId } });
    }

    // ──────────────────────────────────────────────
    // Tags
    // ──────────────────────────────────────────────

    /// <summary>
    /// Get available tags
    /// </summary>
    [HttpGet("tags")]
    public async Task<IActionResult> GetTags()
    {
        var companyId = GetCurrentCompanyId();
        if (companyId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var result = await _kbService.GetTagsAsync(companyId);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = result.Tags });
    }

    // ──────────────────────────────────────────────
    // Comments
    // ──────────────────────────────────────────────

    /// <summary>
    /// Get comments for an article
    /// </summary>
    [HttpGet("articles/{id}/comments")]
    public async Task<IActionResult> GetComments(int id)
    {
        var companyId = GetCurrentCompanyId();
        if (companyId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var result = await _kbService.GetArticleCommentsAsync(companyId, id);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = result.Comments });
    }

    /// <summary>
    /// Add a comment/feedback to an article
    /// </summary>
    [HttpPost("articles/{id}/comments")]
    public async Task<IActionResult> AddComment(int id, [FromBody] AddArticleCommentRequest request)
    {
        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { success = false, message = "Comment content is required." });

        var result = await _kbService.AddArticleCommentAsync(companyId, userId, id, request.Content);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new { success = true, data = new { commentId = result.CommentId } });
    }

    // ──────────────────────────────────────────────
    // Stats
    // ──────────────────────────────────────────────

    /// <summary>
    /// Get knowledge base statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var companyId = GetCurrentCompanyId();
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();
        if (companyId == 0 || userId == 0)
            return BadRequest(new { success = false, message = "Invalid user context." });

        var result = await _kbService.GetKbStatsAsync(companyId, userId, role);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(new
        {
            success = true,
            data = new
            {
                totalArticles = result.TotalArticles,
                approvedArticles = result.ApprovedArticles,
                pendingArticles = result.PendingArticles,
                draftArticles = result.DraftArticles,
                rejectedArticles = result.RejectedArticles,
                totalCategories = result.TotalCategories
            }
        });
    }
}

// ─── Request DTOs for Controller ──────────────────────

public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
}

public class ApprovalActionRequest
{
    public string? Reason { get; set; }
}

public class AddArticleCommentRequest
{
    public string Content { get; set; } = string.Empty;
}
