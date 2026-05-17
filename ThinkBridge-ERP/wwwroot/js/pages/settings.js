// =============================================
// ThinkBridge ERP - Settings Page
// =============================================

(function () {
    'use strict';

    let profileData = null;      // cached profile from API
    let originalProfile = null;  // snapshot for cancel/dirty check
    let lastBackupCodes = [];

    // ------------------------------------------
    // Init
    // ------------------------------------------
    document.addEventListener('DOMContentLoaded', () => {
        initNavigation();
        loadProfile();
        bindProfileActions();
        bindPasswordActions();
        bindTotpActions();
        loadTotpStatus();
        initThemeSelector();
        initAvatarColorPicker();
    });

    // ------------------------------------------
    // Sidebar navigation
    // ------------------------------------------
    function initNavigation() {
        document.querySelectorAll('.settings-nav-item').forEach(item => {
            item.addEventListener('click', (e) => {
                e.preventDefault();
                const sectionId = item.getAttribute('data-section');

                document.querySelectorAll('.settings-nav-item').forEach(n => n.classList.remove('active'));
                item.classList.add('active');

                document.querySelectorAll('.settings-section').forEach(s => s.classList.remove('active'));
                document.getElementById(sectionId)?.classList.add('active');
            });
        });
    }

    // ------------------------------------------
    // Load profile from API
    // ------------------------------------------
    async function loadProfile() {
        try {
            const res = await fetch('/api/settings/profile');
            if (!res.ok) throw new Error('Failed to load profile');
            profileData = await res.json();
            populateProfile(profileData);
            populateAccount(profileData);
        } catch (err) {
            console.error('Error loading profile:', err);
            showToast('Failed to load profile data.', 'error');
        }
    }

    // ------------------------------------------
    // Populate profile section
    // ------------------------------------------
    function populateProfile(data) {
        // Avatar initials
        const initials = ((data.fname?.[0] || '') + (data.lname?.[0] || '')).toUpperCase() || '?';
        document.getElementById('avatar-initials').textContent = initials;

        // Assign avatar color from user data
        const avatarEl = document.getElementById('settings-avatar');
        const color = data.avatarColor || '#0B4F6C';
        if (avatarEl) {
            avatarEl.style.background = color;
        }

        // Sync color picker selection
        document.querySelectorAll('.avatar-color-swatch').forEach(s => {
            s.classList.toggle('selected', s.dataset.color === color);
        });

        // Header info
        document.getElementById('profile-display-name').textContent = `${data.fname} ${data.lname}`;
        document.getElementById('profile-display-role').textContent = formatRole(data.role);

        // Form fields
        document.getElementById('first-name').value = data.fname || '';
        document.getElementById('last-name').value = data.lname || '';
        document.getElementById('email').value = data.email || '';
        document.getElementById('phone').value = data.phone || '';

        // Snapshot for cancel
        originalProfile = {
            fname: data.fname || '',
            lname: data.lname || '',
            phone: data.phone || ''
        };
    }

    // ------------------------------------------
    // Populate account section
    // ------------------------------------------
    function populateAccount(data) {
        document.getElementById('account-role').textContent = formatRole(data.role);
        document.getElementById('account-role-desc').textContent = getRoleDescription(data.role);
        document.getElementById('account-email').textContent = data.email || '—';
        document.getElementById('account-company').textContent = data.companyName || 'N/A (Platform Admin)';

        // Status badge
        const statusEl = document.getElementById('account-status');
        statusEl.textContent = data.status || '—';
        statusEl.className = 'detail-value status-badge status-' + (data.status || '').toLowerCase();

        // Dates
        document.getElementById('account-created').textContent = data.createdAt
            ? formatDate(data.createdAt) : '—';
        document.getElementById('account-last-login').textContent = data.lastLoginAt
            ? formatDateTime(data.lastLoginAt) : 'Never';
    }

    // ------------------------------------------
    // Profile save / cancel
    // ------------------------------------------
    function bindProfileActions() {
        document.getElementById('btn-save-profile')?.addEventListener('click', saveProfile);
        document.getElementById('btn-cancel-profile')?.addEventListener('click', cancelProfile);
    }

    async function saveProfile() {
        const fname = document.getElementById('first-name').value.trim();
        const lname = document.getElementById('last-name').value.trim();
        const phone = document.getElementById('phone').value.trim();
        const avatarColor = profileData?.avatarColor || '#0B4F6C';

        if (!fname || !lname) {
            showToast('First name and last name are required.', 'error');
            return;
        }

        const btn = document.getElementById('btn-save-profile');
        setButtonLoading(btn, true);

        try {
            const res = await fetch('/api/settings/profile', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ fname, lname, phone: phone || null, avatarColor })
            });

            const result = await res.json();
            if (!res.ok) throw new Error(result.message || 'Update failed');

            // Update cached data
            profileData.fname = result.fname;
            profileData.lname = result.lname;
            profileData.phone = result.phone;
            profileData.avatarColor = result.avatarColor;

            // Refresh display
            populateProfile(profileData);

            // Also refresh topbar name and color
            updateTopbar(result.fname, result.lname, result.avatarColor);

            showToast('Profile updated successfully.', 'success');
        } catch (err) {
            showToast(err.message, 'error');
        } finally {
            setButtonLoading(btn, false);
        }
    }

    function cancelProfile() {
        if (originalProfile) {
            document.getElementById('first-name').value = originalProfile.fname;
            document.getElementById('last-name').value = originalProfile.lname;
            document.getElementById('phone').value = originalProfile.phone;
        }
    }

    // ------------------------------------------
    // Password change
    // ------------------------------------------
    function bindPasswordActions() {
        document.getElementById('btn-change-password')?.addEventListener('click', changePassword);
        document.getElementById('btn-cancel-password')?.addEventListener('click', cancelPassword);

        // Real-time checklist
        document.getElementById('new-password')?.addEventListener('input', (e) => {
            updatePasswordChecklist(e.target.value);
            checkPasswordMatch();
        });

        // Match check
        document.getElementById('confirm-password')?.addEventListener('input', checkPasswordMatch);
    }

    async function changePassword() {
        const currentPassword = document.getElementById('current-password').value;
        const newPassword = document.getElementById('new-password').value;
        const confirmPassword = document.getElementById('confirm-password').value;

        if (!currentPassword) {
            showToast('Current password is required.', 'error');
            return;
        }
        if (!newPassword || newPassword.length < 12) {
            showToast('Password must be at least 12 characters.', 'error');
            return;
        }
        if (!/[A-Z]/.test(newPassword)) {
            showToast('Password must include an uppercase letter.', 'error');
            return;
        }
        if (!/[a-z]/.test(newPassword)) {
            showToast('Password must include a lowercase letter.', 'error');
            return;
        }
        if (!/[0-9]/.test(newPassword)) {
            showToast('Password must include a number.', 'error');
            return;
        }
        if (!/[^a-zA-Z0-9]/.test(newPassword)) {
            showToast('Password must include a special character.', 'error');
            return;
        }
        if (newPassword !== confirmPassword) {
            showToast('Passwords do not match.', 'error');
            return;
        }

        const btn = document.getElementById('btn-change-password');
        setButtonLoading(btn, true);

        try {
            const res = await fetch('/api/settings/change-password', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ currentPassword, newPassword, confirmPassword })
            });

            const result = await res.json();
            if (!res.ok) throw new Error(result.message || 'Password change failed');

            // Clear form
            clearPasswordForm();
            showToast('Password changed successfully.', 'success');
        } catch (err) {
            showToast(err.message, 'error');
        } finally {
            setButtonLoading(btn, false);
        }
    }

    function cancelPassword() {
        clearPasswordForm();
    }

    function clearPasswordForm() {
        document.getElementById('current-password').value = '';
        document.getElementById('new-password').value = '';
        document.getElementById('confirm-password').value = '';
        resetPasswordChecklist();
        const hint = document.getElementById('password-match-hint');
        if (hint) { hint.style.display = 'none'; hint.textContent = ''; }
    }

    // ------------------------------------------
    // TOTP Security
    // ------------------------------------------
    function bindTotpActions() {
        document.getElementById('btn-start-totp-setup')?.addEventListener('click', startTotpSetup);
        document.getElementById('btn-verify-totp-setup')?.addEventListener('click', verifyTotpSetup);
        document.getElementById('btn-show-totp-manual-key')?.addEventListener('click', toggleTotpManualKey);
        document.getElementById('btn-show-backup-codes')?.addEventListener('click', toggleBackupCodesPanel);
        document.getElementById('btn-copy-backup-codes')?.addEventListener('click', copyBackupCodes);

        document.getElementById('btn-show-regenerate-backup-form')?.addEventListener('click', function () {
            toggleSensitivePanel('totp-regenerate-form', true);
        });
        document.getElementById('btn-cancel-regenerate-backup')?.addEventListener('click', function () {
            toggleSensitivePanel('totp-regenerate-form', false);
        });
        document.getElementById('btn-regenerate-backup-codes')?.addEventListener('click', regenerateBackupCodes);

        document.getElementById('btn-show-disable-totp-form')?.addEventListener('click', function () {
            toggleSensitivePanel('totp-disable-form', true);
        });
        document.getElementById('btn-cancel-disable-totp')?.addEventListener('click', function () {
            toggleSensitivePanel('totp-disable-form', false);
        });
        document.getElementById('btn-disable-totp')?.addEventListener('click', disableTotp);
    }

    async function loadTotpStatus() {
        try {
            const res = await fetch('/api/settings/totp/status');
            const result = await res.json();
            if (!res.ok) {
                throw new Error(result.message || 'Failed to load TOTP status.');
            }

            renderTotpStatus(result);
        } catch (err) {
            renderTotpStatus({ isEnabled: false, isPendingSetup: false, hasBackupCodes: false, lockoutSeconds: 0 });
            showToast(err.message || 'Failed to load TOTP status.', 'error');
        }
    }

    function renderTotpStatus(status) {
        const badge = document.getElementById('totp-status-badge');
        const hint = document.getElementById('totp-status-hint');
        const disabledPanel = document.getElementById('totp-disabled-panel');
        const enabledPanel = document.getElementById('totp-enabled-panel');
        const setupPanel = document.getElementById('totp-setup-panel');

        if (!badge || !hint || !disabledPanel || !enabledPanel || !setupPanel) {
            return;
        }

        setupPanel.style.display = 'none';
        resetTotpManualKeyVisibility();
        toggleSensitivePanel('totp-regenerate-form', false);
        toggleSensitivePanel('totp-disable-form', false);

        if (status.isEnabled) {
            badge.textContent = 'Enabled';
            badge.className = 'status-badge status-active';
            hint.textContent = status.lockoutSeconds > 0
                ? `Temporarily locked for ${Math.ceil(status.lockoutSeconds / 60)} minute(s).`
                : 'Your account requires an authenticator code during login.';

            disabledPanel.style.display = 'none';
            enabledPanel.style.display = '';
        } else {
            badge.textContent = status.isPendingSetup ? 'Setup In Progress' : 'Disabled';
            badge.className = 'status-badge status-inactive';
            hint.textContent = 'Enable TOTP to protect your account with an extra login step.';

            disabledPanel.style.display = '';
            enabledPanel.style.display = 'none';
        }
    }

    async function startTotpSetup() {
        try {
            const res = await fetch('/api/settings/totp/setup', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });

            const result = await res.json();
            if (!res.ok) {
                throw new Error(result.message || 'Unable to start TOTP setup.');
            }

            const setupPanel = document.getElementById('totp-setup-panel');
            const manualKey = document.getElementById('totp-manual-key');
            const qrImage = document.getElementById('totp-qr-image');

            manualKey.value = result.secret || '';
            qrImage.src = `https://api.qrserver.com/v1/create-qr-code/?size=220x220&data=${encodeURIComponent(result.provisioningUri || '')}`;
            setupPanel.style.display = '';
            resetTotpManualKeyVisibility();
            document.getElementById('totp-setup-code').focus();
            showToast('Scan the QR code and verify using your authenticator app.', 'info');
        } catch (err) {
            showToast(err.message || 'Unable to start TOTP setup.', 'error');
        }
    }

    function toggleTotpManualKey() {
        const wrap = document.getElementById('totp-manual-key-wrap');
        const toggle = document.getElementById('btn-show-totp-manual-key');
        if (!wrap || !toggle) return;

        const show = wrap.style.display === 'none';
        wrap.style.display = show ? '' : 'none';
        toggle.textContent = show
            ? 'Hide manual key'
            : "Can't scan QR? Enter key manually";
    }

    function resetTotpManualKeyVisibility() {
        const wrap = document.getElementById('totp-manual-key-wrap');
        const toggle = document.getElementById('btn-show-totp-manual-key');
        if (!wrap || !toggle) return;

        wrap.style.display = 'none';
        toggle.textContent = "Can't scan QR? Enter key manually";
    }

    async function verifyTotpSetup() {
        const code = document.getElementById('totp-setup-code').value.trim();
        if (!code) {
            showToast('Please enter your authenticator code.', 'error');
            return;
        }

        try {
            const res = await fetch('/api/settings/totp/verify-setup', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ code })
            });

            const result = await res.json();
            if (!res.ok) {
                throw new Error(result.message || 'Unable to verify setup code.');
            }

            lastBackupCodes = Array.isArray(result.backupCodes) ? result.backupCodes : [];
            renderBackupCodes(lastBackupCodes);
            document.getElementById('backup-codes-panel').style.display = '';
            document.getElementById('totp-setup-code').value = '';

            await loadTotpStatus();
            showToast('Authenticator app enabled successfully.', 'success');
        } catch (err) {
            showToast(err.message || 'Unable to verify setup code.', 'error');
        }
    }

    function toggleBackupCodesPanel() {
        const panel = document.getElementById('backup-codes-panel');
        if (!panel) return;

        if (panel.style.display === 'none') {
            if (!lastBackupCodes.length) {
                showToast('No new backup codes to show. Regenerate codes to receive a fresh set.', 'info');
                return;
            }
            panel.style.display = '';
            return;
        }

        panel.style.display = 'none';
    }

    function renderBackupCodes(codes) {
        const container = document.getElementById('backup-codes-grid');
        if (!container) return;

        container.innerHTML = '';
        (codes || []).forEach(code => {
            const item = document.createElement('div');
            item.className = 'backup-code-item';
            item.textContent = code;
            container.appendChild(item);
        });
    }

    async function copyBackupCodes() {
        if (!lastBackupCodes.length) {
            showToast('No backup codes available to copy.', 'error');
            return;
        }

        try {
            await navigator.clipboard.writeText(lastBackupCodes.join('\n'));
            showToast('Backup codes copied to clipboard.', 'success');
        } catch {
            showToast('Unable to copy backup codes.', 'error');
        }
    }

    function toggleSensitivePanel(panelId, show) {
        const panel = document.getElementById(panelId);
        if (!panel) return;

        panel.style.display = show ? '' : 'none';
        if (!show) {
            panel.querySelectorAll('input').forEach(input => {
                input.value = '';
            });
        }
    }

    async function regenerateBackupCodes() {
        const currentPassword = document.getElementById('totp-regenerate-password').value;
        const code = document.getElementById('totp-regenerate-code').value.trim();
        if (!currentPassword || !code) {
            showToast('Current password and verification code are required.', 'error');
            return;
        }

        try {
            const res = await fetch('/api/settings/totp/backup-codes/regenerate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ currentPassword, code })
            });

            const result = await res.json();
            if (!res.ok) {
                throw new Error(result.message || 'Unable to regenerate backup codes.');
            }

            lastBackupCodes = Array.isArray(result.backupCodes) ? result.backupCodes : [];
            renderBackupCodes(lastBackupCodes);
            document.getElementById('backup-codes-panel').style.display = '';
            toggleSensitivePanel('totp-regenerate-form', false);

            showToast('Backup codes regenerated.', 'success');
        } catch (err) {
            showToast(err.message || 'Unable to regenerate backup codes.', 'error');
        }
    }

    async function disableTotp() {
        const currentPassword = document.getElementById('totp-disable-password').value;
        const code = document.getElementById('totp-disable-code').value.trim();
        if (!currentPassword || !code) {
            showToast('Current password and verification code are required.', 'error');
            return;
        }

        try {
            const res = await fetch('/api/settings/totp/disable', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ currentPassword, code })
            });

            const result = await res.json();
            if (!res.ok) {
                throw new Error(result.message || 'Unable to disable authenticator app.');
            }

            lastBackupCodes = [];
            renderBackupCodes(lastBackupCodes);
            document.getElementById('backup-codes-panel').style.display = 'none';
            toggleSensitivePanel('totp-disable-form', false);
            await loadTotpStatus();

            showToast(result.message || 'Authenticator app disabled.', 'success');
        } catch (err) {
            showToast(err.message || 'Unable to disable authenticator app.', 'error');
        }
    }

    // ------------------------------------------
    // Password checklist
    // ------------------------------------------
    function updatePasswordChecklist(password) {
        const checklist = document.getElementById('passwordChecklist');
        if (!checklist) return;

        checklist.style.display = password.length > 0 ? 'block' : 'none';

        const update = (id, pass) => {
            const el = document.getElementById(id);
            if (!el) return;
            el.classList.toggle('valid', pass);
            el.classList.toggle('invalid', !pass);
        };

        update('check-length', password.length >= 12);
        update('check-lowercase', /[a-z]/.test(password));
        update('check-uppercase', /[A-Z]/.test(password));
        update('check-number', /[0-9]/.test(password));
        update('check-special', /[^a-zA-Z0-9]/.test(password));
    }

    function resetPasswordChecklist() {
        const checklist = document.getElementById('passwordChecklist');
        if (checklist) checklist.style.display = 'none';
        ['check-length', 'check-lowercase', 'check-uppercase', 'check-number', 'check-special'].forEach(id => {
            const el = document.getElementById(id);
            if (el) { el.classList.remove('valid', 'invalid'); }
        });
    }

    function checkPasswordMatch() {
        const pw = document.getElementById('new-password').value;
        const cpw = document.getElementById('confirm-password').value;
        const hint = document.getElementById('password-match-hint');

        if (!cpw) {
            hint.style.display = 'none';
            return;
        }

        hint.style.display = 'block';
        if (pw === cpw) {
            hint.textContent = 'Passwords match';
            hint.style.color = '#10b981';
        } else {
            hint.textContent = 'Passwords do not match';
            hint.style.color = '#ef4444';
        }
    }

    // ------------------------------------------
    // Password visibility toggle
    // ------------------------------------------
    window.togglePasswordVisibility = function (inputId, btn) {
        const input = document.getElementById(inputId);
        if (!input) return;
        const isPassword = input.type === 'password';
        input.type = isPassword ? 'text' : 'password';
        btn.classList.toggle('active', isPassword);
    };

    // ------------------------------------------
    // Update topbar after profile save
    // ------------------------------------------
    function updateTopbar(fname, lname, avatarColor) {
        const fullName = `${fname} ${lname}`;
        // Try known topbar selectors
        const nameEl = document.querySelector('.user-name') || document.querySelector('.topbar-user-name');
        if (nameEl) nameEl.textContent = fullName;

        const avatarSpans = document.querySelectorAll('.user-avatar span, .topbar-avatar span');
        const initials = ((fname?.[0] || '') + (lname?.[0] || '')).toUpperCase();
        avatarSpans.forEach(s => s.textContent = initials);

        // Update topbar avatar color
        if (avatarColor) {
            const topbarAvatar = document.getElementById('topbar-avatar');
            if (topbarAvatar) topbarAvatar.style.background = avatarColor;
        }
    }

    // ------------------------------------------
    // Avatar Color Picker
    // ------------------------------------------
    function initAvatarColorPicker() {
        const container = document.getElementById('avatar-color-options');
        if (!container) return;

        container.addEventListener('click', (e) => {
            const swatch = e.target.closest('.avatar-color-swatch');
            if (!swatch) return;

            const color = swatch.dataset.color;
            if (!color) return;

            // Update selection UI
            container.querySelectorAll('.avatar-color-swatch').forEach(s => s.classList.remove('selected'));
            swatch.classList.add('selected');

            // Update cached profile data
            if (profileData) profileData.avatarColor = color;

            // Live preview on settings avatar
            const avatarEl = document.getElementById('settings-avatar');
            if (avatarEl) avatarEl.style.background = color;

            // Live preview on topbar avatar
            const topbarAvatar = document.getElementById('topbar-avatar');
            if (topbarAvatar) topbarAvatar.style.background = color;

            // Auto-save the color
            saveAvatarColor(color);
        });
    }

    async function saveAvatarColor(color) {
        if (!profileData) return;
        try {
            const res = await fetch('/api/settings/profile', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    fname: profileData.fname,
                    lname: profileData.lname,
                    phone: profileData.phone || null,
                    avatarColor: color
                })
            });
            const result = await res.json();
            if (!res.ok) throw new Error(result.message || 'Update failed');
            profileData.avatarColor = result.avatarColor;
            showToast('Avatar color updated.', 'success');
        } catch (err) {
            showToast('Failed to save avatar color.', 'error');
        }
    }

    // ------------------------------------------
    // Helpers
    // ------------------------------------------
    function formatRole(role) {
        if (!role) return '—';
        const map = {
            'SuperAdmin': 'Super Admin',
            'CompanyAdmin': 'Company Admin',
            'ProjectManager': 'Project Manager',
            'TeamMember': 'Team Member'
        };
        return map[role] || role;
    }

    function getRoleDescription(role) {
        const desc = {
            'SuperAdmin': 'Full platform access. Manage all companies, subscriptions, and system settings.',
            'CompanyAdmin': 'Full company access. Manage users, projects, billing, and company settings.',
            'ProjectManager': 'Create and manage projects, assign tasks, and oversee team activities.',
            'TeamMember': 'View assigned tasks, collaborate on projects, and participate in team activities.'
        };
        return desc[role] || 'Standard user role.';
    }

    function toUtcDate(dateStr) {
        if (!dateStr) return new Date();
        const s = String(dateStr);
        return new Date(s.endsWith('Z') || s.includes('+') ? s : s + 'Z');
    }

    function formatDate(dateStr) {
        if (!dateStr) return '—';
        return toUtcDate(dateStr).toLocaleDateString('en-US', {
            timeZone: 'Asia/Manila', year: 'numeric', month: 'long', day: 'numeric'
        });
    }

    function formatDateTime(dateStr) {
        if (!dateStr) return '—';
        return toUtcDate(dateStr).toLocaleString('en-US', {
            timeZone: 'Asia/Manila', year: 'numeric', month: 'short', day: 'numeric',
            hour: 'numeric', minute: '2-digit'
        });
    }

    function setButtonLoading(btn, loading) {
        if (!btn) return;
        const text = btn.querySelector('.btn-text');
        const spinner = btn.querySelector('.btn-spinner');
        if (loading) {
            btn.disabled = true;
            if (text) text.style.display = 'none';
            if (spinner) spinner.style.display = 'inline-flex';
        } else {
            btn.disabled = false;
            if (text) text.style.display = 'inline';
            if (spinner) spinner.style.display = 'none';
        }
    }

    // ------------------------------------------
    // Toast
    // ------------------------------------------
    function showToast(message, type = 'info') {
        const toast = document.getElementById('settings-toast');
        const msgEl = document.getElementById('toast-message');
        const iconEl = document.getElementById('toast-icon');

        if (!toast || !msgEl) return;

        // Icon
        const icons = {
            success: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"></polyline></svg>',
            error: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>',
            info: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>'
        };

        iconEl.innerHTML = icons[type] || icons.info;
        msgEl.textContent = message;

        toast.className = 'settings-toast show ' + type;

        clearTimeout(toast._timeout);
        toast._timeout = setTimeout(() => {
            toast.classList.remove('show');
        }, 3500);
    }

    // ------------------------------------------
    // Theme Selector
    // ------------------------------------------
    function initThemeSelector() {
        const lightOption = document.getElementById('theme-light');
        const darkOption = document.getElementById('theme-dark');

        if (!lightOption || !darkOption) return;

        // Read current theme from localStorage / document attribute
        const currentTheme = document.documentElement.getAttribute('data-theme') || 'light';
        setThemeSelection(currentTheme);

        lightOption.addEventListener('click', () => applyTheme('light'));
        darkOption.addEventListener('click', () => applyTheme('dark'));
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem('thinkbridge-theme', theme);
        setThemeSelection(theme);
        showToast(theme === 'dark' ? 'Dark mode enabled.' : 'Light mode enabled.', 'success');
    }

    function setThemeSelection(theme) {
        const lightOption = document.getElementById('theme-light');
        const darkOption = document.getElementById('theme-dark');
        if (!lightOption || !darkOption) return;

        lightOption.classList.toggle('selected', theme === 'light');
        darkOption.classList.toggle('selected', theme === 'dark');
    }

})();
