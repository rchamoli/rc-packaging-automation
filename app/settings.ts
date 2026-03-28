import { getEl } from './utils.js';

/// <reference path="globals.d.ts" />

// ── Types ───────────────────────────────────────────────────────────
interface NotificationPreferences {
    emailOnSuccess: boolean;
    emailOnFailure: boolean;
    teamsOnSuccess: boolean;
    teamsOnFailure: boolean;
    emailAddress: string | null;
    teamsWebhookUrl: string | null;
}

// ── Notification Preferences ────────────────────────────────────────
async function loadNotificationPreferences(): Promise<void> {
    const loadingEl = getEl('notifLoadingState');
    const formEl = getEl('notifForm');

    try {
        const response = await fetch('/api/notifications/preferences');
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const prefs: NotificationPreferences = await response.json();

        // Populate form
        const teamsUrl = getEl<HTMLInputElement>('teamsWebhookUrl');
        if (teamsUrl) teamsUrl.value = prefs.teamsWebhookUrl ?? '';

        const teamsOnSuccess = getEl<HTMLInputElement>('teamsOnSuccess');
        if (teamsOnSuccess) teamsOnSuccess.checked = prefs.teamsOnSuccess;

        const teamsOnFailure = getEl<HTMLInputElement>('teamsOnFailure');
        if (teamsOnFailure) teamsOnFailure.checked = prefs.teamsOnFailure;

        const emailAddr = getEl<HTMLInputElement>('emailAddress');
        if (emailAddr) emailAddr.value = prefs.emailAddress ?? '';

        const emailOnSuccess = getEl<HTMLInputElement>('emailOnSuccess');
        if (emailOnSuccess) emailOnSuccess.checked = prefs.emailOnSuccess;

        const emailOnFailure = getEl<HTMLInputElement>('emailOnFailure');
        if (emailOnFailure) emailOnFailure.checked = prefs.emailOnFailure;

        if (loadingEl) loadingEl.classList.add('hidden');
        if (formEl) formEl.classList.remove('hidden');

        // Disable form for non-admins (userRoles is guaranteed set by app-shell-ready)
        const roles = window.userRoles ?? [];
        const isAdmin = roles.includes('admin');
        if (!isAdmin) {
            formEl?.querySelectorAll('input, button[type="submit"]').forEach(el => {
                (el as HTMLInputElement).disabled = true;
            });
            const saveBtn = getEl('notifSaveBtn');
            if (saveBtn) saveBtn.title = 'Only admins can change notification preferences';
        }
    } catch (error) {
        console.error('Failed to load notification preferences:', error);
        if (loadingEl) loadingEl.textContent = 'Failed to load notification preferences.';
    }
}

async function saveNotificationPreferences(e: Event): Promise<void> {
    e.preventDefault();

    const saveBtn = getEl<HTMLButtonElement>('notifSaveBtn');
    const statusEl = getEl('notifSaveStatus');
    if (saveBtn) { saveBtn.disabled = true; saveBtn.textContent = 'Saving...'; }

    const prefs: NotificationPreferences = {
        teamsWebhookUrl: (getEl<HTMLInputElement>('teamsWebhookUrl')?.value ?? '').trim() || null,
        teamsOnSuccess: getEl<HTMLInputElement>('teamsOnSuccess')?.checked ?? true,
        teamsOnFailure: getEl<HTMLInputElement>('teamsOnFailure')?.checked ?? true,
        emailAddress: (getEl<HTMLInputElement>('emailAddress')?.value ?? '').trim() || null,
        emailOnSuccess: getEl<HTMLInputElement>('emailOnSuccess')?.checked ?? false,
        emailOnFailure: getEl<HTMLInputElement>('emailOnFailure')?.checked ?? true,
    };

    try {
        const response = await fetch('/api/notifications/preferences', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(prefs)
        });

        if (!response.ok) {
            const data = await response.json().catch(() => ({ error: 'Save failed.' }));
            throw new Error(data.error || `HTTP ${response.status}`);
        }

        if (statusEl) {
            statusEl.textContent = 'Saved!';
            statusEl.className = 'text-sm text-green-700';
            statusEl.classList.remove('hidden');
            setTimeout(() => statusEl.classList.add('hidden'), 3000);
        }
    } catch (error: any) {
        if (statusEl) {
            statusEl.textContent = error.message || 'Failed to save.';
            statusEl.className = 'text-sm text-red-700';
            statusEl.classList.remove('hidden');
        }
    } finally {
        if (saveBtn) { saveBtn.disabled = false; saveBtn.textContent = 'Save Preferences'; }
    }
}

// ── Init ────────────────────────────────────────────────────────────
function init(): void {
    loadNotificationPreferences();

    const form = getEl<HTMLFormElement>('notifForm');
    if (form) form.addEventListener('submit', saveNotificationPreferences);
}

// app-shell-ready guarantees window.userRoles is set before init runs
if (window.appShellReady) {
    init();
} else {
    window.addEventListener('app-shell-ready', () => init());
}
