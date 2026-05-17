using Microsoft.EntityFrameworkCore;
using ThinkBridge_ERP.Data;
using ThinkBridge_ERP.Models.Entities;
using ThinkBridge_ERP.Services.Interfaces;

namespace ThinkBridge_ERP.Services;

public class KnowledgeBaseService : IKnowledgeBaseService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ILogger<KnowledgeBaseService> _logger;

    public KnowledgeBaseService(
        ApplicationDbContext context,
        INotificationService notificationService,
        ILogger<KnowledgeBaseService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    // ──────────────────────────────────────────────
    // Articles (Documents)
    // ──────────────────────────────────────────────

    public async Task<ArticleListResult> GetArticlesAsync(int companyId, int userId, string userRole, ArticleFilterRequest filter)
    {
        try
        {
            var query = _context.Documents
                .Include(d => d.Folder)
                .Include(d => d.Uploader)
                .Include(d => d.Approver)
                .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
                .Where(d => d.CompanyID == companyId);

            // Role-based visibility:
            // TeamMember: only sees Approved articles
            // ProjectManager: sees own articles (all statuses) + Approved articles
            // CompanyAdmin: sees all articles
            if (userRole == "TeamMember")
            {
                query = query.Where(d => d.ApprovalStatus == "Approved");
            }
            else if (userRole == "ProjectManager")
            {
                query = query.Where(d => d.ApprovalStatus == "Approved" || d.UploadedBy == userId);
            }
            // CompanyAdmin sees all

            // Apply filters
            if (filter.FolderId.HasValue && filter.FolderId.Value > 0)
            {
                query = query.Where(d => d.FolderID == filter.FolderId.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                query = query.Where(d => d.ApprovalStatus == filter.Status);
            }
            else
            {
                // Exclude archived articles from default view
                query = query.Where(d => d.ApprovalStatus != "Archived");
            }

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.ToLower();
                query = query.Where(d =>
                    d.Title.ToLower().Contains(term) ||
                    (d.Description != null && d.Description.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(filter.Tag))
            {
                query = query.Where(d => d.DocumentTags.Any(dt => dt.Tag.TagName.ToLower() == filter.Tag.ToLower()));
            }

            var totalCount = await query.CountAsync();

            var articles = await query
                .OrderByDescending(d => d.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(d => new ArticleItem
                {
                    DocumentID = d.DocumentID,
                    Title = d.Title,
                    Description = d.Description,
                    ApprovalStatus = d.ApprovalStatus,
                    CategoryName = d.Folder.FolderName,
                    FolderID = d.FolderID,
                    AuthorName = d.Uploader.Fname + " " + d.Uploader.Lname,
                    AuthorInitials = d.Uploader.Fname.Substring(0, 1) + d.Uploader.Lname.Substring(0, 1),
                    AuthorAvatarColor = d.Uploader.AvatarColor,
                    ApproverName = d.Approver != null ? d.Approver.Fname + " " + d.Approver.Lname : null,
                    CreatedAt = d.CreatedAt,
                    UpdatedAt = d.UpdatedAt,
                    PublishedAt = d.PublishedAt,
                    Tags = d.DocumentTags.Select(dt => dt.Tag.TagName).ToList(),
                    ReferenceCount = d.DocumentReferences.Count(),
                    CommentCount = _context.Comments.Count(c => c.Post.PostDocuments.Any(pd => pd.DocumentID == d.DocumentID))
                })
                .ToListAsync();

            return new ArticleListResult
            {
                Success = true,
                Articles = articles,
                TotalCount = totalCount,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting articles for company {CompanyId}", companyId);
            return new ArticleListResult { Success = false, ErrorMessage = "Failed to retrieve articles." };
        }
    }

    public async Task<ArticleDetailResult> GetArticleByIdAsync(int companyId, int userId, string userRole, int documentId)
    {
        try
        {
            var doc = await _context.Documents
                .Include(d => d.Folder)
                .Include(d => d.Uploader)
                .Include(d => d.Approver)
                .Include(d => d.Project)
                .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
                .Include(d => d.DocumentReferences)
                .Include(d => d.DocumentVersions)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.CompanyID == companyId);

            if (doc == null)
                return new ArticleDetailResult { Success = false, ErrorMessage = "Article not found." };

            // Role-based access check
            if (userRole == "TeamMember" && doc.ApprovalStatus != "Approved")
                return new ArticleDetailResult { Success = false, ErrorMessage = "You don't have access to this article." };

            if (userRole == "ProjectManager" && doc.ApprovalStatus != "Approved" && doc.UploadedBy != userId)
                return new ArticleDetailResult { Success = false, ErrorMessage = "You don't have access to this article." };

            // Get the latest content from DocumentVersion
            var latestVersion = doc.DocumentVersions
                .OrderByDescending(v => v.UploadedAt)
                .FirstOrDefault();

            var commentCount = await _context.Comments
                .CountAsync(c => c.Post.PostDocuments.Any(pd => pd.DocumentID == documentId));

            var article = new ArticleDetailItem
            {
                DocumentID = doc.DocumentID,
                Title = doc.Title,
                Description = doc.Description,
                Content = latestVersion?.FilePath ?? string.Empty, // We store article content in FilePath for articles
                ApprovalStatus = doc.ApprovalStatus,
                CategoryName = doc.Folder.FolderName,
                FolderID = doc.FolderID,
                AuthorName = doc.Uploader.Fname + " " + doc.Uploader.Lname,
                AuthorInitials = doc.Uploader.Fname.Substring(0, 1) + doc.Uploader.Lname.Substring(0, 1),
                AuthorAvatarColor = doc.Uploader.AvatarColor,
                ApproverName = doc.Approver != null ? doc.Approver.Fname + " " + doc.Approver.Lname : null,
                CreatedAt = doc.CreatedAt,
                UpdatedAt = doc.UpdatedAt,
                PublishedAt = doc.PublishedAt,
                Tags = doc.DocumentTags.Select(dt => dt.Tag.TagName).ToList(),
                ReferenceCount = doc.DocumentReferences.Count,
                CommentCount = commentCount,
                ProjectID = doc.ProjectID,
                ProjectName = doc.Project?.ProjectName,
                UploadedBy = doc.UploadedBy,
                FileType = doc.FileType,
                References = doc.DocumentReferences
                    .OrderBy(r => r.SortOrder)
                    .Select(r => new ArticleReferenceItem
                    {
                        ReferenceID = r.ReferenceID,
                        Title = r.Title,
                        Url = r.Url,
                        Author = r.Author,
                        SourceName = r.SourceName,
                        PublishedDateText = r.PublishedDateText,
                        Notes = r.Notes,
                        SortOrder = r.SortOrder
                    })
                    .ToList()
            };

            return new ArticleDetailResult { Success = true, Article = article };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting article {DocumentId}", documentId);
            return new ArticleDetailResult { Success = false, ErrorMessage = "Failed to retrieve article." };
        }
    }

    public async Task<CreateArticleResult> CreateArticleAsync(int companyId, int userId, string userRole, CreateArticleRequest request)
    {
        try
        {
            // Only CompanyAdmin and ProjectManager can create articles
            if (userRole == "TeamMember")
                return new CreateArticleResult { Success = false, ErrorMessage = "You don't have permission to create articles." };

            // Validate folder exists
            var folder = await _context.Folders.FirstOrDefaultAsync(f => f.FolderID == request.FolderId && f.CompanyID == companyId);
            if (folder == null)
                return new CreateArticleResult { Success = false, ErrorMessage = "Category not found." };

            var normalizedReferences = NormalizeReferences(request.References);
            var requiresCitation = !request.SaveAsDraft && LooksExternallySourced(request.Title, request.Description, request.Content);
            var referencesValidationError = ValidateReferences(normalizedReferences, requiresCitation);
            if (!string.IsNullOrWhiteSpace(referencesValidationError))
                return new CreateArticleResult { Success = false, ErrorMessage = referencesValidationError };

            // Determine approval status based on role
            string approvalStatus;
            int? approvedBy = null;

            if (request.SaveAsDraft)
            {
                approvalStatus = "Draft";
            }
            else if (userRole == "CompanyAdmin")
            {
                // Admin articles are auto-approved
                approvalStatus = "Approved";
                approvedBy = userId;
            }
            else
            {
                // ProjectManager articles go to Pending
                approvalStatus = "Pending";
            }

            var document = new Document
            {
                CompanyID = companyId,
                FolderID = request.FolderId,
                ProjectID = request.ProjectId,
                Title = request.Title,
                Description = request.Description,
                FileType = "Article",
                UploadedBy = userId,
                ApprovalStatus = approvalStatus,
                ApprovedBy = approvedBy,
                CreatedAt = DateTime.UtcNow,
                PublishedAt = approvalStatus == "Approved" ? DateTime.UtcNow : null
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            // Save article content as a DocumentVersion
            var version = new DocumentVersion
            {
                DocumentID = document.DocumentID,
                VersionLabel = "v1.0",
                FilePath = request.Content, // Store article content in FilePath for KMS articles
                FileSize = System.Text.Encoding.UTF8.GetByteCount(request.Content),
                UploadedBy = userId,
                UploadedAt = DateTime.UtcNow
            };

            _context.DocumentVersions.Add(version);

            // Handle tags
            if (request.Tags?.Any() == true)
            {
                foreach (var tagName in request.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    var trimmedTag = tagName.Trim();
                    var tag = await _context.Tags.FirstOrDefaultAsync(t => t.CompanyID == companyId && t.TagName.ToLower() == trimmedTag.ToLower());
                    if (tag == null)
                    {
                        tag = new Tag { CompanyID = companyId, TagName = trimmedTag };
                        _context.Tags.Add(tag);
                        await _context.SaveChangesAsync();
                    }

                    _context.DocumentTags.Add(new DocumentTag { DocumentID = document.DocumentID, TagID = tag.TagID });
                }
            }

            for (var i = 0; i < normalizedReferences.Count; i++)
            {
                var reference = normalizedReferences[i];
                _context.DocumentReferences.Add(new DocumentReference
                {
                    DocumentID = document.DocumentID,
                    Title = reference.Title!,
                    Url = reference.Url,
                    Author = reference.Author,
                    SourceName = reference.SourceName,
                    PublishedDateText = reference.PublishedDateText,
                    Notes = reference.Notes,
                    SortOrder = i,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            // Create a Post for comments to be attached to
            var post = new Post
            {
                CompanyID = companyId,
                ProjectID = request.ProjectId,
                CreatedBy = userId,
                Content = $"Article: {request.Title}",
                CreatedAt = DateTime.UtcNow
            };

            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            _context.PostDocuments.Add(new PostDocument { PostID = post.PostID, DocumentID = document.DocumentID });
            await _context.SaveChangesAsync();

            // Notify admins if article is pending approval
            if (approvalStatus == "Pending")
            {
                await NotifyAdminsOfPendingArticle(companyId, userId, document.Title);
            }

            // Log audit
            _context.AuditLogs.Add(new AuditLog
            {
                CompanyID = companyId,
                UserID = userId,
                Action = "Created",
                EntityName = "Document",
                EntityID = document.DocumentID,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return new CreateArticleResult { Success = true, DocumentId = document.DocumentID };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating article for company {CompanyId}", companyId);
            return new CreateArticleResult { Success = false, ErrorMessage = "Failed to create article." };
        }
    }

    public async Task<ServiceResult> UpdateArticleAsync(int companyId, int userId, string userRole, int documentId, UpdateArticleRequest request)
    {
        try
        {
            var doc = await _context.Documents
                .Include(d => d.DocumentTags)
                .Include(d => d.DocumentReferences)
                .Include(d => d.DocumentVersions)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.CompanyID == companyId);

            if (doc == null)
                return new ServiceResult { Success = false, ErrorMessage = "Article not found." };

            // Only the author (PM) or CompanyAdmin can edit
            if (userRole == "TeamMember")
                return new ServiceResult { Success = false, ErrorMessage = "You don't have permission to edit articles." };

            if (userRole == "ProjectManager" && doc.UploadedBy != userId)
                return new ServiceResult { Success = false, ErrorMessage = "You can only edit your own articles." };

            // Update fields
            if (!string.IsNullOrWhiteSpace(request.Title))
                doc.Title = request.Title;

            if (request.Description != null)
                doc.Description = request.Description;

            if (request.FolderId.HasValue)
                doc.FolderID = request.FolderId.Value;

            if (request.ProjectId.HasValue)
                doc.ProjectID = request.ProjectId.Value;

            doc.UpdatedAt = DateTime.UtcNow;

            var effectiveTitle = !string.IsNullOrWhiteSpace(request.Title) ? request.Title : doc.Title;
            var effectiveDescription = request.Description ?? doc.Description;
            var currentContent = doc.DocumentVersions
                .OrderByDescending(v => v.UploadedAt)
                .Select(v => v.FilePath)
                .FirstOrDefault();
            var effectiveContent = !string.IsNullOrWhiteSpace(request.Content) ? request.Content : currentContent;
            var isPublishingOrSubmitting = (request.SubmitForApproval == true && userRole == "ProjectManager")
                || (userRole == "CompanyAdmin" && doc.ApprovalStatus == "Draft");
            var requiresCitation = isPublishingOrSubmitting && LooksExternallySourced(effectiveTitle, effectiveDescription, effectiveContent);
            var normalizedReferences = request.References != null ? NormalizeReferences(request.References) : null;

            if (normalizedReferences != null)
            {
                var referencesValidationError = ValidateReferences(normalizedReferences, requiresCitation);
                if (!string.IsNullOrWhiteSpace(referencesValidationError))
                    return new ServiceResult { Success = false, ErrorMessage = referencesValidationError };

                _context.DocumentReferences.RemoveRange(doc.DocumentReferences);
                for (var i = 0; i < normalizedReferences.Count; i++)
                {
                    var reference = normalizedReferences[i];
                    _context.DocumentReferences.Add(new DocumentReference
                    {
                        DocumentID = doc.DocumentID,
                        Title = reference.Title!,
                        Url = reference.Url,
                        Author = reference.Author,
                        SourceName = reference.SourceName,
                        PublishedDateText = reference.PublishedDateText,
                        Notes = reference.Notes,
                        SortOrder = i,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            else if (requiresCitation && !doc.DocumentReferences.Any())
            {
                return new ServiceResult
                {
                    Success = false,
                    ErrorMessage = "At least one reference is required before submitting or publishing externally sourced content."
                };
            }

            // Handle content update via new version
            if (!string.IsNullOrWhiteSpace(request.Content))
            {
                var versionCount = doc.DocumentVersions.Count;
                var version = new DocumentVersion
                {
                    DocumentID = doc.DocumentID,
                    VersionLabel = $"v{versionCount + 1}.0",
                    FilePath = request.Content,
                    FileSize = System.Text.Encoding.UTF8.GetByteCount(request.Content),
                    UploadedBy = userId,
                    UploadedAt = DateTime.UtcNow
                };
                _context.DocumentVersions.Add(version);
            }

            // Handle submit for approval
            if (request.SubmitForApproval == true && userRole == "ProjectManager")
            {
                doc.ApprovalStatus = "Pending";
                doc.ApprovedBy = null;
                await NotifyAdminsOfPendingArticle(companyId, userId, doc.Title);
            }
            else if (userRole == "CompanyAdmin" && doc.ApprovalStatus == "Draft")
            {
                // Admin can auto-approve when editing a draft
                doc.ApprovalStatus = "Approved";
                doc.ApprovedBy = userId;
                doc.PublishedAt = DateTime.UtcNow;
            }

            // Handle tags
            if (request.Tags != null)
            {
                // Remove existing tags
                var existingTags = await _context.DocumentTags.Where(dt => dt.DocumentID == documentId).ToListAsync();
                _context.DocumentTags.RemoveRange(existingTags);

                foreach (var tagName in request.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    var trimmedTag = tagName.Trim();
                    var tag = await _context.Tags.FirstOrDefaultAsync(t => t.CompanyID == companyId && t.TagName.ToLower() == trimmedTag.ToLower());
                    if (tag == null)
                    {
                        tag = new Tag { CompanyID = companyId, TagName = trimmedTag };
                        _context.Tags.Add(tag);
                        await _context.SaveChangesAsync();
                    }

                    _context.DocumentTags.Add(new DocumentTag { DocumentID = documentId, TagID = tag.TagID });
                }
            }

            await _context.SaveChangesAsync();

            // Audit log
            _context.AuditLogs.Add(new AuditLog
            {
                CompanyID = companyId,
                UserID = userId,
                Action = "Updated",
                EntityName = "Document",
                EntityID = doc.DocumentID,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return new ServiceResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating article {DocumentId}", documentId);
            return new ServiceResult { Success = false, ErrorMessage = "Failed to update article." };
        }
    }

    public async Task<ServiceResult> ArchiveArticleAsync(int companyId, int userId, string userRole, int documentId)
    {
        try
        {
            var doc = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.CompanyID == companyId);

            if (doc == null)
                return new ServiceResult { Success = false, ErrorMessage = "Article not found." };

            // Only CompanyAdmin can archive, or PM can archive their own non-approved articles
            if (userRole == "TeamMember")
                return new ServiceResult { Success = false, ErrorMessage = "You don't have permission to archive articles." };

            if (userRole == "ProjectManager" && (doc.UploadedBy != userId || doc.ApprovalStatus == "Approved"))
                return new ServiceResult { Success = false, ErrorMessage = "You can only archive your own non-approved articles." };

            if (doc.ApprovalStatus == "Archived")
                return new ServiceResult { Success = false, ErrorMessage = "Article is already archived." };

            doc.ApprovalStatus = "Archived";
            await _context.SaveChangesAsync();

            // Audit log
            _context.AuditLogs.Add(new AuditLog
            {
                CompanyID = companyId,
                UserID = userId,
                Action = "Archived",
                EntityName = "Document",
                EntityID = documentId,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return new ServiceResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving article {DocumentId}", documentId);
            return new ServiceResult { Success = false, ErrorMessage = "Failed to archive article." };
        }
    }

    public async Task<ServiceResult> RestoreArticleAsync(int companyId, int userId, string userRole, int documentId)
    {
        try
        {
            var doc = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.CompanyID == companyId);

            if (doc == null)
                return new ServiceResult { Success = false, ErrorMessage = "Article not found." };

            if (userRole != "CompanyAdmin")
                return new ServiceResult { Success = false, ErrorMessage = "Only administrators can restore archived articles." };

            if (doc.ApprovalStatus != "Archived")
                return new ServiceResult { Success = false, ErrorMessage = "Article is not archived." };

            doc.ApprovalStatus = "Draft";
            await _context.SaveChangesAsync();

            _context.AuditLogs.Add(new AuditLog
            {
                CompanyID = companyId,
                UserID = userId,
                Action = "Restored",
                EntityName = "Document",
                EntityID = documentId,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return new ServiceResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring article {DocumentId}", documentId);
            return new ServiceResult { Success = false, ErrorMessage = "Failed to restore article." };
        }
    }

    // ──────────────────────────────────────────────
    // Approval Workflow
    // ──────────────────────────────────────────────

    public async Task<ArticleListResult> GetPendingArticlesAsync(int companyId, int userId, string userRole, ArticleFilterRequest filter)
    {
        try
        {
            if (userRole != "CompanyAdmin")
                return new ArticleListResult { Success = false, ErrorMessage = "Only administrators can view pending articles." };

            filter.Status = "Pending";
            return await GetArticlesAsync(companyId, userId, userRole, filter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending articles for company {CompanyId}", companyId);
            return new ArticleListResult { Success = false, ErrorMessage = "Failed to retrieve pending articles." };
        }
    }

    public async Task<ServiceResult> ApproveArticleAsync(int companyId, int userId, string userRole, int documentId)
    {
        try
        {
            if (userRole != "CompanyAdmin")
                return new ServiceResult { Success = false, ErrorMessage = "Only administrators can approve articles." };

            var doc = await _context.Documents
                .Include(d => d.Uploader)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.CompanyID == companyId);

            if (doc == null)
                return new ServiceResult { Success = false, ErrorMessage = "Article not found." };

            if (doc.ApprovalStatus != "Pending")
                return new ServiceResult { Success = false, ErrorMessage = "Only pending articles can be approved." };

            doc.ApprovalStatus = "Approved";
            doc.ApprovedBy = userId;
            doc.PublishedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Notify the author
            await _notificationService.SendNotificationAsync(
                doc.UploadedBy,
                "ArticleApproved",
                $"Your article \"{doc.Title}\" has been approved and is now published.");

            // Audit log
            _context.AuditLogs.Add(new AuditLog
            {
                CompanyID = companyId,
                UserID = userId,
                Action = "Approved",
                EntityName = "Document",
                EntityID = doc.DocumentID,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return new ServiceResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving article {DocumentId}", documentId);
            return new ServiceResult { Success = false, ErrorMessage = "Failed to approve article." };
        }
    }

    public async Task<ServiceResult> RejectArticleAsync(int companyId, int userId, string userRole, int documentId, string? reason)
    {
        try
        {
            if (userRole != "CompanyAdmin")
                return new ServiceResult { Success = false, ErrorMessage = "Only administrators can reject articles." };

            var doc = await _context.Documents
                .Include(d => d.Uploader)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.CompanyID == companyId);

            if (doc == null)
                return new ServiceResult { Success = false, ErrorMessage = "Article not found." };

            if (doc.ApprovalStatus != "Pending")
                return new ServiceResult { Success = false, ErrorMessage = "Only pending articles can be rejected." };

            doc.ApprovalStatus = "Rejected";
            doc.ApprovedBy = null;
            await _context.SaveChangesAsync();

            // Notify the author
            var message = $"Your article \"{doc.Title}\" has been rejected.";
            if (!string.IsNullOrWhiteSpace(reason))
                message += $" Reason: {reason}";

            await _notificationService.SendNotificationAsync(doc.UploadedBy, "ArticleRejected", message);

            // Audit log
            _context.AuditLogs.Add(new AuditLog
            {
                CompanyID = companyId,
                UserID = userId,
                Action = "Rejected",
                EntityName = "Document",
                EntityID = doc.DocumentID,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return new ServiceResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting article {DocumentId}", documentId);
            return new ServiceResult { Success = false, ErrorMessage = "Failed to reject article." };
        }
    }

    public async Task<ServiceResult> RequestRevisionAsync(int companyId, int userId, string userRole, int documentId, string? reason)
    {
        try
        {
            if (userRole != "CompanyAdmin")
                return new ServiceResult { Success = false, ErrorMessage = "Only administrators can request revisions." };

            var doc = await _context.Documents
                .Include(d => d.Uploader)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.CompanyID == companyId);

            if (doc == null)
                return new ServiceResult { Success = false, ErrorMessage = "Article not found." };

            if (doc.ApprovalStatus != "Pending")
                return new ServiceResult { Success = false, ErrorMessage = "Only pending articles can be sent back for revision." };

            // Set back to Draft so the PM can edit and resubmit
            doc.ApprovalStatus = "Draft";
            doc.ApprovedBy = null;
            await _context.SaveChangesAsync();

            // Notify the author
            var message = $"Your article \"{doc.Title}\" requires revisions.";
            if (!string.IsNullOrWhiteSpace(reason))
                message += $" Feedback: {reason}";

            await _notificationService.SendNotificationAsync(doc.UploadedBy, "ArticleRevision", message);

            // Audit log
            _context.AuditLogs.Add(new AuditLog
            {
                CompanyID = companyId,
                UserID = userId,
                Action = "RequestedRevision",
                EntityName = "Document",
                EntityID = doc.DocumentID,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return new ServiceResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting revision for article {DocumentId}", documentId);
            return new ServiceResult { Success = false, ErrorMessage = "Failed to request revision." };
        }
    }

    // ──────────────────────────────────────────────
    // Categories (Folders)
    // ──────────────────────────────────────────────

    public async Task<CategoryListResult> GetCategoriesAsync(int companyId)
    {
        try
        {
            var categories = await _context.Folders
                .Where(f => f.CompanyID == companyId && f.ParentFolderID == null)
                .Select(f => new CategoryItem
                {
                    FolderID = f.FolderID,
                    FolderName = f.FolderName,
                    ArticleCount = f.Documents.Count(d => d.FileType == "Article")
                })
                .OrderBy(c => c.FolderName)
                .ToListAsync();

            return new CategoryListResult { Success = true, Categories = categories };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting categories for company {CompanyId}", companyId);
            return new CategoryListResult { Success = false, ErrorMessage = "Failed to retrieve categories." };
        }
    }

    public async Task<CreateCategoryResult> CreateCategoryAsync(int companyId, int userId, string userRole, string categoryName)
    {
        try
        {
            if (userRole == "TeamMember")
                return new CreateCategoryResult { Success = false, ErrorMessage = "You don't have permission to create categories." };

            // Check if category already exists
            var exists = await _context.Folders.AnyAsync(f => f.CompanyID == companyId && f.FolderName.ToLower() == categoryName.ToLower() && f.ParentFolderID == null);
            if (exists)
                return new CreateCategoryResult { Success = false, ErrorMessage = "A category with this name already exists." };

            var folder = new Folder
            {
                CompanyID = companyId,
                FolderName = categoryName.Trim(),
                ParentFolderID = null,
                CreatedAt = DateTime.UtcNow
            };

            _context.Folders.Add(folder);
            await _context.SaveChangesAsync();

            return new CreateCategoryResult { Success = true, FolderId = folder.FolderID };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating category for company {CompanyId}", companyId);
            return new CreateCategoryResult { Success = false, ErrorMessage = "Failed to create category." };
        }
    }

    // ──────────────────────────────────────────────
    // Tags
    // ──────────────────────────────────────────────

    public async Task<TagListResult> GetTagsAsync(int companyId)
    {
        try
        {
            var tags = await _context.Tags
                .Where(t => t.CompanyID == companyId)
                .OrderBy(t => t.TagName)
                .Select(t => new TagItem { TagID = t.TagID, TagName = t.TagName })
                .ToListAsync();

            return new TagListResult { Success = true, Tags = tags };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tags for company {CompanyId}", companyId);
            return new TagListResult { Success = false, ErrorMessage = "Failed to retrieve tags." };
        }
    }

    // ──────────────────────────────────────────────
    // Comments on Articles
    // ──────────────────────────────────────────────

    public async Task<ArticleCommentListResult> GetArticleCommentsAsync(int companyId, int documentId)
    {
        try
        {
            // Verify the document belongs to the caller's company
            var docCompanyId = await _context.Documents
                .Where(d => d.DocumentID == documentId)
                .Select(d => d.CompanyID)
                .FirstOrDefaultAsync();

            if (docCompanyId == 0 || docCompanyId != companyId)
                return new ArticleCommentListResult { Success = true, Comments = new List<ArticleCommentItem>() };

            // Find the post associated with this document
            var postDoc = await _context.PostDocuments
                .Include(pd => pd.Post)
                .FirstOrDefaultAsync(pd => pd.DocumentID == documentId);

            if (postDoc == null)
                return new ArticleCommentListResult { Success = true, Comments = new List<ArticleCommentItem>() };

            var comments = await _context.Comments
                .Include(c => c.User)
                .Where(c => c.PostID == postDoc.PostID)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new ArticleCommentItem
                {
                    CommentID = c.CommentID,
                    AuthorName = c.User.Fname + " " + c.User.Lname,
                    AuthorInitials = c.User.Fname.Substring(0, 1) + c.User.Lname.Substring(0, 1),
                    AuthorAvatarColor = c.User.AvatarColor,
                    Content = c.Content,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            return new ArticleCommentListResult { Success = true, Comments = comments };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting comments for document {DocumentId}", documentId);
            return new ArticleCommentListResult { Success = false, ErrorMessage = "Failed to retrieve comments." };
        }
    }

    public async Task<CreateArticleCommentResult> AddArticleCommentAsync(int companyId, int userId, int documentId, string content)
    {
        try
        {
            // Find or create post for this document
            var postDoc = await _context.PostDocuments
                .Include(pd => pd.Post)
                .FirstOrDefaultAsync(pd => pd.DocumentID == documentId);

            int postId;
            if (postDoc == null)
            {
                var doc = await _context.Documents.FindAsync(documentId);
                if (doc == null)
                    return new CreateArticleCommentResult { Success = false, ErrorMessage = "Article not found." };

                var post = new Post
                {
                    CompanyID = companyId,
                    ProjectID = doc.ProjectID,
                    CreatedBy = userId,
                    Content = $"Article: {doc.Title}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.Posts.Add(post);
                await _context.SaveChangesAsync();

                _context.PostDocuments.Add(new PostDocument { PostID = post.PostID, DocumentID = documentId });
                await _context.SaveChangesAsync();

                postId = post.PostID;
            }
            else
            {
                postId = postDoc.PostID;
            }

            var comment = new Comment
            {
                PostID = postId,
                UserID = userId,
                Content = content,
                CreatedAt = DateTime.UtcNow
            };

            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            // Notify article author
            var document = await _context.Documents
                .Include(d => d.Uploader)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId);

            if (document != null && document.UploadedBy != userId)
            {
                var commenter = await _context.Users.FindAsync(userId);
                await _notificationService.SendNotificationAsync(
                    document.UploadedBy,
                    "ArticleComment",
                    $"{commenter?.FullName ?? "Someone"} commented on your article \"{document.Title}\".");
            }

            return new CreateArticleCommentResult { Success = true, CommentId = comment.CommentID };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding comment to document {DocumentId}", documentId);
            return new CreateArticleCommentResult { Success = false, ErrorMessage = "Failed to add comment." };
        }
    }

    // ──────────────────────────────────────────────
    // Stats
    // ──────────────────────────────────────────────

    public async Task<KbStatsResult> GetKbStatsAsync(int companyId, int userId, string userRole)
    {
        try
        {
            var query = _context.Documents
                .Where(d => d.CompanyID == companyId && d.FileType == "Article");

            // Apply role-based filtering for stats
            if (userRole == "TeamMember")
            {
                query = query.Where(d => d.ApprovalStatus == "Approved");
            }
            else if (userRole == "ProjectManager")
            {
                query = query.Where(d => d.ApprovalStatus == "Approved" || d.UploadedBy == userId);
            }

            var totalArticles = await query.CountAsync();
            var approved = await query.CountAsync(d => d.ApprovalStatus == "Approved");
            var pending = await query.CountAsync(d => d.ApprovalStatus == "Pending");
            var draft = await query.CountAsync(d => d.ApprovalStatus == "Draft");
            var rejected = await query.CountAsync(d => d.ApprovalStatus == "Rejected");
            var categories = await _context.Folders.CountAsync(f => f.CompanyID == companyId && f.ParentFolderID == null);

            return new KbStatsResult
            {
                Success = true,
                TotalArticles = totalArticles,
                ApprovedArticles = approved,
                PendingArticles = pending,
                DraftArticles = draft,
                RejectedArticles = rejected,
                TotalCategories = categories
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting KB stats for company {CompanyId}", companyId);
            return new KbStatsResult { Success = false, ErrorMessage = "Failed to retrieve stats." };
        }
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static List<ArticleReferenceRequest> NormalizeReferences(IEnumerable<ArticleReferenceRequest>? references)
    {
        if (references == null)
        {
            return new List<ArticleReferenceRequest>();
        }

        return references
            .Select((r, index) => new ArticleReferenceRequest
            {
                Title = r.Title?.Trim(),
                Url = r.Url?.Trim(),
                Author = r.Author?.Trim(),
                SourceName = r.SourceName?.Trim(),
                PublishedDateText = r.PublishedDateText?.Trim(),
                Notes = r.Notes?.Trim(),
                SortOrder = r.SortOrder ?? index
            })
            .Where(r => !string.IsNullOrWhiteSpace(r.Title)
                || !string.IsNullOrWhiteSpace(r.Url)
                || !string.IsNullOrWhiteSpace(r.Author)
                || !string.IsNullOrWhiteSpace(r.SourceName)
                || !string.IsNullOrWhiteSpace(r.PublishedDateText)
                || !string.IsNullOrWhiteSpace(r.Notes))
            .OrderBy(r => r.SortOrder)
            .ToList();
    }

    private static string? ValidateReferences(IReadOnlyCollection<ArticleReferenceRequest> references, bool requireAtLeastOne)
    {
        if (requireAtLeastOne && references.Count == 0)
        {
            return "At least one reference is required before submitting or publishing externally sourced content.";
        }

        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Title))
            {
                return "Each reference must include a title.";
            }

            if (string.IsNullOrWhiteSpace(reference.Url))
            {
                return "Each reference must include a source URL.";
            }

            if (!Uri.TryCreate(reference.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return "Each reference URL must be a valid http or https link.";
            }
        }

        return null;
    }

    private static bool LooksExternallySourced(string? title, string? description, string? content)
    {
        var combined = string.Join(" ", new[] { title, description, content })
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(combined))
        {
            return false;
        }

        if (combined.Contains("http://") || combined.Contains("https://") || combined.Contains("www."))
        {
            return true;
        }

        var indicators = new[]
        {
            "according to",
            "study",
            "research",
            "journal",
            "guideline",
            "whitepaper",
            "report",
            "who",
            "cdc",
            "iso",
            "nist"
        };

        return indicators.Any(combined.Contains);
    }

    private async System.Threading.Tasks.Task NotifyAdminsOfPendingArticle(int companyId, int authorUserId, string articleTitle)
    {
        try
        {
            var admins = await _context.UserRoles
                .Include(ur => ur.Role)
                .Include(ur => ur.User)
                .Where(ur => ur.User.CompanyID == companyId && ur.Role.RoleName == "CompanyAdmin")
                .Select(ur => ur.UserID)
                .ToListAsync();

            var author = await _context.Users.FindAsync(authorUserId);
            foreach (var adminId in admins)
            {
                await _notificationService.SendNotificationAsync(
                    adminId,
                    "ArticlePending",
                    $"{author?.FullName ?? "A team member"} submitted article \"{articleTitle}\" for approval.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying admins of pending article");
        }
    }
}
