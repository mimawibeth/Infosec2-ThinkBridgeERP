/* ==========================================
   ThinkBridge ERP - Audit Log Page Script
   ========================================== */
(function () {
    'use strict';

    let currentPage = 1;
    const pageSize = 15;
    let currentSearch = '';
    let currentAction = 'All';
    let currentEntity = 'All';
    let currentDateRange = 'today';
    let currentLogType = 'system';
    let totalPages = 1;
    let autoRefreshInterval = null;

    // ─── Init ────────────────────────────────────

    document.addEventListener('DOMContentLoaded', function () {
        initTabs();
        initTabStateFromUrl();
        initSearch();
        initFilters();
        initRefreshButton();
        loadAuditLogs();
        startAutoRefresh();
    });

    // ─── API Helper ──────────────────────────────

    async function apiGet(url) {
        try {
            const resp = await fetch(url, { credentials: 'same-origin' });
            if (!resp.ok) {
                const err = await resp.json().catch(() => null);
                return { success: false, message: err?.message || `Error ${resp.status}` };
            }
            return await resp.json();
        } catch (e) {
            return { success: false, message: 'Network error.' };
        }
    }

    // ─── Search ──────────────────────────────────

    function initSearch() {
        const searchInput = document.getElementById('audit-search');
        if (!searchInput) return;

        let timeout;
        searchInput.addEventListener('input', function () {
            clearTimeout(timeout);
            timeout = setTimeout(() => {
                currentSearch = this.value.trim();
                currentPage = 1;
                loadAuditLogs();
            }, 400);
        });
    }

    // ─── Tabs ────────────────────────────────────

    function initTabs() {
        const tabs = document.querySelectorAll('.audit-log-tabs .tab-btn');
        const tabsContainer = document.querySelector('.audit-log-tabs');

        tabs.forEach(tab => {
            tab.addEventListener('click', function () {
                const nextType = this.getAttribute('data-log-type') || 'system';
                setActiveTab(nextType, true);
            });
        });

        if (tabsContainer) {
            tabsContainer.addEventListener('keydown', function (e) {
                const tabButtons = Array.from(document.querySelectorAll('.audit-log-tabs .tab-btn'));
                const currentIndex = tabButtons.findIndex(btn => btn.classList.contains('active'));

                if (currentIndex < 0 || tabButtons.length === 0) return;

                let targetIndex = currentIndex;
                if (e.key === 'ArrowRight') targetIndex = (currentIndex + 1) % tabButtons.length;
                if (e.key === 'ArrowLeft') targetIndex = (currentIndex - 1 + tabButtons.length) % tabButtons.length;
                if (e.key === 'Home') targetIndex = 0;
                if (e.key === 'End') targetIndex = tabButtons.length - 1;

                if (targetIndex !== currentIndex) {
                    e.preventDefault();
                    tabButtons[targetIndex].focus();
                    const nextType = tabButtons[targetIndex].getAttribute('data-log-type') || 'system';
                    setActiveTab(nextType, true);
                }
            });
        }
    }

    function initTabStateFromUrl() {
        const url = new URL(window.location.href);
        const tab = (url.searchParams.get('tab') || '').toLowerCase();
        setActiveTab(tab === 'security' ? 'security' : 'system', false);
    }

    function setActiveTab(logType, shouldReload) {
        const nextType = logType === 'security' ? 'security' : 'system';
        const changed = nextType !== currentLogType;
        currentLogType = nextType;

        const panel = document.getElementById('audit-log-panel');
        document.querySelectorAll('.audit-log-tabs .tab-btn').forEach(btn => {
            const isActive = btn.getAttribute('data-log-type') === currentLogType;
            btn.classList.toggle('active', isActive);
            btn.setAttribute('aria-selected', isActive ? 'true' : 'false');
            btn.setAttribute('tabindex', isActive ? '0' : '-1');
            if (isActive && panel) {
                panel.setAttribute('aria-labelledby', btn.id);
            }
        });

        persistTabToUrl();

        if (shouldReload && changed) {
            currentPage = 1;
            loadAuditLogs();
        }
    }

    function persistTabToUrl() {
        const url = new URL(window.location.href);
        url.searchParams.set('tab', currentLogType);
        window.history.replaceState({}, '', url.toString());
    }

    // ─── Filters ─────────────────────────────────

    function initFilters() {
        // Action filter dropdown
        const actionDropdown = document.getElementById('action-filter');
        const actionBtn = document.getElementById('action-filter-btn');
        const actionItems = document.querySelectorAll('.action-filter-item');

        if (actionBtn && actionDropdown) {
            actionBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                closeAllDropdownsExcept('action-filter');
                actionDropdown.classList.toggle('open');
            });

            actionItems.forEach(item => {
                item.addEventListener('click', function () {
                    currentAction = this.dataset.value;
                    actionBtn.querySelector('.filter-text').textContent = this.textContent;
                    actionDropdown.classList.remove('open');
                    currentPage = 1;
                    loadAuditLogs();
                });
            });
        }

        // Entity filter dropdown
        const entityDropdown = document.getElementById('entity-filter');
        const entityBtn = document.getElementById('entity-filter-btn');
        const entityItems = document.querySelectorAll('.entity-filter-item');

        if (entityBtn && entityDropdown) {
            entityBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                closeAllDropdownsExcept('entity-filter');
                entityDropdown.classList.toggle('open');
            });

            entityItems.forEach(item => {
                item.addEventListener('click', function () {
                    currentEntity = this.dataset.value;
                    entityBtn.querySelector('.filter-text').textContent = this.textContent;
                    entityDropdown.classList.remove('open');
                    currentPage = 1;
                    loadAuditLogs();
                });
            });
        }

        // Date range filter dropdown
        const dateDropdown = document.getElementById('date-filter');
        const dateBtn = document.getElementById('date-filter-btn');
        const dateItems = document.querySelectorAll('.date-filter-item');

        if (dateBtn && dateDropdown) {
            dateBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                closeAllDropdownsExcept('date-filter');
                dateDropdown.classList.toggle('open');
            });

            dateItems.forEach(item => {
                item.addEventListener('click', function () {
                    currentDateRange = this.dataset.value;
                    dateBtn.querySelector('.filter-text').textContent = this.textContent;
                    dateDropdown.classList.remove('open');
                    currentPage = 1;
                    loadAuditLogs();
                });
            });
        }

        // Close dropdowns on outside click
        document.addEventListener('click', function () {
            document.querySelectorAll('.filter-dropdown.open').forEach(d => d.classList.remove('open'));
        });
    }

    function closeAllDropdownsExcept(keepId) {
        document.querySelectorAll('.filter-dropdown.open').forEach(d => {
            if (d.id !== keepId) d.classList.remove('open');
        });
    }

    function initRefreshButton() {
        const refreshBtn = document.getElementById('refresh-btn');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', function () {
                loadAuditLogs();
            });
        }
    }

    function startAutoRefresh() {
        // Auto-refresh every 60 seconds
        if (autoRefreshInterval) clearInterval(autoRefreshInterval);
        autoRefreshInterval = setInterval(() => {
            loadAuditLogs();
        }, 60000);
    }

    // ─── Load Audit Logs ─────────────────────────

    function toUtcDate(dateStr) {
        if (!dateStr) return new Date();
        const s = String(dateStr);
        return new Date(s.endsWith('Z') || s.includes('+') ? s : s + 'Z');
    }

    async function loadAuditLogs() {
        const tbody = document.getElementById('audit-log-body');
        if (!tbody) return;

        tbody.innerHTML = '<tr><td colspan="5" class="audit-log-state-row">Loading audit logs...</td></tr>';

        const params = new URLSearchParams({
            page: currentPage,
            pageSize: pageSize
        });

        if (currentSearch) params.append('search', currentSearch);
        if (currentAction && currentAction !== 'All') params.append('action', currentAction);
        if (currentEntity && currentEntity !== 'All') params.append('entity', currentEntity);
        if (currentDateRange) params.append('dateRange', currentDateRange);
        params.append('logType', currentLogType === 'security' ? 'Security' : 'System');

        const result = await apiGet(`/api/companyadmin/auditlogs?${params}`);

        if (!result.success) {
            tbody.innerHTML = `<tr><td colspan="5" class="audit-log-state-row audit-log-state-error">${escapeHtml(result.message)}</td></tr>`;
            return;
        }

        updateTabCounts(result.counts);

        const logs = result.data || [];
        totalPages = result.pagination?.totalPages || 1;
        const totalCount = result.pagination?.totalCount || 0;

        if (logs.length === 0) {
            tbody.innerHTML = '<tr><td colspan="5" class="audit-log-state-row">No audit logs found.</td></tr>';
            renderPagination(0);
            return;
        }

        let html = '';
        logs.forEach(log => {
            const date = toUtcDate(log.createdAt);
            const dateStr = date.toLocaleDateString('en-US', { timeZone: 'Asia/Manila', year: 'numeric', month: 'short', day: 'numeric' });
            const timeStr = date.toLocaleTimeString('en-US', { timeZone: 'Asia/Manila', hour: '2-digit', minute: '2-digit', second: '2-digit' });
            const initials = getInitials(log.userName);

            html += `<tr>
                <td class="timestamp">
                    <span class="date">${escapeHtml(dateStr)}</span>
                    <span class="time">${escapeHtml(timeStr)}</span>
                </td>
                <td class="user">
                    <div class="user-cell">
                        <span class="user-avatar">${escapeHtml(initials)}</span>
                        <div class="user-info">
                            <span class="user-name">${escapeHtml(log.userName)}</span>
                            <span class="user-email">${escapeHtml(log.userEmail)}</span>
                        </div>
                    </div>
                </td>
                <td class="action-text">${escapeHtml(log.action)}</td>
                <td class="details">${escapeHtml(log.action)} on ${escapeHtml(log.entityName)}</td>
                <td class="ip-address">${escapeHtml(log.ipAddress || '—')}</td>
            </tr>`;
        });

        tbody.innerHTML = html;
        renderPagination(totalCount);
    }

    function updateTabCounts(counts) {
        if (!counts) return;

        const systemCount = document.getElementById('system-log-count');
        const securityCount = document.getElementById('security-log-count');

        if (systemCount) {
            systemCount.textContent = String(counts.system ?? 0);
        }

        if (securityCount) {
            securityCount.textContent = String(counts.security ?? 0);
        }
    }

    // ─── Pagination ──────────────────────────────

    function renderPagination(totalCount) {
        const info = document.getElementById('pagination-info');
        const controls = document.getElementById('pagination-controls');

        if (info) {
            const start = totalCount === 0 ? 0 : (currentPage - 1) * pageSize + 1;
            const end = Math.min(currentPage * pageSize, totalCount);
            info.innerHTML = `Showing <strong>${start}-${end}</strong> of <strong>${totalCount}</strong> entries`;
        }

        if (!controls) return;

        let html = '';

        // Prev button
        html += `<button class="pagination-btn" ${currentPage <= 1 ? 'disabled' : ''} onclick="auditGoToPage(${currentPage - 1})">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 18 9 12 15 6"></polyline></svg>
        </button>`;

        // Page numbers
        const maxVisible = 5;
        let startPage = Math.max(1, currentPage - Math.floor(maxVisible / 2));
        let endPage = Math.min(totalPages, startPage + maxVisible - 1);
        if (endPage - startPage < maxVisible - 1) {
            startPage = Math.max(1, endPage - maxVisible + 1);
        }

        if (startPage > 1) {
            html += `<button class="pagination-btn" onclick="auditGoToPage(1)">1</button>`;
            if (startPage > 2) html += '<span class="pagination-ellipsis">...</span>';
        }

        for (let i = startPage; i <= endPage; i++) {
            html += `<button class="pagination-btn ${i === currentPage ? 'active' : ''}" onclick="auditGoToPage(${i})">${i}</button>`;
        }

        if (endPage < totalPages) {
            if (endPage < totalPages - 1) html += '<span class="pagination-ellipsis">...</span>';
            html += `<button class="pagination-btn" onclick="auditGoToPage(${totalPages})">${totalPages}</button>`;
        }

        // Next button
        html += `<button class="pagination-btn" ${currentPage >= totalPages ? 'disabled' : ''} onclick="auditGoToPage(${currentPage + 1})">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"></polyline></svg>
        </button>`;

        controls.innerHTML = html;
    }

    window.auditGoToPage = function (page) {
        if (page < 1 || page > totalPages) return;
        currentPage = page;
        loadAuditLogs();
    };

    // ─── Helpers ─────────────────────────────────

    function getInitials(name) {
        if (!name) return '?';
        return name.split(' ').map(w => w[0]).join('').toUpperCase().slice(0, 2);
    }

    function getActionClass(action) {
        if (!action) return '';
        const a = action.toLowerCase();
        if (a.includes('create') || a.includes('add')) return 'create';
        if (a.includes('update') || a.includes('edit') || a.includes('change')) return 'update';
        if (a.includes('delete') || a.includes('remove')) return 'delete';
        if (a.includes('archive')) return 'delete';
        if (a.includes('restore')) return 'create';
        if (a.includes('advance')) return 'update';
        if (a.includes('approve')) return 'create';
        if (a.includes('reject')) return 'delete';
        if (a.includes('reset')) return 'update';
        if (a.includes('activate')) return 'create';
        if (a.includes('deactivate')) return 'delete';
        if (a.includes('upload')) return 'upload';
        if (a.includes('login') || a.includes('auth')) return 'login';
        if (a.includes('permission') || a.includes('role')) return 'permission';
        return 'update';
    }

    function escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }
})();
