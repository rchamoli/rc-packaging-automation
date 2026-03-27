export {};

/// <reference path="globals.d.ts" />

// ── Types ───────────────────────────────────────────────────────────
interface RunDetail {
    id: string;
    appName: string;
    version: string;
    status: string;
    startTime: string;
    endTime: string | null;
    sourceType: string;
    sourceLocation: string;
    logUrl: string | null;
    outputArtifactPath: string | null;
    artifactUrl: string | null;
    errorSummary: string | null;
    metadataFileReference: string | null;
    intuneAppId: string | null;
    intuneAppLink: string | null;
    createIntuneApp: boolean;
}

// ── State ───────────────────────────────────────────────────────────
let pollTimer: ReturnType<typeof setInterval> | null = null;
let currentRunId: string | null = null;

// ── Helpers ─────────────────────────────────────────────────────────
function getEl<T extends HTMLElement>(id: string): T | null {
    return document.getElementById(id) as T | null;
}

function showElement(id: string): void {
    const el = getEl(id);
    if (el) el.classList.remove('hidden');
}

function hideElement(id: string): void {
    const el = getEl(id);
    if (el) el.classList.add('hidden');
}

function escapeHtml(text: string): string {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function formatDateTime(iso: string | null): string {
    if (!iso) return '—';
    const date = new Date(iso);
    if (isNaN(date.getTime())) return '—';
    return date.toLocaleString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit'
    });
}

function statusBadge(status: string): string {
    const base = 'inline-flex items-center px-2.5 py-1 rounded-full text-xs font-semibold';
    switch (status) {
        case 'Succeeded':
            return `<span class="${base} bg-green-100 text-green-800">Succeeded</span>`;
        case 'SucceededWithWarnings':
            return `<span class="${base} bg-yellow-100 text-yellow-800">Succeeded with Warnings</span>`;
        case 'Failed':
            return `<span class="${base} bg-red-100 text-red-800">Failed</span>`;
        case 'Running':
            return `<span class="${base} bg-amber-100 text-amber-800">Running</span>`;
        case 'Queued':
            return `<span class="${base} bg-indigo-100 text-indigo-800">Queued</span>`;
        default:
            return `<span class="${base} bg-neutral-100 text-neutral-700">${escapeHtml(status)}</span>`;
    }
}

function isTerminalStatus(status: string): boolean {
    return status === 'Succeeded' || status === 'SucceededWithWarnings' || status === 'Failed';
}

// ── Rendering ───────────────────────────────────────────────────────
function renderRunDetail(run: RunDetail): void {
    hideElement('loadingState');
    hideElement('errorState');
    showElement('detailContent');

    // Page header
    const headerRunId = getEl('headerRunId');
    if (headerRunId) headerRunId.textContent = run.id;

    const headerTitle = getEl('headerTitle');
    if (headerTitle) headerTitle.textContent = `${run.appName} v${run.version}`;

    const headerSubtitle = getEl('headerSubtitle');
    if (headerSubtitle) headerSubtitle.textContent = `Run ${run.id}`;

    const headerStatus = getEl('headerStatus');
    if (headerStatus) headerStatus.innerHTML = statusBadge(run.status);

    // Update page title
    document.title = `${run.appName} v${run.version} — Run Detail | Packaging Automation`;

    // Metadata fields
    const detailAppName = getEl('detailAppName');
    if (detailAppName) detailAppName.textContent = run.appName;

    const detailVersion = getEl('detailVersion');
    if (detailVersion) detailVersion.textContent = run.version;

    const detailStatus = getEl('detailStatus');
    if (detailStatus) detailStatus.innerHTML = statusBadge(run.status);

    const detailSourceType = getEl('detailSourceType');
    if (detailSourceType) detailSourceType.textContent = run.sourceType || '—';

    const detailStartTime = getEl('detailStartTime');
    if (detailStartTime) detailStartTime.textContent = formatDateTime(run.startTime);

    const detailEndTime = getEl('detailEndTime');
    if (detailEndTime) detailEndTime.textContent = formatDateTime(run.endTime);

    const detailSourceLocation = getEl('detailSourceLocation');
    if (detailSourceLocation) detailSourceLocation.textContent = run.sourceLocation || '—';

    // Metadata file reference (optional)
    if (run.metadataFileReference) {
        showElement('detailMetadataRow');
        const detailMetadataFile = getEl('detailMetadataFile');
        if (detailMetadataFile) detailMetadataFile.textContent = run.metadataFileReference;
    }

    // Intune info (optional)
    if (run.createIntuneApp || run.intuneAppId) {
        showElement('detailIntuneRow');
        const detailIntuneApp = getEl('detailIntuneApp');
        if (detailIntuneApp) {
            if (run.intuneAppId && run.intuneAppLink) {
                detailIntuneApp.innerHTML = `<a href="${escapeHtml(run.intuneAppLink)}" target="_blank" rel="noopener noreferrer" class="text-primary hover:text-primary-dark font-medium">Created (${escapeHtml(run.intuneAppId)})</a>`;
            } else if (run.intuneAppId) {
                detailIntuneApp.textContent = `Created (${run.intuneAppId})`;
            } else if (run.createIntuneApp) {
                detailIntuneApp.textContent = 'Requested';
            }
        }
    }

    // Error summary (optional — shown for failed runs)
    if (run.errorSummary) {
        showElement('detailErrorRow');
        const detailErrorSummary = getEl('detailErrorSummary');
        if (detailErrorSummary) detailErrorSummary.textContent = run.errorSummary;
    }

    // Log link
    if (run.logUrl) {
        showElement('logLinkSection');
        hideElement('noLogSection');
        const logLink = getEl<HTMLAnchorElement>('logLink');
        if (logLink) logLink.href = run.logUrl;

        // Fetch and display inline log content
        fetchLogContent(run.logUrl);
    } else {
        hideElement('logLinkSection');
        showElement('noLogSection');
    }

    // Artifact link
    if (run.artifactUrl) {
        showElement('artifactLinkSection');
        hideElement('noArtifactSection');
        const artifactLink = getEl<HTMLAnchorElement>('artifactLink');
        if (artifactLink) artifactLink.href = run.artifactUrl;
    } else {
        hideElement('artifactLinkSection');
        showElement('noArtifactSection');
    }

    // Create Intune App button (for succeeded runs without an Intune app)
    if ((run.status === 'Succeeded' || run.status === 'SucceededWithWarnings') && !run.intuneAppId && run.artifactUrl) {
        showElement('createIntuneAppSection');
    } else {
        hideElement('createIntuneAppSection');
    }

    // Auto-refresh polling for running status
    if (run.status === 'Running') {
        startPolling();
    } else {
        stopPolling();
    }
}

// ── Inline Log Content ─────────────────────────────────────────────
async function fetchLogContent(logUrl: string): Promise<void> {
    try {
        const response = await fetch(logUrl);
        if (!response.ok) return;
        const text = await response.text();
        const logContent = getEl<HTMLPreElement>('logContent');
        if (logContent) {
            logContent.textContent = text;
            showElement('logContentSection');
        }
    } catch {
        // Silently fail — the download link is still available
    }
}

// ── Create Intune App ──────────────────────────────────────────────
async function createIntuneApp(runId: string): Promise<void> {
    const btn = getEl<HTMLButtonElement>('createIntuneAppBtn');
    const errorEl = getEl('createIntuneAppError');
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Creating…';
        btn.classList.add('opacity-60', 'cursor-not-allowed');
    }
    if (errorEl) errorEl.classList.add('hidden');

    try {
        const response = await fetch(`/api/packaging/runs/${encodeURIComponent(runId)}/intune`, {
            method: 'POST'
        });

        if (!response.ok) {
            const data = await response.json().catch(() => null);
            throw new Error(data?.error || `HTTP ${response.status}`);
        }

        // Reload run detail to show updated Intune info
        await loadRunDetail(runId);
    } catch (error) {
        const message = error instanceof Error ? error.message : 'Failed to create Intune app';
        if (errorEl) {
            errorEl.textContent = message;
            errorEl.classList.remove('hidden');
        }
        if (btn) {
            btn.disabled = false;
            btn.textContent = 'Create Intune App';
            btn.classList.remove('opacity-60', 'cursor-not-allowed');
        }
    }
}

// ── Auto-refresh Polling ───────────────────────────────────────────
function startPolling(): void {
    if (pollTimer) return;
    pollTimer = setInterval(async () => {
        if (!currentRunId) return;
        try {
            const response = await fetch(`/api/packaging/runs/${encodeURIComponent(currentRunId)}`);
            if (!response.ok) return;
            const run: RunDetail = await response.json();
            renderRunDetail(run);
        } catch {
            // Silently ignore polling errors
        }
    }, 5000);
}

function stopPolling(): void {
    if (pollTimer) {
        clearInterval(pollTimer);
        pollTimer = null;
    }
}

function showError(message: string): void {
    hideElement('loadingState');
    hideElement('detailContent');

    const errorMsg = getEl('errorMessage');
    if (errorMsg) errorMsg.textContent = message;
    showElement('errorState');
}

// ── Data Loading ────────────────────────────────────────────────────
async function loadRunDetail(runId: string): Promise<void> {
    hideElement('errorState');
    hideElement('detailContent');
    showElement('loadingState');

    try {
        const response = await fetch(`/api/packaging/runs/${encodeURIComponent(runId)}`);
        if (!response.ok) {
            const errorData = await response.json().catch(() => null);
            if (response.status === 404) {
                throw new Error('Run not found. It may have been deleted.');
            }
            throw new Error(errorData?.error || `HTTP ${response.status}`);
        }

        const run: RunDetail = await response.json();
        renderRunDetail(run);
    } catch (error) {
        const message = error instanceof Error ? error.message : 'An unexpected error occurred';
        showError(message);
    }
}

// ── Initialization ──────────────────────────────────────────────────
function init(): void {
    const params = new URLSearchParams(window.location.search);
    const runId = params.get('id');

    if (!runId) {
        showError('No run ID specified. Please navigate from the Runs list.');
        return;
    }

    currentRunId = runId;

    // Wire retry button
    const retryBtn = getEl<HTMLButtonElement>('retryBtn');
    retryBtn?.addEventListener('click', () => loadRunDetail(runId));

    // Wire Create Intune App button
    const createIntuneAppBtn = getEl<HTMLButtonElement>('createIntuneAppBtn');
    createIntuneAppBtn?.addEventListener('click', () => createIntuneApp(runId));

    loadRunDetail(runId);
}

// Cleanup on page unload
window.addEventListener('beforeunload', () => stopPolling());

// Wait for app shell ready signal (ensures auth is loaded)
if (window.appShellReady) {
    init();
} else {
    window.addEventListener('app-shell-ready', () => init());
}
