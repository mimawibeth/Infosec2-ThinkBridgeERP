/**
 * ThinkBridge ERP - Knowledge Base Module
 * Handles articles CRUD, approval workflow, categories, comments
 */
(function () {
    'use strict';

    const API_BASE = '/api/knowledgebase';
    let currentPage = 1;
    let currentPageSize = 9;
    let totalPages = 1;
    let currentFolderId = 0;
    let currentStatus = '';
    let currentSearch = '';
    let currentArticleId = 0;
    let approvalAction = ''; // 'reject' or 'revision'
    let categories = [];
    let userRole = '';
    let pendingArchiveId = null;

    //  Initialization 
    document.addEventListener('DOMContentLoaded', function () {
        userRole = document.getElementById('current-user-role')?.value || 'teammember';

        loadCategories();
        loadArticles();
        loadStats();
        initEventHandlers();
    });

    //  Event Handlers 
    function initEventHandlers() {
        // Search
        const searchInput = document.getElementById('kb-search-input');
        if (searchInput) {
            let debounceTimer;
            searchInput.addEventListener('input', function () {
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(() => {
                    currentSearch = this.value.trim();
                    currentPage = 1;
                    loadArticles();
                }, 400);
            });
        }

        // Status filter
        const statusFilter = document.getElementById('kb-status-filter');
        if (statusFilter) {
            statusFilter.addEventListener('change', function () {
                currentStatus = this.value;
                currentPage = 1;
                loadArticles();
            });
        }

        // New Article
        const newArticleBtn = document.getElementById('new-article-btn');
        if (newArticleBtn) {
            newArticleBtn.addEventListener('click', function () {
                openArticleForm();
            });
        }

        // Save Draft
        const saveDraftBtn = document.getElementById('save-draft-btn');
        if (saveDraftBtn) {
            saveDraftBtn.addEventListener('click', function () {
                saveArticle(true);
            });
        }

        // Submit Article
        const submitArticleBtn = document.getElementById('submit-article-btn');
        if (submitArticleBtn) {
            submitArticleBtn.addEventListener('click', function () {
                saveArticle(false);
            });
        }

        const addReferenceBtn = document.getElementById('add-reference-btn');
        if (addReferenceBtn) {
            addReferenceBtn.addEventListener('click', function () {
                addReferenceRow();
            });
        }

        const referencesList = document.getElementById('article-references-list');
        if (referencesList) {
            referencesList.addEventListener('click', function (event) {
                const target = event.target.closest('.reference-remove-btn');
                if (!target) return;

                const row = target.closest('.reference-row');
                if (row) {
                    row.remove();
                }

                if (!referencesList.querySelector('.reference-row')) {
                    addReferenceRow();
                }
            });
        }

        // Add Category
        const addCategoryBtn = document.getElementById('add-category-btn');
        if (addCategoryBtn) {
            addCategoryBtn.addEventListener('click', function () {
                openModal('add-category-modal');
            });
        }

        const saveCategoryBtn = document.getElementById('save-category-btn');
        if (saveCategoryBtn) {
            saveCategoryBtn.addEventListener('click', function () {
                saveCategory();
            });
        }

        // Pending Review link
        const pendingLink = document.getElementById('pending-review-link');
        if (pendingLink) {
            pendingLink.addEventListener('click', function () {
                // Clear other active states
                document.querySelectorAll('#category-list .category-item').forEach(i => i.classList.remove('active'));
                this.classList.add('active');
                currentFolderId = 0;
                currentStatus = 'Pending';
                const statusSelect = document.getElementById('kb-status-filter');
                if (statusSelect) statusSelect.value = 'Pending';
                currentPage = 1;
                loadArticles();
            });
        }

        // View modal actions
        const editBtn = document.getElementById('view-edit-btn');
        if (editBtn) {
            editBtn.addEventListener('click', function () {
                closeModal('view-article-modal');
                setTimeout(() => openArticleForm(currentArticleId), 250);
            });
        }

        const archiveBtn = document.getElementById('view-archive-btn');
        if (archiveBtn) {
            archiveBtn.addEventListener('click', function () {
                archiveArticle(currentArticleId);
            });
        }

        const confirmArchiveBtn = document.getElementById('confirm-archive-btn');
        if (confirmArchiveBtn) {
            confirmArchiveBtn.addEventListener('click', function () {
                confirmArchive();
            });
        }

        const restoreBtn = document.getElementById('view-restore-btn');
        if (restoreBtn) {
            restoreBtn.addEventListener('click', function () {
                restoreArticle(currentArticleId);
            });
        }

        // Approval actions
        const approveBtn = document.getElementById('view-approve-btn');
        if (approveBtn) {
            approveBtn.addEventListener('click', function () {
                approveArticle(currentArticleId);
            });
        }

        const rejectBtn = document.getElementById('view-reject-btn');
        if (rejectBtn) {
            rejectBtn.addEventListener('click', function () {
                approvalAction = 'reject';
                document.getElementById('approval-reason-title').textContent = 'Rejection Reason';
                document.getElementById('approval-reason-text').value = '';
                openModal('approval-reason-modal');
            });
        }

        const revisionBtn = document.getElementById('view-request-revision-btn');
        if (revisionBtn) {
            revisionBtn.addEventListener('click', function () {
                approvalAction = 'revision';
                document.getElementById('approval-reason-title').textContent = 'Revision Feedback';
                document.getElementById('approval-reason-text').value = '';
                openModal('approval-reason-modal');
            });
        }

        const reasonSubmitBtn = document.getElementById('approval-reason-submit');
        if (reasonSubmitBtn) {
            reasonSubmitBtn.addEventListener('click', function () {
                const reason = document.getElementById('approval-reason-text').value.trim();
                if (approvalAction === 'reject') {
                    rejectArticle(currentArticleId, reason);
                } else if (approvalAction === 'revision') {
                    requestRevision(currentArticleId, reason);
                }
                closeModal('approval-reason-modal');
            });
        }

        // Add comment
        const addCommentBtn = document.getElementById('kb-add-comment-btn');
        if (addCommentBtn) {
            addCommentBtn.addEventListener('click', function () {
                addComment();
            });
        }
    }

    //  API Calls 

    async function apiGet(url) {
        const resp = await fetch(url, { credentials: 'same-origin' });
        return resp.json();
    }

    async function apiPost(url, body) {
        const resp = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify(body)
        });
        return resp.json();
    }

    async function apiPut(url, body) {
        const resp = await fetch(url, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify(body)
        });
        return resp.json();
    }

    async function apiDelete(url) {
        const resp = await fetch(url, {
            method: 'DELETE',
            credentials: 'same-origin'
        });
        return resp.json();
    }

    //  Load Articles 

    async function loadArticles() {
        showLoading(true);
        hideEmpty();

        let url = `${API_BASE}/articles?page=${currentPage}&pageSize=${currentPageSize}`;
        if (currentFolderId > 0) url += `&folderId=${currentFolderId}`;
        if (currentStatus) url += `&status=${encodeURIComponent(currentStatus)}`;
        if (currentSearch) url += `&search=${encodeURIComponent(currentSearch)}`;

        try {
            const result = await apiGet(url);
            showLoading(false);

            if (result.success) {
                renderArticles(result.data);
                totalPages = result.pagination?.totalPages || 1;
                updatePagination(result.pagination);
            } else {
                showToast(result.message || 'Failed to load articles', 'error');
            }
        } catch (err) {
            showLoading(false);
            showToast('Failed to load articles', 'error');
            console.error(err);
        }
    }

    function renderArticles(articles) {
        const grid = document.getElementById('articles-grid');
        if (!grid) return;

        if (!articles || articles.length === 0) {
            grid.innerHTML = '';
            showEmpty();
            return;
        }

        hideEmpty();
        grid.innerHTML = articles.map(a => `
            <article class="article-card" data-id="${a.documentID}" onclick="window.kbViewArticle(${a.documentID})">
                <div class="article-header">
                    <span class="article-category cat-default">${escapeHtml(a.categoryName || 'Uncategorized')}</span>
                    ${a.approvalStatus && a.approvalStatus !== 'Approved' ? `<span class="article-status-badge status-${(a.approvalStatus).toLowerCase()}">${escapeHtml(a.approvalStatus)}</span>` : ''}
                </div>
                <div class="article-icon">
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                        <polyline points="14 2 14 8 20 8"></polyline>
                        <line x1="16" y1="13" x2="8" y2="13"></line>
                        <line x1="16" y1="17" x2="8" y2="17"></line>
                    </svg>
                </div>
                <h3 class="article-title">${escapeHtml(a.title)}</h3>
                <p class="article-description">${escapeHtml(a.description || 'No description')}</p>
                <div class="article-meta">
                    <div class="meta-author">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path>
                            <circle cx="12" cy="7" r="4"></circle>
                        </svg>
                        <span>${escapeHtml(a.authorName)}</span>
                    </div>
                    <div class="meta-right">
                        <div class="meta-date" title="${a.publishedAt ? 'Published' : 'Created'}" style="${a.publishedAt ? 'color:var(--success, #10b981);' : ''}">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect>
                                <line x1="16" y1="2" x2="16" y2="6"></line>
                                <line x1="8" y1="2" x2="8" y2="6"></line>
                                <line x1="3" y1="10" x2="21" y2="10"></line>
                            </svg>
                            <span>${formatDate(a.publishedAt || a.createdAt)}</span>
                        </div>
                        <div class="meta-comments">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"></path>
                            </svg>
                            <span>${a.commentCount || 0}</span>
                        </div>
                    </div>
                </div>
            </article>
        `).join('');
    }

    //  View Article 

    window.kbViewArticle = async function (documentId) {
        currentArticleId = documentId;
        try {
            const result = await apiGet(`${API_BASE}/articles/${documentId}`);
            if (!result.success) {
                showToast(result.message || 'Failed to load article', 'error');
                return;
            }

            const a = result.data;

            document.getElementById('view-article-title').textContent = a.title;
            document.getElementById('view-article-category').textContent = a.categoryName || 'Uncategorized';
            document.getElementById('view-article-author').textContent = a.authorName;
            document.getElementById('view-article-date').textContent = formatDate(a.createdAt);

            // Last Updated date
            const updatedSection = document.getElementById('view-article-updated-section');
            if (a.updatedAt) {
                updatedSection.style.display = '';
                document.getElementById('view-article-updated').textContent = formatDate(a.updatedAt);
            } else {
                updatedSection.style.display = 'none';
            }

            // Published date
            const publishedSection = document.getElementById('view-article-published-section');
            if (a.publishedAt) {
                publishedSection.style.display = '';
                document.getElementById('view-article-published').textContent = formatDate(a.publishedAt);
            } else {
                publishedSection.style.display = 'none';
            }

            document.getElementById('view-article-description').textContent = a.description || 'No description provided.';

            // Status badge — hide for Approved articles (clean look)
            const statusBadge = document.getElementById('view-article-status-badge');
            if (a.approvalStatus && a.approvalStatus !== 'Approved') {
                statusBadge.className = `badge article-status-badge status-${(a.approvalStatus).toLowerCase()}`;
                statusBadge.textContent = a.approvalStatus;
                statusBadge.style.display = '';
            } else {
                statusBadge.style.display = 'none';
            }

            // Content - convert newlines to paragraphs
            const contentDiv = document.getElementById('view-article-content');
            contentDiv.innerHTML = formatContent(a.content || 'No content.');

            // Project
            const projectSection = document.getElementById('view-article-project-section');
            if (a.projectName) {
                projectSection.style.display = '';
                document.getElementById('view-article-project').textContent = a.projectName;
            } else {
                projectSection.style.display = 'none';
            }

            // Tags
            const tagsContainer = document.getElementById('view-article-tags');
            const tagsSection = document.getElementById('view-article-tags-section');
            if (a.tags && a.tags.length > 0) {
                tagsSection.style.display = '';
                tagsContainer.innerHTML = a.tags.map(t => `<span class="tag">${escapeHtml(t)}</span>`).join('');
            } else {
                tagsSection.style.display = 'none';
            }

            // References
            const referencesSection = document.getElementById('view-article-references-section');
            const referencesContainer = document.getElementById('view-article-references');
            if (a.references && a.references.length > 0) {
                referencesSection.style.display = '';
                referencesContainer.innerHTML = a.references
                    .sort((x, y) => (x.sortOrder ?? 0) - (y.sortOrder ?? 0))
                    .map((reference, index) => {
                        const url = reference.url
                            ? `<a href="${escapeHtml(reference.url)}" target="_blank" rel="noopener noreferrer" class="kb-reference-link">${escapeHtml(reference.url)}</a>`
                            : '<span class="kb-reference-missing-url">No URL provided</span>';

                        const metaParts = [reference.author, reference.sourceName, reference.publishedDateText]
                            .filter(Boolean)
                            .map(escapeHtml);

                        return `
                            <div class="kb-reference-item">
                                <div class="kb-reference-index">${index + 1}</div>
                                <div class="kb-reference-body">
                                    <div class="kb-reference-title">${escapeHtml(reference.title || 'Untitled source')}</div>
                                    <div class="kb-reference-meta">${metaParts.join(' • ')}</div>
                                    <div class="kb-reference-url">${url}</div>
                                    ${reference.notes ? `<p class="kb-reference-note">${escapeHtml(reference.notes)}</p>` : ''}
                                </div>
                            </div>
                        `;
                    })
                    .join('');
            } else {
                referencesSection.style.display = '';
                referencesContainer.innerHTML = '<p class="kb-no-references">No references added.</p>';
            }

            // Show/hide action buttons based on role
            const editBtn = document.getElementById('view-edit-btn');
            const archiveBtn = document.getElementById('view-archive-btn');
            const restoreBtn = document.getElementById('view-restore-btn');
            const adminActions = document.getElementById('view-admin-actions');

            // Reset
            if (editBtn) editBtn.style.display = 'none';
            if (archiveBtn) archiveBtn.style.display = 'none';
            if (restoreBtn) restoreBtn.style.display = 'none';
            if (adminActions) adminActions.style.display = 'none';

            if (userRole === 'companyadmin') {
                if (editBtn) editBtn.style.display = '';
                if (a.approvalStatus === 'Archived') {
                    if (restoreBtn) restoreBtn.style.display = '';
                } else {
                    if (archiveBtn) archiveBtn.style.display = '';
                }
                if (a.approvalStatus === 'Pending' && adminActions) {
                    adminActions.style.display = '';
                }
            } else if (userRole === 'projectmanager' && a.uploadedBy) {
                // PM can edit own articles
                if (editBtn) editBtn.style.display = '';
                if (a.approvalStatus !== 'Approved' && a.approvalStatus !== 'Archived' && archiveBtn) archiveBtn.style.display = '';
            }

            // Load comments
            loadComments(documentId);

            openModal('view-article-modal');
        } catch (err) {
            showToast('Failed to load article', 'error');
            console.error(err);
        }
    };

    //  Create/Edit Article Form 

    async function openArticleForm(docId) {
        const titleEl = document.getElementById('article-form-title');
        const formId = document.getElementById('article-form-id');

        // Populate category dropdown
        await populateCategoryDropdown();

        if (docId) {
            titleEl.textContent = 'Edit Article';
            formId.value = docId;

            // Load article data
            const result = await apiGet(`${API_BASE}/articles/${docId}`);
            if (result.success) {
                const a = result.data;
                document.getElementById('article-title').value = a.title;
                document.getElementById('article-category').value = a.folderID;
                document.getElementById('article-summary').value = a.description || '';
                document.getElementById('article-content').value = a.content || '';
                document.getElementById('article-tags').value = (a.tags || []).join(', ');
                renderReferenceRows(a.references || []);
                await populateProjectDropdown(a.projectID);
            } else {
                renderReferenceRows([]);
                await populateProjectDropdown();
            }
        } else {
            titleEl.textContent = 'Create New Article';
            formId.value = '0';
            document.getElementById('article-title').value = '';
            document.getElementById('article-category').value = '';
            document.getElementById('article-summary').value = '';
            document.getElementById('article-content').value = '';
            document.getElementById('article-tags').value = '';
            renderReferenceRows([]);
            await populateProjectDropdown();
        }

        openModal('article-form-modal');
    }

    async function populateProjectDropdown(selectedProjectId) {
        const select = document.getElementById('article-project');
        if (!select) return;

        select.innerHTML = '<option value="">No Project</option>';

        try {
            const result = await apiGet('/api/projects?pageSize=100');
            if (result.success && result.data) {
                result.data.forEach(p => {
                    const opt = document.createElement('option');
                    opt.value = p.projectID;
                    opt.textContent = p.projectName;
                    select.appendChild(opt);
                });
            }
        } catch (err) {
            console.error('Failed to load projects', err);
        }

        if (selectedProjectId) {
            select.value = selectedProjectId;
        }
    }

    async function populateCategoryDropdown() {
        const select = document.getElementById('article-category');
        if (!select) return;

        // Keep the first "Select Category" option
        select.innerHTML = '<option value="">Select Category</option>';

        if (categories.length === 0) {
            await loadCategories();
        }

        categories.forEach(c => {
            const opt = document.createElement('option');
            opt.value = c.folderID;
            opt.textContent = c.folderName;
            select.appendChild(opt);
        });
    }

    async function saveArticle(asDraft) {
        const formId = parseInt(document.getElementById('article-form-id').value || '0');
        const title = document.getElementById('article-title').value.trim();
        const folderId = parseInt(document.getElementById('article-category').value || '0');
        const description = document.getElementById('article-summary').value.trim();
        const content = document.getElementById('article-content').value.trim();
        const tagsRaw = document.getElementById('article-tags').value.trim();
        const tags = tagsRaw ? tagsRaw.split(',').map(t => t.trim()).filter(t => t) : [];
        const references = collectReferenceRows();
        const projectIdVal = document.getElementById('article-project')?.value;
        const projectId = projectIdVal ? parseInt(projectIdVal) : null;

        if (!title) { showToast('Article title is required', 'error'); return; }
        if (!folderId) { showToast('Please select a category', 'error'); return; }
        if (!content) { showToast('Article content is required', 'error'); return; }
        if (!asDraft) {
            const validRefs = references.filter(r => r.title && r.url);
            if (validRefs.length === 0) {
                showToast('At least one reference with a title and URL is required when submitting for approval.', 'error');
                return;
            }
        }

        try {
            let result;
            if (formId > 0) {
                // Update
                result = await apiPut(`${API_BASE}/articles/${formId}`, {
                    title, description, content, folderId, tags,
                    references,
                    projectId,
                    submitForApproval: !asDraft
                });
            } else {
                // Create
                result = await apiPost(`${API_BASE}/articles`, {
                    title, description, content, folderId, tags,
                    references,
                    projectId,
                    fileType: 'Article',
                    saveAsDraft: asDraft
                });
            }

            if (result.success) {
                closeModal('article-form-modal');
                const action = formId > 0 ? 'updated' : 'created';
                const statusMsg = asDraft ? 'saved as draft' : (userRole === 'companyadmin' ? 'published' : 'submitted for approval');
                showToast(`Article ${action} and ${statusMsg}!`, 'success');
                loadArticles();
                loadStats();
                loadCategories();
                // If editing, reopen the view modal to show updated dates
                if (formId > 0) {
                    setTimeout(() => window.kbViewArticle(formId), 300);
                }
            } else {
                showToast(result.message || 'Failed to save article', 'error');
            }
        } catch (err) {
            showToast('Failed to save article', 'error');
            console.error(err);
        }
    }

    function renderReferenceRows(references) {
        const list = document.getElementById('article-references-list');
        if (!list) return;

        list.innerHTML = '';
        const rows = Array.isArray(references) ? references : [];
        if (!rows.length) {
            addReferenceRow();
            return;
        }

        rows
            .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
            .forEach(reference => addReferenceRow(reference));
    }

    function addReferenceRow(reference) {
        const list = document.getElementById('article-references-list');
        if (!list) return;

        const row = document.createElement('div');
        row.className = 'reference-row';
        row.innerHTML = `
            <div class="reference-row-grid">
                <div class="form-group">
                    <label>Source Title <span class="required">*</span></label>
                    <input type="text" class="form-input ref-title" maxlength="300" placeholder="Article, guideline, or study title" required value="${escapeHtml(reference?.title || '')}" />
                </div>
                <div class="form-group">
                    <label>URL <span class="required">*</span></label>
                    <input type="url" class="form-input ref-url" maxlength="2048" placeholder="https://example.com/source" required value="${escapeHtml(reference?.url || '')}" />
                </div>
            </div>
            <div class="reference-row-grid reference-row-grid-meta">
                <div class="form-group">
                    <label>Author / Organization</label>
                    <input type="text" class="form-input ref-author" maxlength="200" placeholder="WHO, NIST, CDC, journal author" value="${escapeHtml(reference?.author || '')}" />
                </div>
                <div class="form-group">
                    <label>Publication / Source</label>
                    <input type="text" class="form-input ref-source-name" maxlength="200" placeholder="Journal, standard, publication" value="${escapeHtml(reference?.sourceName || '')}" />
                </div>
                <div class="form-group">
                    <label>Published Date / Year</label>
                    <input type="text" class="form-input ref-published" maxlength="50" placeholder="2025 or Jan 2025" value="${escapeHtml(reference?.publishedDateText || '')}" />
                </div>
            </div>
            <div class="reference-row-footer">
                <div class="form-group">
                    <label>Note (optional)</label>
                    <textarea class="form-textarea ref-notes" rows="2" maxlength="500" placeholder="Why this source is relevant">${escapeHtml(reference?.notes || '')}</textarea>
                </div>
                <button type="button" class="btn btn-sm btn-secondary reference-remove-btn">Remove</button>
            </div>
        `;

        list.appendChild(row);
    }

    function collectReferenceRows() {
        const rows = Array.from(document.querySelectorAll('#article-references-list .reference-row'));
        return rows
            .map((row, index) => ({
                title: row.querySelector('.ref-title')?.value.trim() || '',
                url: row.querySelector('.ref-url')?.value.trim() || '',
                author: row.querySelector('.ref-author')?.value.trim() || '',
                sourceName: row.querySelector('.ref-source-name')?.value.trim() || '',
                publishedDateText: row.querySelector('.ref-published')?.value.trim() || '',
                notes: row.querySelector('.ref-notes')?.value.trim() || '',
                sortOrder: index
            }))
            .filter(reference =>
                reference.title
                || reference.url
                || reference.author
                || reference.sourceName
                || reference.publishedDateText
                || reference.notes);
    }

    function archiveArticle(docId) {
        pendingArchiveId = docId;
        openModal('archive-confirm-modal');
    }

    async function confirmArchive() {
        if (!pendingArchiveId) return;
        const docId = pendingArchiveId;
        pendingArchiveId = null;
        closeModal('archive-confirm-modal');

        try {
            const result = await apiPost(`${API_BASE}/articles/${docId}/archive`, {});
            if (result.success) {
                closeModal('view-article-modal');
                showToast('Article archived successfully', 'success');
                loadArticles();
                loadStats();
                loadCategories();
            } else {
                showToast(result.message || 'Failed to archive article', 'error');
            }
        } catch (err) {
            showToast('Failed to archive article', 'error');
            console.error(err);
        }
    }

    async function restoreArticle(docId) {
        try {
            const result = await apiPost(`${API_BASE}/articles/${docId}/restore`, {});
            if (result.success) {
                closeModal('view-article-modal');
                showToast('Article restored successfully', 'success');
                loadArticles();
                loadStats();
                loadCategories();
            } else {
                showToast(result.message || 'Failed to restore article', 'error');
            }
        } catch (err) {
            showToast('Failed to restore article', 'error');
            console.error(err);
        }
    }

    //  Approval Workflow 

    async function approveArticle(docId) {
        try {
            const result = await apiPost(`${API_BASE}/articles/${docId}/approve`, {});
            if (result.success) {
                showToast('Article approved and published!', 'success');
                loadArticles();
                loadStats();
                // Refresh the view modal in-place to show the Published Date
                await window.kbViewArticle(docId);
            } else {
                showToast(result.message || 'Failed to approve article', 'error');
            }
        } catch (err) {
            showToast('Failed to approve article', 'error');
            console.error(err);
        }
    }

    async function rejectArticle(docId, reason) {
        try {
            const result = await apiPost(`${API_BASE}/articles/${docId}/reject`, { reason });
            if (result.success) {
                closeModal('view-article-modal');
                showToast('Article rejected', 'success');
                loadArticles();
                loadStats();
            } else {
                showToast(result.message || 'Failed to reject article', 'error');
            }
        } catch (err) {
            showToast('Failed to reject article', 'error');
            console.error(err);
        }
    }

    async function requestRevision(docId, reason) {
        try {
            const result = await apiPost(`${API_BASE}/articles/${docId}/request-revision`, { reason });
            if (result.success) {
                closeModal('view-article-modal');
                showToast('Revision requested', 'success');
                loadArticles();
                loadStats();
            } else {
                showToast(result.message || 'Failed to request revision', 'error');
            }
        } catch (err) {
            showToast('Failed to request revision', 'error');
            console.error(err);
        }
    }

    //  Categories 

    async function loadCategories() {
        try {
            const result = await apiGet(`${API_BASE}/categories`);
            if (result.success) {
                categories = result.data || [];
                renderCategories(categories);
            }
        } catch (err) {
            console.error('Failed to load categories', err);
        }
    }

    function renderCategories(cats) {
        const list = document.getElementById('category-list');
        if (!list) return;

        // Keep the "All Articles" item
        const allItem = list.querySelector('[data-folder-id="0"]');
        list.innerHTML = '';
        if (allItem) {
            const totalCount = cats.reduce((sum, c) => sum + (c.articleCount || 0), 0);
            const countBadge = allItem.querySelector('#all-articles-count');
            if (countBadge) countBadge.textContent = totalCount;
            list.appendChild(allItem);
        }

        cats.forEach(c => {
            const li = document.createElement('li');
            li.className = 'category-item' + (currentFolderId === c.folderID ? ' active' : '');
            li.setAttribute('data-folder-id', c.folderID);
            li.innerHTML = `
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
                </svg>
                <span>${escapeHtml(c.folderName)}</span>
                <span class="category-count">${c.articleCount || 0}</span>
            `;
            li.addEventListener('click', function () {
                document.querySelectorAll('#category-list .category-item').forEach(i => i.classList.remove('active'));
                const pendingLink = document.getElementById('pending-review-link');
                if (pendingLink) pendingLink.classList.remove('active');
                this.classList.add('active');
                currentFolderId = c.folderID;
                currentStatus = '';
                const statusSelect = document.getElementById('kb-status-filter');
                if (statusSelect) statusSelect.value = '';
                currentPage = 1;
                loadArticles();
            });
            list.appendChild(li);
        });

        // Re-attach "All Articles" click handler
        if (allItem) {
            allItem.onclick = function () {
                document.querySelectorAll('#category-list .category-item').forEach(i => i.classList.remove('active'));
                const pendingLink = document.getElementById('pending-review-link');
                if (pendingLink) pendingLink.classList.remove('active');
                allItem.classList.add('active');
                currentFolderId = 0;
                currentStatus = '';
                const statusSelect = document.getElementById('kb-status-filter');
                if (statusSelect) statusSelect.value = '';
                currentPage = 1;
                loadArticles();
            };
        }
    }

    async function saveCategory() {
        const nameInput = document.getElementById('new-category-name');
        const name = nameInput?.value.trim();
        if (!name) { showToast('Category name is required', 'error'); return; }

        try {
            const result = await apiPost(`${API_BASE}/categories`, { name });
            if (result.success) {
                closeModal('add-category-modal');
                nameInput.value = '';
                showToast('Category created successfully!', 'success');
                loadCategories();
                loadStats();
            } else {
                showToast(result.message || 'Failed to create category', 'error');
            }
        } catch (err) {
            showToast('Failed to create category', 'error');
            console.error(err);
        }
    }

    //  Comments 

    async function loadComments(documentId) {
        const list = document.getElementById('kb-comments-list');
        const countBadge = document.getElementById('view-comment-count');

        try {
            const result = await apiGet(`${API_BASE}/articles/${documentId}/comments`);
            if (result.success) {
                const comments = result.data || [];
                countBadge.textContent = comments.length;

                if (comments.length === 0) {
                    list.innerHTML = '<p class="kb-no-comments">No comments yet. Be the first to share your feedback!</p>';
                    return;
                }

                list.innerHTML = comments.map(c => `
                    <div class="kb-comment-item">
                        <div class="kb-comment-avatar">${escapeHtml(c.authorInitials)}</div>
                        <div class="kb-comment-body">
                            <span class="kb-comment-author">${escapeHtml(c.authorName)}</span>
                            <span class="kb-comment-date">${formatDateRelative(c.createdAt)}</span>
                            <p class="kb-comment-text">${escapeHtml(c.content)}</p>
                        </div>
                    </div>
                `).join('');
            }
        } catch (err) {
            console.error('Failed to load comments', err);
        }
    }

    async function addComment() {
        const input = document.getElementById('kb-comment-input');
        const content = input?.value.trim();
        if (!content) { showToast('Please enter a comment', 'error'); return; }

        try {
            const result = await apiPost(`${API_BASE}/articles/${currentArticleId}/comments`, { content });
            if (result.success) {
                input.value = '';
                showToast('Comment posted!', 'success');
                loadComments(currentArticleId);
            } else {
                showToast(result.message || 'Failed to post comment', 'error');
            }
        } catch (err) {
            showToast('Failed to post comment', 'error');
            console.error(err);
        }
    }

    //  Stats 

    async function loadStats() {
        try {
            const result = await apiGet(`${API_BASE}/stats`);
            if (result.success) {
                const d = result.data;
                setTextSafe('stat-total', d.totalArticles);
                setTextSafe('stat-approved', d.approvedArticles);
                setTextSafe('stat-pending', d.pendingArticles);
                setTextSafe('stat-draft', d.draftArticles);
                setTextSafe('stat-categories', d.totalCategories);
                setTextSafe('pending-count-badge', d.pendingArticles);
            }
        } catch (err) {
            console.error('Failed to load stats', err);
        }
    }

    //  Pagination 

    function updatePagination(pag) {
        const paginationEl = document.getElementById('kb-pagination');
        if (!pag || pag.totalCount <= pag.pageSize) {
            if (paginationEl) paginationEl.style.display = 'none';
            return;
        }

        if (paginationEl) paginationEl.style.display = 'flex';

        totalPages = pag.totalPages || 1;
        currentPage = pag.page || 1;

        const start = (currentPage - 1) * pag.pageSize + 1;
        const end = Math.min(currentPage * pag.pageSize, pag.totalCount);
        document.getElementById('kb-page-info').textContent = `Showing ${start}-${end} of ${pag.totalCount}`;

        const controls = document.getElementById('kb-pagination-controls');
        if (controls) {
            let btns = `<button class="btn btn-sm btn-secondary" ${currentPage === 1 ? 'disabled' : ''} onclick="window._kbGoToPage(${currentPage - 1})">&laquo;</button>`;
            for (let i = 1; i <= totalPages; i++) {
                btns += `<button class="btn btn-sm ${i === currentPage ? 'btn-primary' : 'btn-secondary'}" onclick="window._kbGoToPage(${i})">${i}</button>`;
            }
            btns += `<button class="btn btn-sm btn-secondary" ${currentPage === totalPages ? 'disabled' : ''} onclick="window._kbGoToPage(${currentPage + 1})">&raquo;</button>`;
            controls.innerHTML = btns;
        }
    }

    window._kbGoToPage = function (page) {
        if (page < 1 || page > totalPages) return;
        currentPage = page;
        loadArticles();
    };

    //  Utilities 

    function showLoading(show) {
        const el = document.getElementById('kb-loading');
        const grid = document.getElementById('articles-grid');
        if (el) el.style.display = show ? 'flex' : 'none';
        if (grid && show) grid.innerHTML = '';
    }

    function showEmpty() {
        const el = document.getElementById('kb-empty');
        if (el) el.style.display = 'flex';
    }

    function hideEmpty() {
        const el = document.getElementById('kb-empty');
        if (el) el.style.display = 'none';
    }

    function setTextSafe(id, text) {
        const el = document.getElementById(id);
        if (el) el.textContent = text ?? '0';
    }

    function escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    function toUtcDate(dateStr) {
        if (!dateStr) return new Date();
        const s = String(dateStr);
        return new Date(s.endsWith('Z') || s.includes('+') ? s : s + 'Z');
    }

    function formatDate(dateStr) {
        if (!dateStr) return '-';
        return toUtcDate(dateStr).toLocaleDateString('en-US', { timeZone: 'Asia/Manila', month: 'short', day: 'numeric', year: 'numeric' });
    }

    function formatDateRelative(dateStr) {
        if (!dateStr) return '';
        const diff = Math.floor((Date.now() - toUtcDate(dateStr).getTime()) / 1000);

        if (diff < 60) return 'just now';
        if (diff < 3600) return Math.floor(diff / 60) + 'm ago';
        if (diff < 86400) return Math.floor(diff / 3600) + 'h ago';
        if (diff < 604800) return Math.floor(diff / 86400) + 'd ago';
        return formatDate(dateStr);
    }

    function formatContent(text) {
        if (!text) return '<p>No content.</p>';
        // Convert line breaks to paragraphs, preserve headings
        return text.split('\n').map(line => {
            line = escapeHtml(line);
            if (line.match(/^\d+\.\s+/)) {
                return `<h5>${line}</h5>`;
            }
            return line.trim() ? `<p>${line}</p>` : '';
        }).join('');
    }

    // Utility: showToast (use global if available)
    function showToast(message, type) {
        if (window.showToast) {
            window.showToast(message, type);
        } else {
            alert(message);
        }
    }

    // Utility: openModal / closeModal (use global if available)
    function openModal(id) {
        if (window.openModal) {
            window.openModal(id);
        } else {
            const el = document.getElementById(id);
            if (el) el.classList.add('active');
        }
    }

    function closeModal(id) {
        if (window.closeModal) {
            window.closeModal(id);
        } else {
            const el = document.getElementById(id);
            if (el) el.classList.remove('active');
        }
    }

})();
