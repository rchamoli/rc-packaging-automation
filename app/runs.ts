export {};

/// <reference path="globals.d.ts" />

// ── Types ───────────────────────────────────────────────────────────
interface PackagingRun {
    id: string;
    appName: string;
    version: string;
    status: string;
    startTime: string;
    endTime: string | null;
    sourceType: string;
    logUrl: string | null;
    errorSummary: string | null;
    artifactUrl: string | null;
    intuneAppId: string | null;
}

interface RunsApiResponse {
    runs: PackagingRun[];
}

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

function formatDateTime(iso: string | null): string {
    if (!iso) return '—';
    const date = new Date(iso);
    if (isNaN(date.getTime())) return '—';
    return date.toLocaleString(undefined, {
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });
}

function statusBadge(status: string): string {
    const base = 'inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium';
    switch (status) {
        case 'Succeeded':
            return `<span class="${base} bg-green-100 text-green-800">Succeeded</span>`;
        case 'SucceededWithWarnings':
            return `<span class="${base} bg-yellow-100 text-yellow-800">Warnings</span>`;
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

// ── Rendering ───────────────────────────────────────────────────────
function renderRuns(runs: PackagingRun[]): void {
    hideElement('loadingState');
    hideElement('errorState');

    if (runs.length === 0) {
        hideElement('tableContainer');
        showElement('emptyState');
        return;
    }

    hideElement('emptyState');
    showElement('tableContainer');

    const tbody = getEl<HTMLTableSectionElement>('runsTableBody');
    if (!tbody) return;

    tbody.innerHTML = runs.map(run => {
        const artifactCell = run.artifactUrl
            ? `<a href="${escapeHtml(run.artifactUrl)}" target="_blank" rel="noopener noreferrer" class="text-primary hover:text-primary-dark text-xs font-medium">Download</a>`
            : '<span class="text-neutral-400 text-xs">—</span>';

        const intuneCell = run.intuneAppId
            ? `<span class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-800">Created</span>`
            : '<span class="text-neutral-400 text-xs">—</span>';

        const errorRow = run.status === 'Failed' && run.errorSummary
            ? `<tr class="bg-red-50"><td colspan="9" class="px-4 py-2 text-xs text-red-600">${escapeHtml(run.errorSummary)}</td></tr>`
            : '';
        return `
        <tr class="hover:bg-neutral-50">
            <td class="px-4 py-3 text-sm">${statusBadge(run.status)}</td>
            <td class="px-4 py-3 text-neutral-700 text-sm font-medium">${escapeHtml(run.appName)}</td>
            <td class="px-4 py-3 text-neutral-700 text-sm">${escapeHtml(run.version)}</td>
            <td class="px-4 py-3 text-neutral-500 text-sm">${escapeHtml(run.sourceType)}</td>
            <td class="px-4 py-3 text-neutral-500 text-sm whitespace-nowrap">${formatDateTime(run.startTime)}</td>
            <td class="px-4 py-3 text-neutral-500 text-sm whitespace-nowrap">${formatDateTime(run.endTime)}</td>
            <td class="px-4 py-3 text-sm">${artifactCell}</td>
            <td class="px-4 py-3 text-sm">${intuneCell}</td>
            <td class="px-4 py-3 text-sm">
                <a href="/app/run-detail.html?id=${encodeURIComponent(run.id)}" class="text-primary hover:text-primary-dark font-medium">View →</a>
            </td>
        </tr>${errorRow}`;
    }).join('');
}

function escapeHtml(text: string): string {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function showError(message: string): void {
    hideElement('loadingState');
    hideElement('tableContainer');
    hideElement('emptyState');

    const errorMsg = getEl('errorMessage');
    if (errorMsg) errorMsg.textContent = message;
    showElement('errorState');
}

// ── Data Loading ────────────────────────────────────────────────────
async function loadRuns(appName?: string): Promise<void> {
    hideElement('errorState');
    hideElement('emptyState');
    hideElement('tableContainer');
    showElement('loadingState');

    try {
        let url = '/api/packaging/runs';
        if (appName && appName.trim()) {
            url += `?appName=${encodeURIComponent(appName.trim())}`;
        }

        const response = await fetch(url);
        if (!response.ok) {
            const errorData = await response.json().catch(() => null);
            throw new Error(errorData?.error || `HTTP ${response.status}`);
        }

        const data: RunsApiResponse = await response.json();
        renderRuns(data.runs);
    } catch (error) {
        const message = error instanceof Error ? error.message : 'An unexpected error occurred';
        showError(message);
    }
}

// ── Initialization ──────────────────────────────────────────────────
function init(): void {
    const filterInput = getEl<HTMLInputElement>('appNameFilter');
    const filterBtn = getEl<HTMLButtonElement>('filterBtn');
    const clearBtn = getEl<HTMLButtonElement>('clearFilterBtn');
    const retryBtn = getEl<HTMLButtonElement>('retryBtn');

    filterBtn?.addEventListener('click', () => {
        const appName = filterInput?.value?.trim() || '';
        if (appName) {
            clearBtn?.classList.remove('hidden');
        }
        loadRuns(appName || undefined);
    });

    clearBtn?.addEventListener('click', () => {
        if (filterInput) filterInput.value = '';
        clearBtn.classList.add('hidden');
        loadRuns();
    });

    retryBtn?.addEventListener('click', () => {
        const appName = filterInput?.value?.trim() || '';
        loadRuns(appName || undefined);
    });

    filterInput?.addEventListener('keydown', (e: KeyboardEvent) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            filterBtn?.click();
        }
    });

    // Load runs on page ready
    loadRuns();
}

// Wait for app shell ready signal (ensures auth is loaded)
if (window.appShellReady) {
    init();
} else {
    window.addEventListener('app-shell-ready', () => init());
}
