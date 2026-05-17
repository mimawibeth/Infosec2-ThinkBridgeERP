/**
 * ThinkBridge ERP - Global JavaScript
 * ==========================================
 */

(function () {
    'use strict';

    // ===========================================
    // DOM Ready Handler
    // ===========================================
    document.addEventListener('DOMContentLoaded', function () {
        initSidebar();
        initActiveNavHighlight();
        initDropdowns();
        initModals();
        initTabs();
        initSettingsNav();
        initTopbarDate();
        initSessionManager();
        initSubscriptionAlert();
    });

    // ===========================================
    // Sidebar Toggle
    // ===========================================
    function initSidebar() {
        const sidebar = document.querySelector('.sidebar');
        const sidebarToggle = document.querySelector('.sidebar-toggle');
        const sidebarOverlay = document.querySelector('.sidebar-overlay');
        const sidebarCollapseBtn = document.querySelector('.sidebar-collapse-btn');

        // Mobile toggle
        if (sidebarToggle) {
            sidebarToggle.addEventListener('click', function () {
                sidebar.classList.toggle('open');
                sidebarOverlay.classList.toggle('active');
            });
        }

        // Overlay click to close
        if (sidebarOverlay) {
            sidebarOverlay.addEventListener('click', function () {
                sidebar.classList.remove('open');
                sidebarOverlay.classList.remove('active');
            });
        }

        // Desktop collapse toggle
        if (sidebarCollapseBtn) {
            sidebarCollapseBtn.addEventListener('click', function () {
                document.body.classList.toggle('sidebar-collapsed');

                // Save preference
                const isCollapsed = document.body.classList.contains('sidebar-collapsed');
                localStorage.setItem('sidebarCollapsed', isCollapsed);
            });

            // Restore preference
            const savedState = localStorage.getItem('sidebarCollapsed');
            if (savedState === 'true') {
                document.body.classList.add('sidebar-collapsed');
            }
        }
    }

    // ===========================================
    // Active Navigation Highlight
    // ===========================================
    function initActiveNavHighlight() {
        const currentPath = window.location.pathname.toLowerCase();
        const navLinks = document.querySelectorAll('.nav-link');

        navLinks.forEach(function (link) {
            const href = link.getAttribute('href');
            if (href) {
                // Extract just the pathname from the href (ignore query params)
                let hrefPath;
                try {
                    // Handle both absolute and relative URLs
                    const url = new URL(href, window.location.origin);
                    hrefPath = url.pathname.toLowerCase();
                } catch (e) {
                    // Fallback: split on '?' to remove query params
                    hrefPath = href.split('?')[0].toLowerCase();
                }

                // Check if current path matches the nav link path
                if (currentPath === hrefPath ||
                    (hrefPath !== '/' && currentPath.startsWith(hrefPath))) {
                    link.classList.add('active');
                } else {
                    link.classList.remove('active');
                }
            }
        });
    }

    // ===========================================
    // Dropdowns (Notifications & User)
    // ===========================================
    function initDropdowns() {
        // Notification dropdown — toggle handled by toggleNotifications() called from onclick
        const notificationBtn = document.querySelector('.notification-btn');
        const notificationDropdown = document.querySelector('.notification-dropdown');

        // User dropdown
        const userDropdownBtn = document.querySelector('.user-dropdown-btn');
        const userDropdown = document.querySelector('.user-dropdown');

        if (userDropdownBtn && userDropdown) {
            userDropdownBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                userDropdown.classList.toggle('active');
                userDropdownBtn.classList.toggle('active');

                // Close notification dropdown if open
                if (notificationDropdown) {
                    notificationDropdown.classList.remove('active');
                }
            });
        }

        // Close dropdowns when clicking outside
        document.addEventListener('click', function () {
            if (notificationDropdown) {
                notificationDropdown.classList.remove('active');
            }
            if (userDropdown) {
                userDropdown.classList.remove('active');
                if (userDropdownBtn) {
                    userDropdownBtn.classList.remove('active');
                }
            }
        });

        // Prevent dropdown from closing when clicking inside
        const dropdowns = document.querySelectorAll('.notification-dropdown, .user-dropdown');
        dropdowns.forEach(function (dropdown) {
            dropdown.addEventListener('click', function (e) {
                e.stopPropagation();
            });
        });

        // Load initial unread count
        loadUnreadCount();
        // Poll every 30 seconds
        setInterval(loadUnreadCount, 30000);
    }

    // ---- Notification Functions (Global) ----
    let notificationsLoaded = false;

    function getNotifLink(notifType) {
        const role = (document.getElementById('topbar-user-role')?.value || '').toLowerCase();
        const taskPage = (role === 'teammember') ? '/Web/MyTasks' : '/Web/Tasks';
        const map = {
            'task': taskPage,
            'task_assigned': taskPage,
            'task_status': taskPage,
            'mention': taskPage,
            'lifecycle': '/Web/ProductLifecycle',
            'ArticleApproved': '/Web/KnowledgeBase',
            'ArticleRejected': '/Web/KnowledgeBase',
            'ArticleRevision': '/Web/KnowledgeBase',
            'ArticleComment': '/Web/KnowledgeBase',
            'ArticlePending': '/Web/KnowledgeBase',
            'post': '/Web/Activity',
            'comment': '/Web/Activity',
            'Calendar': '/Web/Calendar'
        };
        return map[notifType] || null;
    }

    window.toggleNotifications = function (e) {
        if (e) e.stopPropagation();
        const dropdown = document.getElementById('notification-dropdown');
        const isOpen = dropdown.classList.contains('active');

        // Close user dropdown
        const userDropdown = document.querySelector('.user-dropdown');
        if (userDropdown) userDropdown.classList.remove('active');

        if (isOpen) {
            dropdown.classList.remove('active');
        } else {
            dropdown.classList.add('active');
            loadNotifications();
        }
    };

    async function loadUnreadCount() {
        try {
            const res = await fetch('/api/notifications/unread-count');
            const json = await res.json();
            if (json.success) {
                const dot = document.getElementById('notification-dot');
                if (dot) {
                    dot.style.display = json.data > 0 ? '' : 'none';
                }
            }
        } catch (e) { /* silent */ }
    }

    async function loadNotifications() {
        try {
            const res = await fetch('/api/notifications?pageSize=15');
            const json = await res.json();
            if (!json.success) return;

            const list = document.getElementById('notification-list');
            if (!list) return;
            const notifications = json.data || [];

            if (notifications.length === 0) {
                list.innerHTML = '<div style="text-align:center;padding:24px;color:var(--text-muted);font-size:0.82rem;">No notifications yet.</div>';
                return;
            }

            list.innerHTML = '';
            notifications.forEach(n => {
                const notificationId = Number(n.notificationID);
                const notifType = n && n.notifType != null ? String(n.notifType) : '';
                const iconType = getNotifIconType(notifType);
                const timeAgo = formatNotifTime(n.createdAt);

                const item = document.createElement('div');
                item.className = 'notification-item';
                if (!n.isRead) item.classList.add('unread');
                item.dataset.notifId = String(notificationId);
                item.dataset.notifType = notifType;

                item.addEventListener('click', function () {
                    if (!Number.isFinite(notificationId)) return;
                    window.handleNotificationClick(notificationId, item);
                });

                const icon = document.createElement('div');
                icon.className = 'notification-icon ' + iconType.cssClass;
                icon.innerHTML = iconType.svg;

                const content = document.createElement('div');
                content.className = 'notification-content';

                const messageEl = document.createElement('p');
                messageEl.textContent = n && n.message != null ? String(n.message) : '';

                const timeEl = document.createElement('span');
                timeEl.className = 'notification-time';
                timeEl.textContent = String(timeAgo);

                content.appendChild(messageEl);
                content.appendChild(timeEl);

                const deleteBtn = document.createElement('button');
                deleteBtn.className = 'notif-delete-btn';
                deleteBtn.title = 'Delete';
                deleteBtn.type = 'button';
                deleteBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>';

                deleteBtn.addEventListener('click', function (event) {
                    event.stopPropagation();
                    if (!Number.isFinite(notificationId)) return;
                    window.deleteNotification(notificationId, deleteBtn);
                });

                item.appendChild(icon);
                item.appendChild(content);
                item.appendChild(deleteBtn);
                list.appendChild(item);
            });

            notificationsLoaded = true;
        } catch (e) { console.error('Failed to load notifications', e); }
    }

    function getNotifIconType(type) {
        const types = {
            'mention': { cssClass: 'mention', svg: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="4"></circle><path d="M16 8v5a3 3 0 0 0 6 0v-1a10 10 0 1 0-3.92 7.94"></path></svg>' },
            'task_assigned': { cssClass: 'task', svg: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 11l3 3L22 4"></path><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"></path></svg>' },
            'task_status': { cssClass: 'task', svg: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 11l3 3L22 4"></path></svg>' },
            'product': { cssClass: 'lifecycle', svg: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg>' },
        };
        return types[type] || { cssClass: 'task', svg: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"></path></svg>' };
    }

    function toUtcDate(dateStr) {
        if (!dateStr) return new Date();
        const s = String(dateStr);
        return new Date(s.endsWith('Z') || s.includes('+') ? s : s + 'Z');
    }

    function formatNotifTime(dateStr) {
        const seconds = Math.floor((Date.now() - toUtcDate(dateStr).getTime()) / 1000);
        if (seconds < 60) return 'Just now';
        const minutes = Math.floor(seconds / 60);
        if (minutes < 60) return `${minutes} min ago`;
        const hours = Math.floor(minutes / 60);
        if (hours < 24) return `${hours}h ago`;
        const days = Math.floor(hours / 24);
        if (days < 7) return `${days}d ago`;
        return toUtcDate(dateStr).toLocaleDateString('en-US', { timeZone: 'Asia/Manila' });
    }

    window.handleNotificationClick = async function (notifId, el) {
        // Mark as read
        if (el.classList.contains('unread')) {
            try {
                await fetch(`/api/notifications/${notifId}/read`, { method: 'PATCH' });
                el.classList.remove('unread');
                loadUnreadCount();
            } catch (e) { /* silent */ }
        }
        // Navigate to the source page
        const notifType = el.dataset.notifType || '';
        const link = getNotifLink(notifType);
        if (link) {
            window.location.href = link;
        }
    };

    window.markAllNotificationsRead = async function (e) {
        if (e) e.preventDefault();
        try {
            const res = await fetch('/api/notifications/read-all', { method: 'PATCH' });
            const json = await res.json();
            if (json.success) {
                document.querySelectorAll('.notification-item.unread').forEach(el => el.classList.remove('unread'));
                loadUnreadCount();
            }
        } catch (e) { console.error('Failed to mark all as read', e); }
    };

    window.deleteNotification = async function (notifId, btn) {
        try {
            const res = await fetch(`/api/notifications/${notifId}`, { method: 'DELETE' });
            const json = await res.json();
            if (json.success) {
                const item = btn.closest('.notification-item');
                if (item) {
                    item.style.opacity = '0';
                    item.style.height = '0';
                    item.style.overflow = 'hidden';
                    item.style.transition = 'all 0.2s ease';
                    setTimeout(() => {
                        item.remove();
                        loadUnreadCount();
                        // Check if list is empty
                        const list = document.getElementById('notification-list');
                        if (!list.querySelector('.notification-item')) {
                            list.innerHTML = '<div style="text-align:center;padding:24px;color:var(--text-muted);font-size:0.82rem;">No notifications yet.</div>';
                        }
                    }, 200);
                }
            }
        } catch (e) { console.error('Failed to delete notification', e); }
    };

    // ===========================================
    // Modal Management
    // ===========================================
    function initModals() {
        // Open modal buttons
        const modalTriggers = document.querySelectorAll('[data-modal-open]');
        modalTriggers.forEach(function (trigger) {
            trigger.addEventListener('click', function () {
                const modalId = this.getAttribute('data-modal-open');
                openModal(modalId);
            });
        });

        // Close modal buttons
        const modalCloseButtons = document.querySelectorAll('[data-modal-close]');
        modalCloseButtons.forEach(function (btn) {
            btn.addEventListener('click', function () {
                const modal = this.closest('.modal-overlay');
                if (modal) {
                    closeModal(modal.id);
                }
            });
        });

        // Close modal when clicking overlay
        const modalOverlays = document.querySelectorAll('.modal-overlay');
        modalOverlays.forEach(function (overlay) {
            overlay.addEventListener('click', function (e) {
                if (e.target === overlay) {
                    closeModal(overlay.id);
                }
            });
        });

        // Close modal on escape key
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                const activeModal = document.querySelector('.modal-overlay.active');
                if (activeModal) {
                    closeModal(activeModal.id);
                }
            }
        });
    }

    function openModal(modalId) {
        const modal = document.getElementById(modalId);
        if (modal) {
            modal.classList.add('active');
            document.body.style.overflow = 'hidden';
        }
    }

    function closeModal(modalId) {
        const modal = document.getElementById(modalId);
        if (modal) {
            modal.classList.remove('active');
            document.body.style.overflow = '';
        }
    }

    // Expose modal functions globally
    window.openModal = openModal;
    window.closeModal = closeModal;

    // ===========================================
    // Tabs
    // ===========================================
    function initTabs() {
        const tabContainers = document.querySelectorAll('.tabs-container');

        tabContainers.forEach(function (container) {
            const tabs = container.querySelectorAll('.tab-btn');

            tabs.forEach(function (tab) {
                tab.addEventListener('click', function () {
                    // Remove active from all tabs
                    tabs.forEach(function (t) {
                        t.classList.remove('active');
                    });

                    // Add active to clicked tab
                    this.classList.add('active');

                    // Handle tab content if data-tab attribute exists
                    const tabId = this.getAttribute('data-tab');
                    if (tabId) {
                        const tabContents = document.querySelectorAll('.tab-content');
                        tabContents.forEach(function (content) {
                            content.classList.remove('active');
                        });

                        const activeContent = document.getElementById(tabId);
                        if (activeContent) {
                            activeContent.classList.add('active');
                        }
                    }
                });
            });
        });
    }

    // ===========================================
    // Settings Navigation
    // ===========================================
    function initSettingsNav() {
        const settingsNavItems = document.querySelectorAll('.settings-nav-item');
        const settingsSections = document.querySelectorAll('.settings-section');

        if (settingsNavItems.length === 0) return;

        settingsNavItems.forEach(function (item) {
            item.addEventListener('click', function (e) {
                e.preventDefault();

                // Remove active from all nav items
                settingsNavItems.forEach(function (nav) {
                    nav.classList.remove('active');
                });

                // Add active to clicked item
                this.classList.add('active');

                // Show corresponding section
                const sectionId = this.getAttribute('data-section');
                if (sectionId) {
                    settingsSections.forEach(function (section) {
                        section.style.display = 'none';
                    });

                    const activeSection = document.getElementById(sectionId);
                    if (activeSection) {
                        activeSection.style.display = 'block';
                    }
                }
            });
        });
    }

    // ===========================================
    // Form Validation Helpers
    // ===========================================
    window.validateForm = function (formId) {
        const form = document.getElementById(formId);
        if (!form) return false;

        let isValid = true;
        const requiredFields = form.querySelectorAll('[required]');

        requiredFields.forEach(function (field) {
            if (!field.value.trim()) {
                isValid = false;
                field.classList.add('error');
            } else {
                field.classList.remove('error');
            }
        });

        return isValid;
    };

    // ===========================================
    // Topbar Date
    // ===========================================
    function initTopbarDate() {
        var dateEl = document.getElementById('topbar-date');
        if (!dateEl) return;

        function updateDate() {
            var now = new Date();
            var options = { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' };
            dateEl.textContent = now.toLocaleDateString(undefined, options);
        }

        updateDate();
        // Update at midnight
        var now = new Date();
        var msUntilMidnight = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1) - now;
        setTimeout(function () {
            updateDate();
            setInterval(updateDate, 86400000);
        }, msUntilMidnight);
    }

    // ===========================================
    // Toast Notifications
    // ===========================================
    window.showToast = function (message, type) {
        type = type || 'info';

        const icons = {
            success: '✓',
            error: '✕',
            warning: '!',
            info: 'i'
        };

        // Create toast container if it doesn't exist
        let toastContainer = document.querySelector('.toast-container');
        if (!toastContainer) {
            toastContainer = document.createElement('div');
            toastContainer.className = 'toast-container';
            document.body.appendChild(toastContainer);
        }

        // Create toast
        const toast = document.createElement('div');
        toast.className = 'toast toast-' + type;

        const icon = document.createElement('span');
        icon.className = 'toast-icon';
        icon.textContent = icons[type] || icons.info;

        const messageEl = document.createElement('span');
        messageEl.className = 'toast-message';
        messageEl.textContent = message == null ? '' : String(message);

        const closeBtn = document.createElement('button');
        closeBtn.className = 'toast-close';
        closeBtn.type = 'button';
        closeBtn.textContent = '×';
        closeBtn.addEventListener('click', function () {
            toast.classList.add('toast-exit');
            setTimeout(function () { toast.remove(); }, 300);
        });

        toast.appendChild(icon);
        toast.appendChild(messageEl);
        toast.appendChild(closeBtn);

        toastContainer.appendChild(toast);

        // Remove after 3.5 seconds
        setTimeout(function () {
            if (toast.parentElement) {
                toast.classList.add('toast-exit');
                setTimeout(function () { toast.remove(); }, 300);
            }
        }, 3500);
    };

    // ===========================================
    // Password Toggle
    // ===========================================
    window.togglePassword = function (inputId) {
        const input = document.getElementById(inputId);
        if (input) {
            input.type = input.type === 'password' ? 'text' : 'password';
        }
    };

    // ===========================================
    // Session Management & Security
    // ===========================================
    function initSessionManager() {
        // Only run on authenticated pages (pages with sidebar = logged in)
        const isAuthPage = !!document.querySelector('.sidebar');
        if (!isAuthPage) return;

        const INACTIVITY_LIMIT = 5 * 60 * 1000; // 5 minutes in ms
        const WARNING_BEFORE = 2 * 60 * 1000;   // Show warning 2 min before expiry
        const WARNING_AT = INACTIVITY_LIMIT - WARNING_BEFORE; // 3 minutes

        let inactivityTimer = null;
        let warningTimer = null;
        let countdownInterval = null;
        let warningModal = null;
        let countdownDisplay = null;
        let stayLoggedInButton = null;
        let lastActivityAt = Date.now();

        function ensureWarningModal() {
            if (warningModal) return;

            warningModal = document.createElement('div');
            warningModal.className = 'session-timeout-overlay';
            warningModal.setAttribute('role', 'dialog');
            warningModal.setAttribute('aria-modal', 'true');
            warningModal.setAttribute('aria-labelledby', 'session-timeout-title');
            warningModal.innerHTML = [
                '<div class="modal modal-sm session-timeout-modal">',
                '  <div class="modal-header">',
                '    <h2 id="session-timeout-title">Session Inactivity Warning</h2>',
                '  </div>',
                '  <div class="modal-body">',
                '    <p class="session-timeout-message">You\'ve been inactive for a while. Do you want to stay logged in?</p>',
                '    <p class="session-timeout-countdown">For security, you will be logged out automatically in <strong id="session-timeout-remaining">02:00</strong>.</p>',
                '  </div>',
                '  <div class="modal-footer session-timeout-actions">',
                '    <button type="button" class="btn btn-primary" id="session-timeout-stay">Stay Logged In</button>',
                '    <button type="button" class="btn btn-outline-danger" id="session-timeout-logout">Logout</button>',
                '  </div>',
                '</div>'
            ].join('');

            document.body.appendChild(warningModal);
            countdownDisplay = warningModal.querySelector('#session-timeout-remaining');
            stayLoggedInButton = warningModal.querySelector('#session-timeout-stay');

            stayLoggedInButton.addEventListener('click', function () {
                lastActivityAt = Date.now();
                resetTimers();
            });

            const logoutButton = warningModal.querySelector('#session-timeout-logout');
            logoutButton.addEventListener('click', function () {
                performLogout('You were logged out after inactivity.');
            });
        }

        function formatRemaining(ms) {
            const totalSeconds = Math.max(0, Math.ceil(ms / 1000));
            const minutes = String(Math.floor(totalSeconds / 60)).padStart(2, '0');
            const seconds = String(totalSeconds % 60).padStart(2, '0');
            return minutes + ':' + seconds;
        }

        function hideWarningModal() {
            if (!warningModal) return;
            warningModal.classList.remove('active');
            if (countdownInterval) {
                clearInterval(countdownInterval);
                countdownInterval = null;
            }
        }

        function updateCountdown() {
            if (!countdownDisplay) return;
            const elapsed = Date.now() - lastActivityAt;
            const remaining = INACTIVITY_LIMIT - elapsed;

            if (remaining <= 0) {
                performLogout('Your session has expired due to inactivity.');
                return;
            }

            countdownDisplay.textContent = formatRemaining(remaining);
        }

        function showSessionWarning() {
            ensureWarningModal();
            updateCountdown();
            warningModal.classList.add('active');
            if (stayLoggedInButton) {
                stayLoggedInButton.focus();
            }

            if (countdownInterval) {
                clearInterval(countdownInterval);
            }
            countdownInterval = setInterval(updateCountdown, 1000);
        }

        function resetTimers() {
            clearTimeout(inactivityTimer);
            clearTimeout(warningTimer);
            hideWarningModal();

            // Warning modal at 3 minutes
            warningTimer = setTimeout(function () {
                showSessionWarning();
            }, WARNING_AT);

            // Auto-logout at 5 minutes of inactivity
            inactivityTimer = setTimeout(function () {
                performLogout('Your session has expired due to inactivity.');
            }, INACTIVITY_LIMIT);
        }

        function performLogout(message) {
            // Clear all client-side storage
            if (countdownInterval) {
                clearInterval(countdownInterval);
                countdownInterval = null;
            }
            sessionStorage.clear();
            localStorage.removeItem('sidebarCollapsed');
            localStorage.removeItem('tb_onboarding_done');

            // Server-side sign out via GET
            window.location.href = '/Auth/Logout';
        }

        // Track user activity (mouse, keyboard, navigation, scroll, touch)
        var activityEvents = ['mousemove', 'mousedown', 'keydown', 'scroll', 'touchstart', 'click'];
        var throttled = false;
        activityEvents.forEach(function (evt) {
            document.addEventListener(evt, function () {
                if (warningModal && warningModal.classList.contains('active')) {
                    return;
                }
                if (!throttled) {
                    throttled = true;
                    lastActivityAt = Date.now();
                    resetTimers();
                    setTimeout(function () { throttled = false; }, 3000); // Throttle to every 3s
                }
            }, { passive: true });
        });

        window.addEventListener('focus', function () {
            if (warningModal && warningModal.classList.contains('active')) {
                return;
            }
            lastActivityAt = Date.now();
            resetTimers();
        });

        window.addEventListener('popstate', function () {
            lastActivityAt = Date.now();
            resetTimers();
        });

        window.addEventListener('hashchange', function () {
            lastActivityAt = Date.now();
            resetTimers();
        });

        document.addEventListener('visibilitychange', function () {
            if (!document.hidden && !(warningModal && warningModal.classList.contains('active'))) {
                lastActivityAt = Date.now();
                resetTimers();
            }
        });

        // Start timers
        resetTimers();

        // Handle tab/browser close: clear session storage
        window.addEventListener('beforeunload', function () {
            // Mark that we are navigating away (page transitions should not clear)
            sessionStorage.setItem('_tb_navigating', '1');
        });

        // On page load, check if this is a fresh tab open (not a navigation)
        // If _tb_navigating flag was NOT set, it means fresh tab → session likely expired
        if (!sessionStorage.getItem('_tb_navigating')) {
            // Fresh tab/browser open - server cookie will handle auth
            // Just ensure sessionStorage is clean for security
        }
        // Always remove the navigation flag on load so next close is treated properly
        sessionStorage.removeItem('_tb_navigating');

        // Intercept fetch to inject CSRF token and handle 401 redirects
        var originalFetch = window.fetch;
        window.fetch = function (url, options) {
            options = options || {};
            var method = (options.method || 'GET').toUpperCase();
            if (method !== 'GET' && method !== 'HEAD') {
                var token = document.querySelector('input[name="__RequestVerificationToken"]');
                if (token) {
                    options.headers = options.headers || {};
                    if (!options.headers['RequestVerificationToken']) {
                        options.headers['RequestVerificationToken'] = token.value;
                    }
                }
            }
            return originalFetch.call(this, url, options).then(function (response) {
                if (response.status === 401) {
                    sessionStorage.clear();
                    window.location.href = '/Auth/Login';
                }
                return response;
            });
        };
    }

    // ===========================================
    // Subscription Alert (Red Dot)
    // ===========================================
    function initSubscriptionAlert() {
        var dot = document.getElementById('subscriptionAlertDot');
        if (!dot) return; // Not on a page with sidebar

        fetch('/api/companyadmin/subscription/alert', { credentials: 'same-origin' })
            .then(function (resp) { return resp.ok ? resp.json() : null; })
            .then(function (result) {
                if (!result || !result.success || !result.data) {
                    dot.style.display = 'none';
                    return;
                }
                var alert = result.data;
                dot.style.display = 'inline-block';
                dot.title = alert.message;
            })
            .catch(function () {
                dot.style.display = 'none';
            });
    }

    // Global logout function (used by logout buttons/links)
    window.performSecureLogout = async function () {
        try {
            // Clear all client-side storage
            sessionStorage.clear();
            localStorage.removeItem('sidebarCollapsed');
            localStorage.removeItem('tb_onboarding_done');

            // Call server logout
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (token) {
                await fetch('/Auth/Logout', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': token
                    },
                    credentials: 'same-origin'
                });
            }
        } catch (e) {
            // Ignore errors
        }
        window.location.href = '/Auth/Login';
    };

})();
