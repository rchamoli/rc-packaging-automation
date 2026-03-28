import { getEl, escapeHtml, formatDateTime, statusBadge } from './utils.js';

/// <reference path="globals.d.ts" />

// ── Types ───────────────────────────────────────────────────────────
interface DashboardStats {
    totalRuns: number;
    succeeded: number;
    failed: number;
    running: number;
    succeededWithWarnings: number;
    successRate: number;
    recentRuns: Array<{
        id: string;
        appName: string;
        version: string;
        status: string;
        startTime: string;
        endTime: string | null;
    }>;
}

// ── Dashboard Data ──────────────────────────────────────────────────
async function loadDashboard(): Promise<void> {
    try {
        const response = await fetch('/api/packaging/stats');
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const stats: DashboardStats = await response.json();

        // Populate stat cards
        const totalEl = getEl('statTotalRuns');
        if (totalEl) totalEl.textContent = String(stats.totalRuns);

        const succeededEl = getEl('statSucceeded');
        if (succeededEl) succeededEl.textContent = String(stats.succeeded);

        const failedEl = getEl('statFailed');
        if (failedEl) failedEl.textContent = String(stats.failed);

        const rateEl = getEl('statSuccessRate');
        if (rateEl) rateEl.textContent = stats.successRate + '%';

        // Render recent runs list
        renderRecentRuns(stats.recentRuns);
    } catch (error) {
        console.error('Failed to load dashboard stats:', error);
    }
}

function renderRecentRuns(runs: DashboardStats['recentRuns']): void {
    const list = getEl('recentRunsList');
    const empty = getEl('recentRunsEmpty');
    if (!list) return;

    if (runs.length === 0) {
        if (empty) empty.classList.remove('hidden');
        return;
    }
    if (empty) empty.classList.add('hidden');

    list.innerHTML = runs.map(run => `
        <a href="/app/run-detail.html?id=${encodeURIComponent(run.id)}"
           class="flex items-center justify-between px-4 py-3 hover:bg-neutral-50 transition border-b border-neutral-100 last:border-b-0">
            <div class="flex items-center gap-3 min-w-0">
                ${statusBadge(run.status)}
                <span class="text-sm font-medium text-neutral-900 truncate">${escapeHtml(run.appName)}</span>
                <span class="text-xs text-neutral-500">v${escapeHtml(run.version)}</span>
            </div>
            <span class="text-xs text-neutral-400 whitespace-nowrap ml-4">${formatDateTime(run.startTime)}</span>
        </a>
    `).join('');
}

// ── Activity Feed ───────────────────────────────────────────────────
interface ActivityEntry {
    eventType: string;
    userId: string;
    userDisplayName: string;
    description: string;
    runId: string | null;
    appName: string | null;
    occurredAt: string;
}

async function loadActivity(): Promise<void> {
    try {
        const response = await fetch('/api/activity');
        if (!response.ok) return;
        const data: { activities: ActivityEntry[] } = await response.json();
        renderActivity(data.activities);
    } catch {
        // Activity endpoint may not exist yet
    }
}

function eventIcon(eventType: string): string {
    switch (eventType) {
        case 'run.created':
            return '<svg class="w-4 h-4 text-blue-500" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4"></path></svg>';
        case 'run.completed':
            return '<svg class="w-4 h-4 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"></path></svg>';
        case 'intune.created':
            return '<svg class="w-4 h-4 text-purple-500" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4"></path></svg>';
        default:
            return '<svg class="w-4 h-4 text-neutral-400" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"></path></svg>';
    }
}

function relativeTime(iso: string): string {
    const now = Date.now();
    const then = new Date(iso).getTime();
    const diffMs = now - then;
    const minutes = Math.floor(diffMs / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    return `${days}d ago`;
}

function renderActivity(activities: ActivityEntry[]): void {
    const feed = getEl('activityFeed');
    const empty = getEl('activityEmpty');
    if (!feed) return;

    if (activities.length === 0) {
        if (empty) empty.classList.remove('hidden');
        return;
    }
    if (empty) empty.classList.add('hidden');

    feed.innerHTML = activities.slice(0, 20).map(a => `
        <div class="flex items-start gap-3 px-4 py-3 border-b border-neutral-100 last:border-b-0">
            <div class="flex-shrink-0 mt-0.5">${eventIcon(a.eventType)}</div>
            <div class="min-w-0 flex-1">
                <p class="text-sm text-neutral-700">${escapeHtml(a.description)}</p>
                <p class="text-xs text-neutral-400 mt-0.5">${escapeHtml(a.userDisplayName)} &middot; ${relativeTime(a.occurredAt)}</p>
            </div>
        </div>
    `).join('');
}

// ── Role-Based UI ───────────────────────────────────────────────────
function applyRoleVisibility(): void {
    const roles = window.userRoles ?? [];
    const isPackager = roles.includes('packager') || roles.includes('admin');

    document.querySelectorAll('[data-require-role="packager"]').forEach(el => {
        if (!isPackager) (el as HTMLElement).classList.add('hidden');
    });
}

// ── Init ────────────────────────────────────────────────────────────
function init(): void {
    loadDashboard();
    loadActivity();
    applyRoleVisibility();
}

if (window.appShellReady) {
    init();
} else {
    window.addEventListener('app-shell-ready', () => init());
}
