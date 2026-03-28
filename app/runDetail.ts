import { getEl, showElement, hideElement, escapeHtml, formatDateTime, statusBadge } from './utils.js';

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
const LOG_TRUNCATE_LINES = 500;

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
    if (headerStatus) headerStatus.innerHTML = statusBadge(run.status, 'md');

    document.title = `${run.appName} v${run.version} — Run Detail | Packaging Automation`;

    // Metadata fields
    const detailAppName = getEl('detailAppName');
    if (detailAppName) detailAppName.textContent = run.appName;

    const detailVersion = getEl('detailVersion');
    if (detailVersion) detailVersion.textContent = run.version;

    const detailStatus = getEl('detailStatus');
    if (detailStatus) detailStatus.innerHTML = statusBadge(run.status, 'md');

    const detailSourceType = getEl('detailSourceType');
    if (detailSourceType) detailSourceType.textContent = run.sourceType || '—';

    const detailStartTime = getEl('detailStartTime');
    if (detailStartTime) detailStartTime.textContent = formatDateTime(run.startTime, true);

    const detailEndTime = getEl('detailEndTime');
    if (detailEndTime) detailEndTime.textContent = formatDateTime(run.endTime, true);

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

    // Error summary (optional)
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

    // Create Intune App button
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

// ── Inline Log Content (with truncation) ────────────────────────────
async function fetchLogContent(logUrl: string): Promise<void> {
    try {
        const response = await fetch(logUrl);
        if (!response.ok) return;
        const text = await response.text();
        const logContent = getEl<HTMLPreElement>('logContent');
        if (!logContent) return;

        const lines = text.split('\n');
        if (lines.length > LOG_TRUNCATE_LINES) {
            logContent.textContent = lines.slice(0, LOG_TRUNCATE_LINES).join('\n');
            // Show "Show all" button
            const showAllBtn = getEl('logShowAllBtn');
            if (showAllBtn) {
                showAllBtn.textContent = `Show all ${lines.length} lines`;
                showAllBtn.classList.remove('hidden');
                showAllBtn.onclick = () => {
                    logContent.textContent = text;
                    showAllBtn.classList.add('hidden');
                };
            }
        } else {
            logContent.textContent = text;
        }
        showElement('logContentSection');
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

    const retryBtn = getEl<HTMLButtonElement>('retryBtn');
    retryBtn?.addEventListener('click', () => loadRunDetail(runId));

    const createIntuneAppBtn = getEl<HTMLButtonElement>('createIntuneAppBtn');
    createIntuneAppBtn?.addEventListener('click', () => createIntuneApp(runId));

    loadRunDetail(runId);
}

window.addEventListener('beforeunload', () => stopPolling());

if (window.appShellReady) {
    init();
} else {
    window.addEventListener('app-shell-ready', () => init());
}
