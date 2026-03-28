/// <reference path="globals.d.ts" />
// ── Shared DOM helpers ──────────────────────────────────────────────
export function getEl(id) {
    return document.getElementById(id);
}
export function showElement(id) {
    const el = getEl(id);
    if (el)
        el.classList.remove('hidden');
}
export function hideElement(id) {
    const el = getEl(id);
    if (el)
        el.classList.add('hidden');
}
// ── Shared formatting helpers ───────────────────────────────────────
export function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
export function formatDateTime(iso, includeSeconds = false) {
    if (!iso)
        return '—';
    const d = new Date(iso);
    if (isNaN(d.getTime()))
        return '—';
    const opts = {
        month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
    };
    if (includeSeconds)
        opts.second = '2-digit';
    return d.toLocaleString(undefined, opts);
}
export function statusBadge(status, size = 'sm') {
    const base = size === 'md'
        ? 'inline-flex items-center px-2.5 py-1 rounded-full text-xs font-semibold'
        : 'inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium';
    switch (status) {
        case 'Succeeded':
            return `<span class="${base} bg-green-100 text-green-800">Succeeded</span>`;
        case 'SucceededWithWarnings':
            return `<span class="${base} bg-yellow-100 text-yellow-800">Warnings</span>`;
        case 'Failed':
            return `<span class="${base} bg-red-100 text-red-800">Failed</span>`;
        case 'Running':
            return `<span class="${base} bg-blue-100 text-blue-800">Running</span>`;
        case 'Queued':
            return `<span class="${base} bg-indigo-100 text-indigo-800">Queued</span>`;
        default:
            return `<span class="${base} bg-neutral-100 text-neutral-800">${escapeHtml(status)}</span>`;
    }
}
//# sourceMappingURL=utils.js.map